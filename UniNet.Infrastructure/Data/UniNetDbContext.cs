using Microsoft.EntityFrameworkCore;
using UniNet.Domain;

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
    }
}
