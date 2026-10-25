using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Domain.Entities.Community;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace UniNet.Infrastructure.Data;

public sealed class UniNetDbContext(DbContextOptions<UniNetDbContext> options) : DbContext(options)
{
    private void GuardHistory()
    {
        if (ChangeTracker.Entries<AccountStatusChange>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Account status history cannot be changed or deleted.");
        foreach (var entry in ChangeTracker.Entries<VerificationNotification>().Where(e => e.State == EntityState.Added).ToList())
        {
            if (!Set<NotificationDelivery>().Local.Any(x => x.NotificationId == entry.Entity.Id && x.Reason == "created"))
                Set<NotificationDelivery>().Add(new() { AccountId = entry.Entity.RecipientUserId, NotificationId = entry.Entity.Id });
        }
        foreach (var entry in ChangeTracker.Entries<ProjectModeration>())
        {
            if (entry.State == EntityState.Modified && entry.Property(x => x.Purpose).IsModified)
                throw new InvalidOperationException("Moderation purpose cannot be changed.");
            if (entry.Entity.Purpose != ProjectModerationPurpose.ProjectEvidence) continue;
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Evidence moderation history cannot be deleted.");
            if (entry.State == EntityState.Modified && new[] { nameof(ProjectModeration.ProjectId), nameof(ProjectModeration.Version),
                nameof(ProjectModeration.SourceHash), nameof(ProjectModeration.SnapshotJson), nameof(ProjectModeration.PolicyHash),
                nameof(ProjectModeration.PolicyJson), nameof(ProjectModeration.CreatedAt) }.Any(p => entry.Property(p).IsModified))
                throw new InvalidOperationException("Evidence moderation snapshots cannot be changed.");
        }
        if (ChangeTracker.Entries<VerificationReport>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Submitted verification reports cannot be changed or deleted.");
    }
    private List<EntityEntry<VerificationNotification>> KeyedNotifications() => Database.IsNpgsql()
        ? ChangeTracker.Entries<VerificationNotification>().Where(e => e.State == EntityState.Added && e.Entity.EventKey != null)
            .OrderBy(e => e.Entity.RecipientUserId).ThenBy(e => e.Entity.EventKey, StringComparer.Ordinal).ToList() : [];

    private static long NotificationLock(VerificationNotification n) => BinaryPrimitives.ReadInt64LittleEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes($"notification:{n.RecipientUserId:N}:{n.EventKey}")));
    private void DetachDuplicate(EntityEntry<VerificationNotification> entry)
    {
        foreach (var delivery in ChangeTracker.Entries<NotificationDelivery>().Where(e => e.State == EntityState.Added &&
            e.Entity.NotificationId == entry.Entity.Id && e.Entity.Reason == "created").ToList()) delivery.State = EntityState.Detached;
        entry.State = EntityState.Detached;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var entries = KeyedNotifications();
        using var owned = entries.Count > 0 && Database.CurrentTransaction == null ? Database.BeginTransaction() : null;
        foreach (var entry in entries) {
            var n = entry.Entity; var key = NotificationLock(n);
            Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock({key})");
            if (Set<VerificationNotification>().AsNoTracking().Any(x => x.RecipientUserId == n.RecipientUserId && x.EventKey == n.EventKey)) DetachDuplicate(entry);
        }
        GuardHistory(); var result = base.SaveChanges(acceptAllChangesOnSuccess); owned?.Commit(); return result;
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        var entries = KeyedNotifications();
        await using var owned = entries.Count > 0 && Database.CurrentTransaction == null ? await Database.BeginTransactionAsync(ct) : null;
        foreach (var entry in entries) {
            var n = entry.Entity; var key = NotificationLock(n);
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            if (await Set<VerificationNotification>().AsNoTracking().AnyAsync(x => x.RecipientUserId == n.RecipientUserId && x.EventKey == n.EventKey, ct)) DetachDuplicate(entry);
        }
        GuardHistory(); var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        if (owned != null) await owned.CommitAsync(ct); return result;
    }

    public DbSet<PortraitAsset> PortraitAssets => Set<PortraitAsset>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<VerificatorAvailability> VerificatorAvailabilities => Set<VerificatorAvailability>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserProfile> Users => Set<UserProfile>();
    public DbSet<UserVerification> UserVerifications => Set<UserVerification>();
    public DbSet<CareerProfile> CareerProfiles => Set<CareerProfile>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectRoleRequirement> ProjectRoleRequirements => Set<ProjectRoleRequirement>();
    public DbSet<ProjectSkill> ProjectSkills => Set<ProjectSkill>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<WorkspaceChannel> WorkspaceChannels => Set<WorkspaceChannel>();
    public DbSet<WorkspaceMessage> WorkspaceMessages => Set<WorkspaceMessage>();
    public DbSet<ProjectJoinRequest> ProjectJoinRequests => Set<ProjectJoinRequest>();
    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ProjectTaskAssignment> ProjectTaskAssignments => Set<ProjectTaskAssignment>();
    public DbSet<TaskReport> TaskReports => Set<TaskReport>();
    public DbSet<TaskEvidence> TaskEvidence => Set<TaskEvidence>();
    public DbSet<TaskReportReview> TaskReportReviews => Set<TaskReportReview>();
    public DbSet<ContributionCycle> ContributionCycles => Set<ContributionCycle>();
    public DbSet<ContributionParticipant> ContributionParticipants => Set<ContributionParticipant>();
    public DbSet<ContributionPeerEvaluation> ContributionPeerEvaluations => Set<ContributionPeerEvaluation>();
    public DbSet<ContributionPeerEvaluationHistory> ContributionPeerEvaluationHistory => Set<ContributionPeerEvaluationHistory>();
    public DbSet<ContributionCalculationRun> ContributionCalculationRuns => Set<ContributionCalculationRun>();
    public DbSet<ContributionResult> ContributionResults => Set<ContributionResult>();
    public DbSet<ContributionReview> ContributionReviews => Set<ContributionReview>();
    public DbSet<ProjectModeration> ProjectModerations => Set<ProjectModeration>();
    public DbSet<EvidenceModerationAttempt> EvidenceModerationAttempts => Set<EvidenceModerationAttempt>();
    public DbSet<UserSkill> UserSkills => Set<UserSkill>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<UserBookmark> UserBookmarks => Set<UserBookmark>();
    public DbSet<StudentSavedJob> StudentSavedJobs => Set<StudentSavedJob>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ProjectVerification> ProjectVerifications => Set<ProjectVerification>();
    public DbSet<ProjectVerificationEvidence> ProjectVerificationEvidence => Set<ProjectVerificationEvidence>();
    public DbSet<VerificationAssignment> VerificationAssignments => Set<VerificationAssignment>();
    public DbSet<VerificationReport> VerificationReports => Set<VerificationReport>();
    public DbSet<ReportAuditLog> ReportAuditLogs => Set<ReportAuditLog>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<RefundTransaction> RefundTransactions => Set<RefundTransaction>();
    public DbSet<VerificationTransaction> VerificationTransactions => Set<VerificationTransaction>();
    public DbSet<CommunityPost> CommunityPosts => Set<CommunityPost>();
    public DbSet<CommunityPostMedia> CommunityPostMedia => Set<CommunityPostMedia>();
    public DbSet<CommunityPostComment> CommunityPostComments => Set<CommunityPostComment>();
    public DbSet<CommunityPostReaction> CommunityPostReactions => Set<CommunityPostReaction>();
    public DbSet<CommunityPostBookmark> CommunityPostBookmarks => Set<CommunityPostBookmark>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();
    public DbSet<AdvertisementCampaign> AdvertisementCampaigns => Set<AdvertisementCampaign>();
    public DbSet<AdvertisementTransaction> AdvertisementTransactions => Set<AdvertisementTransaction>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        AdminManagementConfiguration.Configure(model);
        model.ApplyConfiguration(new VerificationNotificationConfiguration());
        NotificationConfiguration.Configure(model);
        model.ApplyConfiguration(new UnifiedProjectModerationConfiguration());
        model.ApplyConfiguration(new EvidenceModerationAttemptConfiguration());
        model.Entity<PortraitAsset>(e =>
        {
            e.ToTable("PortraitAssets"); e.HasKey(x => x.Id);
            e.Property(x => x.PublicId).HasMaxLength(255); e.HasIndex(x => x.PublicId).IsUnique();
            e.Property(x => x.ImageUrl).HasMaxLength(2048); e.HasIndex(x => x.ImageUrl).IsUnique();
            e.Property(x => x.State).IsConcurrencyToken(); e.HasIndex(x => new { x.State, x.CreatedAtUnix });
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Account>(e =>
        {
            e.ToTable("Accounts", t => { t.HasCheckConstraint("CK_Accounts_Role", "\"Role\" BETWEEN 0 AND 4"); t.HasCheckConstraint("CK_Accounts_Status", "\"Status\" BETWEEN 0 AND 3"); }); e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired(); e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Role).HasDatabaseName("IX_Accounts_SingleAdmin")
                .IsUnique().HasFilter("\"Role\" = 2");
            e.Property(x => x.PasswordHash).HasColumnType("text");
            e.Property(x => x.GoogleId).HasMaxLength(255); e.HasIndex(x => x.GoogleId).IsUnique().HasFilter("\"GoogleId\" IS NOT NULL");
            e.Property(x => x.GoogleEmail).HasMaxLength(255);
            e.Property(x => x.Role).HasConversion<short>(); e.Property(x => x.Status).HasConversion<short>();
        });
        model.Entity<VerificatorAvailability>(e =>
        {
            e.ToTable("VerificatorAvailabilities", t => t.HasCheckConstraint(
                "CK_VerificatorAvailabilities_Interval", "\"EndAt\" > \"StartAt\""));
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.StartAt, x.EndAt });
            e.HasOne(x => x.User).WithMany(x => x.VerificatorAvailabilities).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProjectVerification>(e =>
        {
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ReservedVerificatorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserProfile>(e =>
        {
            e.ToTable("Users", t => t.HasCheckConstraint("CK_Users_PartnerType", "\"PartnerType\" IS NULL OR \"PartnerType\" BETWEEN 0 AND 4")); e.HasKey(x => x.Id);
            e.HasIndex(x => x.AccountId).IsUnique();
            e.HasOne(x => x.Account).WithOne(x => x.Profile).HasForeignKey<UserProfile>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.FullName).HasMaxLength(255).IsRequired();
            e.Property(x => x.Nickname).HasMaxLength(100).IsRequired();
            e.Property(x => x.OrganizationName).HasMaxLength(255);
            e.HasIndex(x => new { x.UniversityName, x.Major });
            e.Property(x => x.UniversityName).HasMaxLength(255); e.Property(x => x.Major).HasMaxLength(255);
            e.Property(x => x.StudentCode).HasMaxLength(50); e.Property(x => x.PartnerType).HasConversion<short?>();
            e.Property(x => x.Industry).HasMaxLength(150); e.Property(x => x.ContactEmail).HasMaxLength(255);
            e.Property(x => x.Phone).HasMaxLength(30);
        });
        model.Entity<UserVerification>(e =>
        {
            e.ToTable("UserVerifications", t =>
            {
                t.HasCheckConstraint("CK_UserVerifications_Type", "\"VerificationType\" BETWEEN 0 AND 1");
                t.HasCheckConstraint("CK_UserVerifications_Status", "\"Status\" BETWEEN 0 AND 3");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.VerificationType).HasConversion<short>();
            e.Property(x => x.Status).HasConversion<short>();
            e.Property(x => x.VerificationData).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.DocumentUrls).HasColumnType("jsonb");
            e.HasOne(x => x.User).WithMany(x => x.Verifications).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.Status });
        });
        model.Entity<CareerProfile>(e =>
        {
            e.ToTable("CareerProfiles"); e.HasKey(x => x.Id);
            e.HasOne(x => x.User).WithOne(x => x.CareerProfile).HasForeignKey<CareerProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Headline).HasMaxLength(255);
            foreach (var name in new[] { nameof(CareerProfile.BasicInfoJson), nameof(CareerProfile.EducationJson), nameof(CareerProfile.SkillsJson), nameof(CareerProfile.ExperienceJson), nameof(CareerProfile.ProjectsJson), nameof(CareerProfile.CertificatesJson), nameof(CareerProfile.ActivitiesJson), nameof(CareerProfile.LanguagesJson), nameof(CareerProfile.SocialLinksJson), nameof(CareerProfile.AppearanceJson) })
                e.Property<string?>(name).HasColumnType("jsonb");
            e.HasIndex(x => x.SkillsJson).HasMethod("gin");
            e.HasIndex(x => x.ProjectsJson).HasMethod("gin");
        });
        model.Entity<Skill>(e =>
        {
            e.ToTable("Skills", t => t.HasCheckConstraint("CK_Skills_Category", "\"Category\" IS NULL OR \"Category\" BETWEEN 0 AND 3")); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Category).HasConversion<short>();
            e.HasIndex(x => new { x.Category, x.IsActive });
        });
        model.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens"); e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasColumnType("text").IsRequired(); e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.AccountId); e.HasIndex(x => x.ExpiresAt);
            e.Property(x => x.DeviceId).HasMaxLength(255); e.Property(x => x.DeviceName).HasMaxLength(255);
            e.HasOne(x => x.Account).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Project>(e =>
        {
            e.ToTable("Projects", t =>
            {
                t.HasCheckConstraint(
                    "CK_Projects_MemberTarget",
                    "\"MemberTarget\" > 0");

                t.HasCheckConstraint("CK_Projects_MinimumQuantity",
                    "\"MinimumQuantity\" >= 1 AND \"MinimumQuantity\" <= \"MemberTarget\"");

                t.HasCheckConstraint(
                    "CK_Projects_Status",
                    "\"Status\" BETWEEN 0 AND 3");
                t.HasCheckConstraint(
                    "CK_Projects_Visibility",
                    "\"Visibility\" BETWEEN 0 AND 1");

                t.HasCheckConstraint(
                    "CK_Projects_RecruitmentStatus",
                    "\"RecruitmentStatus\" BETWEEN 0 AND 3");
            });

            e.HasKey(x => x.Id);

            e.Property(x => x.Title)
                .HasMaxLength(255)
                .IsRequired();

            e.Property(x => x.ProjectField)
                .HasMaxLength(150)
                .IsRequired();

            e.Property(x => x.Description)
                .HasColumnType("text")
                .IsRequired();

            e.Property(x => x.MemberTarget)
                .IsRequired();

            e.Property(x => x.RecruitmentDeadline)
                .IsRequired();

            e.Property(x => x.ExpectedOutput);

            e.Property(x => x.Visibility)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.Status)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.RecruitmentStatus)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            e.Property(x => x.CompletedAt);

            // Creator / Leader
            e.HasOne(x => x.Creator)
                .WithMany()
                .HasForeignKey(x => x.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            e.HasIndex(x => x.CreatorId);

            e.HasIndex(x => new
            {
                x.Visibility,
                x.Status,
                x.RecruitmentStatus
            });

            e.HasIndex(x => x.ProjectField);

            e.HasIndex(x => x.RecruitmentDeadline);

            e.HasOne(x => x.ContributionCycle)
                .WithOne(x => x.Project)
                .HasForeignKey<ContributionCycle>(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProjectRoleRequirement>(e =>
        {
            e.ToTable("ProjectRoleRequirements", t =>
            {
                t.HasCheckConstraint(
                    "CK_ProjectRoleRequirements_Quantity",
                    "\"Quantity\" > 0");
            });

            e.HasKey(x => x.Id);

            e.Property(x => x.Role)
                .HasMaxLength(150)
                .IsRequired();

            e.Property(x => x.Quantity)
                .IsRequired();

            e.Property(x => x.Requirements)
                .HasColumnType("text")
                .IsRequired();

            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            e.HasOne(x => x.Project)
                .WithMany(x => x.RoleRequirements)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.ProjectId);
        });
        model.Entity<ProjectSkill>(e =>
        {
            e.ToTable("ProjectSkills");

            e.HasKey(x => new { x.ProjectId, x.SkillId });

            e.HasOne(x => x.Project)
                .WithMany(x => x.ProjectSkills)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Skill)
                .WithMany(x => x.ProjectSkills)
                .HasForeignKey(x => x.SkillId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProjectMember>(e =>
        {
            e.ToTable("ProjectMembers", t =>
            {
                t.HasCheckConstraint(
                    "CK_ProjectMembers_Status",
                    "\"Status\" BETWEEN 0 AND 2");
            });

            e.HasKey(x => x.Id);

            e.Property(x => x.Role)
                .HasMaxLength(150)
                .IsRequired();

            e.Property(x => x.Status)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.JoinedAt)
                .IsRequired();

            e.Property(x => x.LeftAt);

            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            // Project
            e.HasOne(x => x.Project)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // User
            e.HasOne(x => x.User)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Unique(ProjectId, UserId)
            e.HasIndex(x => new
            {
                x.ProjectId,
                x.UserId
            }).IsUnique();

            e.HasIndex(x => new
            {
                x.ProjectId,
                x.Status
            });

            e.HasIndex(x => new
            {
                x.UserId,
                x.Status
            });
        });
        model.Entity<WorkspaceChannel>(e =>
        {
            e.ToTable("WorkspaceChannels", t => t.HasCheckConstraint(
                "CK_WorkspaceChannels_DeletedMetadata",
                "(\"IsDeleted\" = FALSE AND \"DeletedAt\" IS NULL AND \"DeletedById\" IS NULL) OR " +
                "(\"IsDeleted\" = TRUE AND \"DeletedAt\" IS NOT NULL AND \"DeletedById\" IS NOT NULL)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
                e.Property(x => x.CreatedAt).HasConversion<long>(
                    value => value.UtcTicks,
                    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            e.HasOne(x => x.Project)
                .WithMany(x => x.WorkspaceChannels)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CreatedBy)
                .WithMany()
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DeletedBy)
                .WithMany()
                .HasForeignKey(x => x.DeletedById)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProjectId, x.IsDeleted });
        });
        model.Entity<WorkspaceMessage>(e =>
        {
            e.ToTable("WorkspaceMessages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).HasMaxLength(4000).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
                e.Property(x => x.CreatedAt).HasConversion<long>(
                    value => value.UtcTicks,
                    ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
            e.HasOne(x => x.Channel)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Sender)
                .WithMany()
                .HasForeignKey(x => x.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ChannelId, x.CreatedAt, x.Id });
        });
        model.Entity<ProjectJoinRequest>(e =>
        {
            e.ToTable("ProjectJoinRequests", t =>
            {
                t.HasCheckConstraint(
                    "CK_ProjectJoinRequests_Status",
                    "\"Status\" BETWEEN 0 AND 3");
            });

            e.HasKey(x => x.Id);

            e.Property(x => x.Role)
                .HasMaxLength(150)
                .IsRequired();

            e.Property(x => x.Message)
                .HasColumnType("text");

            e.Property(x => x.Status)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            // Project
            e.HasOne(x => x.Project)
                .WithMany(x => x.JoinRequests)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // User
            e.HasOne(x => x.User)
                .WithMany(x => x.JoinRequests)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new
            {
                x.ProjectId,
                x.Status
            });

            e.HasIndex(x => new
            {
                x.UserId,
                x.Status
            });
        });
        model.Entity<ProjectInvitation>(e =>
        {
            e.ToTable("ProjectInvitations", t =>
            {
                t.HasCheckConstraint(
                    "CK_ProjectInvitations_Status",
                    "\"Status\" BETWEEN 0 AND 4");
            });

            e.HasKey(x => x.Id);

            e.Property(x => x.Role)
                .HasMaxLength(150)
                .IsRequired();

            e.Property(x => x.Message)
                .HasColumnType("text");

            e.Property(x => x.Status)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            // Project
            e.HasOne(x => x.Project)
                .WithMany(x => x.Invitations)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            // Person who sends the invitation
            e.HasOne(x => x.Inviter)
                .WithMany(x => x.SentInvitations)
                .HasForeignKey(x => x.InviterId)
                .OnDelete(DeleteBehavior.Restrict);

            // Person who receives the invitation
            e.HasOne(x => x.Invitee)
                .WithMany(x => x.ReceivedInvitations)
                .HasForeignKey(x => x.InviteeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new
            {
                x.ProjectId,
                x.Status
            });

            e.HasIndex(x => new
            {
                x.InviteeId,
                x.Status
            });
        });
        model.Entity<ProjectTask>(e =>
        {
            e.ToTable("ProjectTasks", t =>
            {
                t.HasCheckConstraint("CK_ProjectTasks_Status", "\"Status\" BETWEEN 0 AND 4");
            });

            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(255).IsRequired();
            e.Property(x => x.Description).HasColumnType("text").IsRequired();
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.Deadline).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Creator)
                .WithMany()
                .HasForeignKey(x => x.CreatorId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.CreatorId);
            e.HasIndex(x => x.Deadline);
        });

        model.Entity<ProjectTaskAssignment>(e =>
        {
            e.ToTable("ProjectTaskAssignments");
            e.HasKey(x => x.Id);
            e.Property(x => x.AssignedAt).IsRequired();

            e.HasOne(x => x.ProjectTask)
                .WithMany(x => x.Assignments)
                .HasForeignKey(x => x.ProjectTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.ProjectTaskId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });
        model.Entity<TaskReport>(e =>
        {
            e.ToTable("TaskReports", t =>
            {
                t.HasCheckConstraint("CK_TaskReports_Status", "\"Status\" BETWEEN 0 AND 4");
                t.HasCheckConstraint("CK_TaskReports_Version", "\"Version\" > 0");
                t.HasCheckConstraint(
                    "CK_TaskReports_DeletedMetadata",
                    "(\"IsDeleted\" = FALSE AND \"DeletedAt\" IS NULL AND \"DeletedById\" IS NULL) OR " +
                    "(\"IsDeleted\" = TRUE AND \"DeletedAt\" IS NOT NULL AND \"DeletedById\" IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).HasMaxLength(5000).IsRequired();
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.SubmittedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            e.Property(x => x.IsDeleted).IsRequired();

            e.HasOne(x => x.ProjectTask)
                .WithMany(x => x.Reports)
                .HasForeignKey(x => x.ProjectTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.SubmittedBy)
                .WithMany()
                .HasForeignKey(x => x.SubmittedById)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ParentReport)
                .WithMany(x => x.ChildReports)
                .HasForeignKey(x => x.ParentReportId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.DeletedBy)
                .WithMany()
                .HasForeignKey(x => x.DeletedById)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.ReportGroupId, x.Version }).IsUnique();
            e.HasIndex(x => new { x.ProjectTaskId, x.SubmittedAt });
        });
        model.Entity<TaskEvidence>(e =>
        {
            e.ToTable("TaskEvidence");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileUrl).HasMaxLength(2048).IsRequired();
            e.Property(x => x.PublicId).HasMaxLength(255);
            e.Property(x => x.ResourceType).HasMaxLength(10);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.Format).HasMaxLength(10);
            e.Property(x => x.CreatedAt).IsRequired();

            e.HasOne(x => x.TaskReport)
                .WithMany(x => x.Evidence)
                .HasForeignKey(x => x.TaskReportId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.TaskReportId);
        });
        model.Entity<TaskReportReview>(e =>
        {
            e.ToTable("TaskReportReviews", t =>
                t.HasCheckConstraint("CK_TaskReportReviews_Decision", "\"Decision\" BETWEEN 0 AND 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Decision).HasConversion<short>().IsRequired();
            e.Property(x => x.Feedback).HasMaxLength(2000);
            e.Property(x => x.ReviewedAt).IsRequired();

            e.HasOne(x => x.TaskReport)
                .WithMany(x => x.Reviews)
                .HasForeignKey(x => x.TaskReportId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Reviewer)
                .WithMany()
                .HasForeignKey(x => x.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.TaskReportId).IsUnique();
            e.HasIndex(x => x.ReviewerId);
        });
        model.Entity<ContributionCycle>(e =>
        {
            e.ToTable("ContributionCycles", t =>
            {
                t.HasCheckConstraint("CK_ContributionCycles_Status", "\"Status\" BETWEEN 0 AND 2");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.OpenedAt).IsRequired();
            e.Property(x => x.FormulaVersion).HasMaxLength(32).IsRequired();
            e.HasOne(x => x.OpenedBy).WithMany().HasForeignKey(x => x.OpenedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProjectId).IsUnique();
        });
        model.Entity<ContributionParticipant>(e =>
        {
            e.ToTable("ContributionParticipants", t =>
            {
                t.HasCheckConstraint("CK_ContributionParticipants_MembershipStatus", "\"MembershipStatusAtSnapshot\" BETWEEN 0 AND 2");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.RoleAtSnapshot).HasMaxLength(150).IsRequired();
            e.Property(x => x.MembershipStatusAtSnapshot).HasConversion<short>().IsRequired();
            e.Property(x => x.JoinedAt).IsRequired();
            e.HasOne(x => x.Cycle).WithMany(x => x.Participants).HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CycleId, x.UserId }).IsUnique();
        });
        model.Entity<ContributionPeerEvaluation>(e =>
        {
            e.ToTable("ContributionPeerEvaluations", t =>
            {
                t.HasCheckConstraint("CK_ContributionPeerEvaluations_Score", "\"Score\" BETWEEN 1 AND 5");
                t.HasCheckConstraint("CK_ContributionPeerEvaluations_NoSelf", "\"EvaluatorUserId\" <> \"EvaluateeUserId\"");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Property(x => x.SubmittedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasOne(x => x.Cycle).WithMany(x => x.PeerEvaluations).HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContributionParticipant>().WithMany()
                .HasForeignKey(x => new { x.CycleId, x.EvaluatorUserId })
                .HasPrincipalKey(x => new { x.CycleId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContributionParticipant>().WithMany()
                .HasForeignKey(x => new { x.CycleId, x.EvaluateeUserId })
                .HasPrincipalKey(x => new { x.CycleId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Evaluator).WithMany().HasForeignKey(x => x.EvaluatorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Evaluatee).WithMany().HasForeignKey(x => x.EvaluateeUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CycleId, x.EvaluatorUserId, x.EvaluateeUserId }).IsUnique();
            e.HasIndex(x => new { x.CycleId, x.EvaluateeUserId });
        });
        model.Entity<ContributionPeerEvaluationHistory>(e =>
        {
            e.ToTable("ContributionPeerEvaluationHistory", t =>
            {
                t.HasCheckConstraint("CK_ContributionPeerEvaluationHistory_Action", "\"Action\" BETWEEN 0 AND 1");
                t.HasCheckConstraint("CK_ContributionPeerEvaluationHistory_NewScore", "\"NewScore\" BETWEEN 1 AND 5");
                t.HasCheckConstraint("CK_ContributionPeerEvaluationHistory_PreviousScore", "\"PreviousScore\" IS NULL OR \"PreviousScore\" BETWEEN 1 AND 5");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasConversion<short>().IsRequired();
            e.Property(x => x.PreviousComment).HasMaxLength(2000);
            e.Property(x => x.NewComment).HasMaxLength(2000);
            e.Property(x => x.ChangedAt).IsRequired();
            e.HasOne(x => x.ContributionPeerEvaluation).WithMany(x => x.History)
                .HasForeignKey(x => x.ContributionPeerEvaluationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContributionCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContributionParticipant>().WithMany()
                .HasForeignKey(x => new { x.CycleId, x.EvaluatorUserId })
                .HasPrincipalKey(x => new { x.CycleId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContributionParticipant>().WithMany()
                .HasForeignKey(x => new { x.CycleId, x.EvaluateeUserId })
                .HasPrincipalKey(x => new { x.CycleId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CycleId, x.EvaluatorUserId, x.EvaluateeUserId, x.ChangedAt });
        });
        model.Entity<ContributionCalculationRun>(e =>
        {
            e.ToTable("ContributionCalculationRuns", t =>
            {
                t.HasCheckConstraint("CK_ContributionCalculationRuns_RunNumber", "\"RunNumber\" > 0");
                t.HasCheckConstraint("CK_ContributionCalculationRuns_Status", "\"Status\" BETWEEN 0 AND 2");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.FormulaVersion).HasMaxLength(32).IsRequired();
            e.Property(x => x.InputSnapshot).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.InputHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.WarningsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.CalculatedAt).IsRequired();
            e.HasOne(x => x.Cycle).WithMany(x => x.CalculationRuns).HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CalculatedBy).WithMany().HasForeignKey(x => x.CalculatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(x => new { x.Id, x.CycleId });
            e.HasIndex(x => new { x.CycleId, x.RunNumber }).IsUnique();
            e.HasIndex(x => new { x.CycleId, x.Status });
        });
        model.Entity<ContributionResult>(e =>
        {
            e.ToTable("ContributionResults", t =>
            {
                t.HasCheckConstraint("CK_ContributionResults_Scores",
                    "CAST(\"TaskExecutionScore\" AS NUMERIC) BETWEEN 0 AND 100 AND " +
                    "CAST(\"ApprovedDeliverablesScore\" AS NUMERIC) BETWEEN 0 AND 100 AND " +
                    "CAST(\"PeerEvaluationScore\" AS NUMERIC) BETWEEN 0 AND 100 AND " +
                    "CAST(\"TotalScore\" AS NUMERIC) BETWEEN 0 AND 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.TaskExecutionScore).HasPrecision(5, 2).IsRequired();
            e.Property(x => x.ApprovedDeliverablesScore).HasPrecision(5, 2).IsRequired();
            e.Property(x => x.PeerEvaluationScore).HasPrecision(5, 2).IsRequired();
            e.Property(x => x.TotalScore).HasPrecision(5, 2).IsRequired();
            e.Property(x => x.CalculationDetails).HasColumnType("jsonb").IsRequired();
            e.HasOne(x => x.Run).WithMany(x => x.Results)
                .HasForeignKey(x => new { x.RunId, x.CycleId })
                .HasPrincipalKey(x => new { x.Id, x.CycleId }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContributionParticipant>().WithMany()
                .HasForeignKey(x => new { x.CycleId, x.UserId })
                .HasPrincipalKey(x => new { x.CycleId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RunId, x.UserId }).IsUnique();
        });
        model.Entity<ContributionReview>(e =>
        {
            e.ToTable("ContributionReviews", t =>
            {
                t.HasCheckConstraint("CK_ContributionReviews_Decision", "\"Decision\" BETWEEN 0 AND 1");
                t.HasCheckConstraint("CK_ContributionReviews_RevisionFeedback",
                    "\"Decision\" <> 1 OR length(trim(\"Feedback\")) > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Decision).HasConversion<short>().IsRequired();
            e.Property(x => x.Feedback).HasMaxLength(2000);
            e.Property(x => x.ReviewedAt).IsRequired();
            e.HasOne(x => x.Run).WithMany(x => x.Reviews).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Reviewer).WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.RunId).IsUnique();
            e.HasIndex(x => x.ReviewerId);
        });
        model.Entity<ProjectModeration>(e =>
        {
            e.ToTable("ProjectModerations", t =>
            {
                t.HasCheckConstraint(
                    "CK_ProjectModerations_Status",
                    "(\"Purpose\" = 0 AND \"Status\" BETWEEN 0 AND 5) OR (\"Purpose\" = 1 AND \"Status\" IN (0,1,4,5,6,7))");

                t.HasCheckConstraint(
                    "CK_ProjectModerations_ContentResult",
                    "\"ContentResult\" BETWEEN 0 AND 3");

                t.HasCheckConstraint(
                    "CK_ProjectModerations_LinkResult",
                    "\"LinkResult\" BETWEEN 0 AND 3");

                t.HasCheckConstraint(
                    "CK_ProjectModerations_Confidence",
                    "\"Confidence\" IS NULL OR (\"Confidence\" >= 0 AND \"Confidence\" <= 1)");

                t.HasCheckConstraint(
                    "CK_ProjectModerations_AttemptNumber",
                    "\"Purpose\" = 1 OR \"AttemptNumber\" > 0");
            });

            // Primary Key
            e.HasKey(x => x.Id);

            // Attempt number
            e.Property(x => x.AttemptNumber)
                .IsRequired();

            // Status
            e.Property(x => x.Status)
                .HasConversion<short>()
                .IsRequired();

            // AI screening results
            e.Property(x => x.ContentResult)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.LinkResult)
                .HasConversion<short>()
                .IsRequired();

            // AI confidence: 0.000 - 1.000
            e.Property(x => x.Confidence)
                .HasPrecision(4, 3);

            // AI violation reason
            e.Property(x => x.ViolationReason)
                .HasColumnType("text");

            // Raw AI response
            e.Property(x => x.ResultJson)
                .HasColumnType("jsonb");

            // AI engine/model version
            e.Property(x => x.EngineVersion)
                .HasMaxLength(100);

            // Hash of project content at moderation time
            e.Property(x => x.ContentHash)
                .HasMaxLength(64)
                .IsRequired();

            // AI processing timestamp
            e.Property(x => x.CheckedAt);

            // AI retry count
            e.Property(x => x.RetryCount)
                .HasDefaultValue(0)
                .IsRequired();

            // Human Moderator review
            e.Property(x => x.ReviewNote)
                .HasColumnType("text");

            e.Property(x => x.ReviewAt);

            // Audit timestamps
            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            // Project 1 : N ProjectModeration
            e.HasOne(x => x.Project)
                .WithMany(x => x.Moderations)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // Moderator/UserProfile 1 : N ProjectModeration
            e.HasOne(x => x.ReviewedBy)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Submission attempt sequence is independent of evidence source versions
            e.HasIndex(x => new
            {
                x.ProjectId,
                x.AttemptNumber
            })
            .IsUnique().HasFilter("\"Purpose\" = 0");

            // Useful for finding moderation history / latest attempt
            e.HasIndex(x => new
            {
                x.ProjectId,
                x.CreatedAt
            });
        });
        model.Entity<Job>(e =>
        {
            e.ToTable("Jobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(255).IsRequired();
            e.Property(x => x.WorkType).HasColumnType("smallint");
            e.Property(x => x.MinSalaryVnd).HasPrecision(14, 0);
            e.Property(x => x.MaxSalaryVnd).HasPrecision(14, 0);
            e.Property(x => x.Location).HasMaxLength(255);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.RequiredJson).HasColumnType("jsonb");
            e.Property(x => x.BenefitsJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnType("smallint");
            e.HasOne(x => x.Partner).WithMany(x => x.PostedJobs).HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.PartnerId);
            e.HasIndex(x => new { x.Status, x.Deadline });
        });
        model.Entity<JobSkill>(e =>
        {
            e.ToTable("JobSkills");
            e.HasKey(x => new { x.JobId, x.SkillId });
            e.Property(x => x.RequirementType).HasColumnType("smallint");
            e.HasOne(x => x.Job).WithMany(x => x.JobSkills).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Skill).WithMany(x => x.JobSkills).HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SkillId);
        });
        model.Entity<UserBookmark>(e =>
        {
            e.ToTable("UserBookmarks");
            e.HasKey(x => new { x.UserId, x.BookmarkedUserId });
            e.Property(x => x.Note).HasColumnType("text");
            e.HasOne(x => x.User).WithMany(x => x.Bookmarks).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.BookmarkedUser).WithMany(x => x.BookmarkedBy).HasForeignKey(x => x.BookmarkedUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.BookmarkedUserId);
        });
        model.Entity<StudentSavedJob>(e =>
        {
            e.ToTable("StudentSavedJobs");
            e.HasKey(x => new { x.StudentUserId, x.JobId });
            e.HasOne(x => x.Student).WithMany(x => x.SavedJobs).HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Job).WithMany(x => x.SavedByStudents).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.JobId);
        });
        model.Entity<JobApplication>(e =>
        {
            e.ToTable("JobApplications");
            e.HasKey(x => x.Id);
            e.Property(x => x.CoverMessage).HasMaxLength(300);
            e.Property(x => x.AiMatchScore).HasPrecision(5, 2);
            e.Property(x => x.ProfileSnapshot).HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnType("smallint");
            e.HasOne(x => x.Job).WithMany(x => x.Applications).HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany(x => x.JobApplications).HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.JobId, x.Status });
            e.HasIndex(x => new { x.StudentUserId, x.AppliedAt });
            e.HasIndex(x => new { x.StudentUserId, x.JobId }).IsUnique().HasFilter("\"Status\" <> 4");
        });
        model.Entity<ProjectVerification>(e =>
        {
            e.ToTable("ProjectVerifications", t =>
            {
                t.HasCheckConstraint("CK_ProjectVerifications_Status", "\"Status\" BETWEEN 0 AND 3");
                t.HasCheckConstraint("CK_ProjectVerifications_BookingStatus", "\"BookingStatus\" BETWEEN 0 AND 5");
                t.HasCheckConstraint("CK_ProjectVerifications_Reservation", "\"BookingStatus\" NOT IN (1, 2) OR (\"ReservationId\" IS NOT NULL AND \"ReservedUntil\" IS NOT NULL AND \"ScheduledStartAt\" IS NOT NULL AND \"ScheduledEndAt\" IS NOT NULL AND \"ScheduledEndAt\" > \"ScheduledStartAt\")");
                t.HasCheckConstraint("CK_ProjectVerifications_ConfirmedBooking", "\"BookingStatus\" <> 2 OR (\"BookingConfirmedAt\" IS NOT NULL AND \"AssignmentEligibleAt\" IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property<ProjectModerationPurpose?>("EvidenceModerationPurpose").IsRequired().HasConversion<short>()
                .HasDefaultValue(ProjectModerationPurpose.ProjectEvidence).HasSentinel(null);
            e.HasOne(x => x.EvidenceModeration).WithMany()
                .HasForeignKey("EvidenceModerationId", "EvidenceModerationPurpose")
                .HasPrincipalKey(x => new { x.Id, x.Purpose }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable("ProjectVerifications", t => t.HasCheckConstraint("CK_ProjectVerifications_EvidencePurpose", "\"EvidenceModerationPurpose\" = 1"));
            e.Property(x => x.BookingStatus).HasConversion<short>();
            e.Property(x => x.MeetingRoomName).HasMaxLength(200);
            e.HasIndex(x => x.MeetingRoomName).IsUnique();
            e.HasIndex(x => new { x.BookingStatus, x.ReservedUntil });
            e.HasIndex(x => x.ReservationId).IsUnique();
            e.Property(x => x.AiSummary).HasColumnType("text");
            e.Property(x => x.AiScore).HasPrecision(5, 2);
            e.Property(x => x.AiAdditionalEvidenceJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.CancellationReason).HasColumnType("text");
            e.Property(x => x.SubmittedAt).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.Project).WithMany(x => x.Verifications)
                .HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestByUser).WithMany(x => x.RequestedVerifications)
                .HasForeignKey(x => x.RequestByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.FinalizedByUser).WithMany(x => x.FinalizedVerifications)
                .HasForeignKey(x => x.FinalizedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Package).WithMany()
                .HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.ProjectId, x.Status });
            e.HasIndex(x => x.ProjectId).IsUnique().HasFilter("\"Status\" = 0")
                .HasDatabaseName("IX_ProjectVerifications_PendingProject");
            e.HasIndex(x => new { x.RequestByUserId, x.SubmittedAt });
        });
        model.Entity<ProjectVerificationEvidence>(e =>
        {
            e.ToTable("ProjectVerificationEvidence", t =>
                t.HasCheckConstraint("CK_ProjectVerificationEvidence_Version", "\"Version\" > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.EvidenceJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.SubmittedAt).IsRequired();
            e.HasOne(x => x.ProjectVerification).WithMany(x => x.EvidenceSubmissions)
                .HasForeignKey(x => x.ProjectVerificationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SubmittedByUser).WithMany()
                .HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProjectVerificationId, x.Version }).IsUnique();
        });
        model.Entity<VerificationAssignment>(e =>
        {
            e.ToTable("VerificationAssignments", t =>
                t.HasCheckConstraint("CK_VerificationAssignments_Status", "\"Status\" BETWEEN 0 AND 3"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.ReplacementReason).HasColumnType("text");
            e.Property(x => x.AssignedAt).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.ProjectVerification).WithMany(x => x.Assignments)
                .HasForeignKey(x => x.ProjectVerificationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedToUser).WithMany(x => x.VerificationAssignments)
                .HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedByUser).WithMany()
                .HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReplacedBy).WithMany()
                .HasForeignKey("ReplacedById").OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.ProjectVerificationId, x.Status });
            e.HasIndex(x => new { x.AssignedToUserId, x.Status });
            e.Property(x => x.OverdueMarkedAt).HasColumnName("ReminderSentAt");
            e.HasIndex(x => x.ReportDueAt);
            e.HasIndex(x => x.AssignmentDueAt);
        });
        model.Entity<VerificationReport>(e =>
        {
            e.ToTable("VerificationReports", t => t.HasCheckConstraint("CK_VerificationReports_Case", "\"ProjectVerificationId\" IS NOT NULL OR \"LegacyOriginalJson\" IS NOT NULL"));
            e.HasKey(x => x.Id);
            e.HasOne<ProjectVerification>().WithMany().HasForeignKey(x => x.ProjectVerificationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProjectVerificationId).IsUnique();
            e.HasOne<VerificationAssignment>().WithMany().HasForeignKey(x => new { x.AssignmentId, x.ProjectVerificationId })
                .HasPrincipalKey(x => new { x.Id, x.ProjectVerificationId }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.LegacyOriginalJson).HasColumnType("jsonb");
            e.Property(x => x.Summary).HasColumnType("text").IsRequired();
            e.Property(x => x.Findings).HasColumnType("text");
            e.Property(x => x.Recommendations).HasColumnType("text");
            e.Property(x => x.SubmittetAt).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.VerificationAssignment).WithOne(x => x.Report)
                .HasForeignKey<VerificationReport>(x => x.AssignmentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Outcome).HasConversion<short?>();
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AssignmentId).IsUnique();
            foreach (var property in e.Metadata.GetProperties())
                property.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        });
        model.Entity<ReportAuditLog>(e =>
        {
            e.ToTable("ReportAuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(50).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.HasIndex(x => new { x.ReportId, x.Version }).IsUnique();
            e.HasOne<VerificationReport>().WithMany().HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Package>(e =>
        {
            e.ToTable("Packages", t =>
            {
                t.HasCheckConstraint("CK_Packages_Type", "\"Type\" BETWEEN 0 AND 1");
                t.HasCheckConstraint("CK_Packages_Role", "\"Role\" BETWEEN 0 AND 1");
                t.HasCheckConstraint("CK_Packages_Price", "\"Price\" >= 0");
                t.HasCheckConstraint("CK_Packages_DurationInDays", "\"DurationInDays\" IS NULL OR \"DurationInDays\" > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Type).HasConversion<short>().IsRequired();
            e.Property(x => x.Role).HasConversion<short>().IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2).IsRequired();
            e.Property(x => x.IsActive).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => new { x.Type, x.Role, x.IsActive });
        });
        model.Entity<Transaction>(e =>
        {
            e.ToTable("Transactions", t =>
            {
                t.HasCheckConstraint("CK_Transactions_Status", "\"Status\" BETWEEN 0 AND 2");
                t.HasCheckConstraint("CK_Transactions_Amount", "\"Amount\" >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            e.Property(x => x.PaymentRef).HasMaxLength(255);
            e.Property(x => x.PaymentUrl).HasMaxLength(2048);
            e.Property(x => x.PaymentMethod).HasMaxLength(100);
            e.Property(x => x.ProviderOrderCode).HasMaxLength(255);
            e.Property(x => x.ProviderPaymentLinkId).HasMaxLength(255);
            e.Property(x => x.FailureReason).HasColumnType("text");
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.User).WithMany(x => x.Transactions)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Package).WithMany(x => x.Transactions)
                .HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasIndex(x => x.ProviderOrderCode);
            e.HasIndex(x => x.ProviderPaymentLinkId);
            e.HasIndex(x => new { x.PaymentMethod, x.ProviderOrderCode }).IsUnique()
                .HasFilter("\"PaymentMethod\" = 'PayOS' AND \"ProviderOrderCode\" IS NOT NULL");
            e.HasIndex(x => new { x.PaymentMethod, x.ProviderPaymentLinkId }).IsUnique()
                .HasFilter("\"PaymentMethod\" = 'PayOS' AND \"ProviderPaymentLinkId\" IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.ExpiredAt });
        });
        model.Entity<VerificationTransaction>(e =>
        {
            e.ToTable("VerificationTransactions");
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.Transaction).WithOne(x => x.VerificationTransaction)
                .HasForeignKey<VerificationTransaction>(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Verification).WithMany(x => x.VerificationTransactions)
                .HasForeignKey(x => x.VerificationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.VerificationId);
        });
        model.Entity<RefundTransaction>(e =>
        {
            e.ToTable("RefundTransactions", t =>
            {
                t.HasCheckConstraint("CK_RefundTransactions_Status", "\"Status\" BETWEEN 0 AND 2");
                t.HasCheckConstraint("CK_RefundTransactions_Amount", "\"Amount\" >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.RequestSource).HasConversion<short>();
            foreach (var name in new[] { nameof(RefundTransaction.RequestedByUserId), nameof(RefundTransaction.RequestedAt), nameof(RefundTransaction.RequestSource) })
                e.Property(name).Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.PaymentProvider).HasMaxLength(100).IsRequired();
            e.Property(x => x.RefundRef).HasMaxLength(255);
            e.Property(x => x.Reason).HasColumnType("text").IsRequired();
            e.Property(x => x.FailureReason).HasColumnType("text");
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.Property(x => x.CompletedAt).IsRequired();

            e.HasOne(x => x.Transaction).WithMany(x => x.RefundTransactions)
                .HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TransactionId, x.Status });
        });
        model.Entity<UserSkill>(e =>
        {
            e.ToTable("UserSkills");

            // Primary key
            e.HasKey(x => x.Id);

            // Skill proficiency level
            e.Property(x => x.Level)
                .HasConversion<short>()
                .IsRequired();

            // Audit timestamps
            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            // User 1 : N UserSkills
            e.HasOne(x => x.User)
                .WithMany(x => x.UserSkills)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Skill 1 : N UserSkills
            e.HasOne(x => x.Skill)
                .WithMany(x => x.UserSkills)
                .HasForeignKey(x => x.SkillId)
                .OnDelete(DeleteBehavior.Restrict);

            // Prevent duplicate skills for the same user
            e.HasIndex(x => new
            {
                x.UserId,
                x.SkillId
            }).IsUnique();

            // Support skill-based user recommendations
            e.HasIndex(x => x.SkillId);
        });

        // 1. CommunityPost
        model.Entity<CommunityPost>(entity =>
        {
            entity.ToTable("CommunityPosts");
            entity.HasKey(e => e.Id);

            // �p ki?u list string th�nh JSONB
            entity.Property(e => e.MediaUrlsJson).HasColumnType("jsonb");

            // R�ng bu?c Check Constraints
            entity.ToTable(t => t.HasCheckConstraint("CK_CommunityPosts_AuthorActorType", "\"AuthorActorType\" BETWEEN 0 AND 1"));
            entity.ToTable(t => t.HasCheckConstraint("CK_CommunityPosts_Status", "\"Status\" BETWEEN 0 AND 3"));

            entity.HasOne(e => e.AuthorUser)
                  .WithMany()
                  .HasForeignKey(e => e.AuthorUserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // 2. CommunityPostMedia
        model.Entity<CommunityPostMedia>(entity =>
        {
            entity.ToTable("CommunityPostMedia");
            entity.HasKey(e => e.Id);

            entity.ToTable(t => t.HasCheckConstraint("CK_CommunityPostMedia_MediaType", "\"MediaType\" BETWEEN 0 AND 1"));

            entity.HasOne(e => e.Post)
                  .WithMany(p => p.Media)
                  .HasForeignKey(e => e.PostId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // 3. CommunityPostComment
        model.Entity<CommunityPostComment>(entity =>
        {
            entity.ToTable("CommunityPostComments");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Post)
                  .WithMany(p => p.Comments)
                  .HasForeignKey(e => e.PostId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AuthorUser)
                  .WithMany()
                  .HasForeignKey(e => e.AuthorUserId)
                  .OnDelete(DeleteBehavior.Restrict); // D�ng Restrict �? tr�nh l?i multiple cascade paths

            entity.HasOne(e => e.ParentComment)
                  .WithMany(c => c.Replies)
                  .HasForeignKey(e => e.ParentCommentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ReplyToUser)
                  .WithMany()
                  .HasForeignKey(e => e.ReplyToUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. CommunityPostReaction (Kh�a ch�nh k�p / Composite Key)
        model.Entity<CommunityPostReaction>(entity =>
        {
            entity.ToTable("CommunityPostReactions");
            entity.HasKey(e => new { e.PostId, e.UserId });

            entity.HasOne(e => e.Post)
                  .WithMany(p => p.Reactions)
                  .HasForeignKey(e => e.PostId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 5. CommunityPostBookmark (Kh�a ch�nh k�p / Composite Key)
        model.Entity<CommunityPostBookmark>(entity =>
        {
            entity.ToTable("CommunityPostBookmarks");
            entity.HasKey(e => new { e.PostId, e.UserId });

            entity.HasOne(e => e.Post)
                  .WithMany(p => p.Bookmarks)
                  .HasForeignKey(e => e.PostId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 6. ContentReport
        model.Entity<ContentReport>(entity =>
        {
            entity.ToTable("ContentReports");
            entity.HasKey(e => e.Id);

            entity.ToTable(t => t.HasCheckConstraint("CK_ContentReports_Status", "\"Status\" BETWEEN 0 AND 2"));

            entity.HasOne(e => e.Post)
                  .WithMany()
                  .HasForeignKey(e => e.PostId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ReporterUser)
                  .WithMany()
                  .HasForeignKey(e => e.ReporterUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
        // 1. AdvertisementCampaign
        model.Entity<AdvertisementCampaign>(entity =>
        {
            entity.ToTable("AdvertisementCampaigns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BudgetCap).HasColumnType("numeric(18,2)");

            entity.HasOne(e => e.CompanyUser)
                  .WithMany()
                  .HasForeignKey(e => e.CompanyUserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Package)
                  .WithMany()
                  .HasForeignKey(e => e.PackageId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 2. AdvertisementTransaction (Mapping 1-1 v?i Transaction)
        model.Entity<AdvertisementTransaction>(entity =>
        {
            entity.ToTable("AdvertisementTransactions");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Transaction)
                  .WithOne(t => t.AdvertisementTransaction)
                  .HasForeignKey<AdvertisementTransaction>(e => e.TransactionId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Campaign)
                  .WithMany(c => c.AdvertisementTransactions)
                  .HasForeignKey(e => e.CampaignId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
