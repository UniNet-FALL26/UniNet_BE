using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Community;

namespace UniNet.Infrastructure.Data;

public sealed class UniNetDbContext(DbContextOptions<UniNetDbContext> options) : DbContext(options)
{
    public DbSet<PortraitAsset> PortraitAssets => Set<PortraitAsset>();
    public DbSet<Account> Accounts => Set<Account>();
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
    public DbSet<ProjectJoinRequest> ProjectJoinRequests => Set<ProjectJoinRequest>();
    public DbSet<ProjectInvitation> ProjectInvitations => Set<ProjectInvitation>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();
    public DbSet<ProjectTaskAssignment> ProjectTaskAssignments => Set<ProjectTaskAssignment>();
    public DbSet<TaskReport> TaskReports => Set<TaskReport>();
    public DbSet<TaskEvidence> TaskEvidence => Set<TaskEvidence>();
    public DbSet<TaskReportReview> TaskReportReviews => Set<TaskReportReview>();
    public DbSet<ProjectModeration> ProjectModerations => Set<ProjectModeration>();
    public DbSet<UserSkill> UserSkills => Set<UserSkill>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();
    public DbSet<User1SavedUser2> User1SavedUser2 => Set<User1SavedUser2>();
    public DbSet<StudentSavedJob> StudentSavedJobs => Set<StudentSavedJob>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ProjectVerification> ProjectVerifications => Set<ProjectVerification>();
    public DbSet<ProjectVerificationEvidence> ProjectVerificationEvidence => Set<ProjectVerificationEvidence>();
    public DbSet<VerificationAssignment> VerificationAssignments => Set<VerificationAssignment>();
    public DbSet<VerificationReport> VerificationReports => Set<VerificationReport>();
    public DbSet<VerificationReportVersion> VerificationReportVersions => Set<VerificationReportVersion>();
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
                t.HasCheckConstraint("CK_TaskReports_Status", "\"Status\" BETWEEN 0 AND 2");
                t.HasCheckConstraint("CK_TaskReports_Version", "\"Version\" > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).HasMaxLength(5000).IsRequired();
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
            e.Property(x => x.SubmittedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.ProjectTask)
                .WithMany(x => x.Reports)
                .HasForeignKey(x => x.ProjectTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.SubmittedBy)
                .WithMany()
                .HasForeignKey(x => x.SubmittedById)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.ProjectTaskId, x.SubmittedById, x.Version }).IsUnique();
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
        model.Entity<ProjectModeration>(e =>
        {
            e.ToTable("ProjectModerations", t =>
            {
                t.HasCheckConstraint(
                    "CK_ProjectModerations_Status",
                    "\"Status\" BETWEEN 0 AND 5");

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
                    "\"AttemptNumber\" > 0");
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
                .OnDelete(DeleteBehavior.Cascade);

            // Moderator/UserProfile 1 : N ProjectModeration
            e.HasOne(x => x.ReviewedBy)
                .WithMany()
                .HasForeignKey(x => x.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Each project has unique moderation attempt numbers
            e.HasIndex(x => new
            {
                x.ProjectId,
                x.AttemptNumber
            })
            .IsUnique();

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
        model.Entity<User1SavedUser2>(e =>
        {
            e.ToTable("User1SavedUser2");
            e.HasKey(x => new { x.UserBookmarkId, x.UserId });
            e.Property(x => x.Note).HasColumnType("text");
            e.HasOne(x => x.UserBookmark).WithMany(x => x.SavedUsers).HasForeignKey(x => x.UserBookmarkId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(x => x.SavedByUsers).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
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
        });
        model.Entity<ProjectVerification>(e =>
        {
            e.ToTable("ProjectVerifications", t =>
                t.HasCheckConstraint("CK_ProjectVerifications_Status", "\"Status\" BETWEEN 0 AND 3"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<short>().IsRequired();
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
            e.HasIndex(x => x.ReportDueAt);
            e.HasIndex(x => x.AssignmentDueAt);
        });
        model.Entity<VerificationReport>(e =>
        {
            e.ToTable("VerificationReports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Summary).HasColumnType("text").IsRequired();
            e.Property(x => x.Findings).HasColumnType("text");
            e.Property(x => x.Recommendations).HasColumnType("text");
            e.Property(x => x.SubmittetAt).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.HasOne(x => x.VerificationAssignment).WithOne(x => x.Report)
                .HasForeignKey<VerificationReport>(x => x.VerificationId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Outcome).HasConversion<short?>();
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.VerificationId).IsUnique();
        });
        model.Entity<VerificationReportVersion>(e =>
        {
            e.ToTable("VerificationReportVersions", t =>
            {
                t.HasCheckConstraint("CK_ReportVersions_Version", "\"Version\" >= 2");
                t.HasCheckConstraint("CK_ReportVersions_Outcome", "\"Outcome\" IN (1, 2)");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ReportId, x.Version }).IsUnique();
            e.Property(x => x.Summary).HasColumnType("text").IsRequired();
            e.Property(x => x.Findings).HasColumnType("text").IsRequired();
            e.Property(x => x.Recommendations).HasColumnType("text").IsRequired();
            e.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Outcome).HasConversion<short>();
            e.HasOne(x => x.Report).WithMany(x => x.Versions).HasForeignKey(x => x.ReportId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
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

            // Ép ki?u list string thành JSONB
            entity.Property(e => e.MediaUrlsJson).HasColumnType("jsonb");

            // Ràng bu?c Check Constraints
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
                  .OnDelete(DeleteBehavior.Restrict); // Dùng Restrict ð? tránh l?i multiple cascade paths

            entity.HasOne(e => e.ParentComment)
                  .WithMany(c => c.Replies)
                  .HasForeignKey(e => e.ParentCommentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ReplyToUser)
                  .WithMany()
                  .HasForeignKey(e => e.ReplyToUserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // 4. CommunityPostReaction (Khóa chính kép / Composite Key)
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

        // 5. CommunityPostBookmark (Khóa chính kép / Composite Key)
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
