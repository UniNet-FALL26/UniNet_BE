using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public sealed class UniNetDbContext(DbContextOptions<UniNetDbContext> options) : DbContext(options)
{
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
    public DbSet<ProjectModeration> ProjectModerations => Set<ProjectModeration>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(e =>
        {
            e.ToTable("Accounts", t => { t.HasCheckConstraint("CK_Accounts_Role", "\"Role\" BETWEEN 0 AND 2"); t.HasCheckConstraint("CK_Accounts_Status", "\"Status\" BETWEEN 0 AND 3"); }); e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired(); e.HasIndex(x => x.Email).IsUnique();
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
            e.ToTable("Skills", t => t.HasCheckConstraint("CK_Skills_Category", "\"Category\" BETWEEN 0 AND 8")); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            e.Property(x => x.Category).HasConversion<short>();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.Category, x.IsActive, x.DisplayOrder });
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
                    "\"Status\" BETWEEN 0 AND 5");

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
                x.Status,
                x.RecruitmentStatus
            });

            e.HasIndex(x => x.ProjectField);

            e.HasIndex(x => x.RecruitmentDeadline);
        });
        model.Entity<ProjectRoleRequirement>(e =>
        {
            e.ToTable("ProjectRoleRequirements", t=>
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
        model.Entity<ProjectModeration>(e =>
        {
            e.ToTable("ProjectModerations", t =>
            {
                t.HasCheckConstraint(
                    "CK_ProjectModerations_Status",
                    "\"Status\" BETWEEN 0 AND 2");

                t.HasCheckConstraint(
                    "CK_ProjectModerations_ContentResult",
                    "\"ContentResult\" BETWEEN 0 AND 2");

                t.HasCheckConstraint(
                    "CK_ProjectModerations_LinkResult",
                    "\"LinkResult\" BETWEEN 0 AND 2");
            });

            e.HasKey(x => x.Id);

            e.Property(x => x.Status)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.ContentResult)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.LinkResult)
                .HasConversion<short>()
                .IsRequired();

            e.Property(x => x.ViolationReason)
                .HasColumnType("text");

            e.Property(x => x.ResultJson)
                .HasColumnType("jsonb");

            e.Property(x => x.EngineVersion)
                .HasMaxLength(100);

            e.Property(x => x.CheckedAt);

            e.Property(x => x.CreatedAt)
                .IsRequired();

            e.Property(x => x.UpdatedAt)
                .IsRequired();

            e.HasOne(x => x.Project)
                .WithMany(x => x.Moderations)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new
            {
                x.ProjectId,
                x.CreatedAt
            });
        });
    }
}
