using Microsoft.EntityFrameworkCore;
using UniNet.Domain;

namespace UniNet.Infrastructure.Data;

public sealed class UniNetDbContext(DbContextOptions<UniNetDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Account>(e =>
        {
            e.ToTable("Accounts", t => { t.HasCheckConstraint("CK_Accounts_Role", "\"Role\" BETWEEN 0 AND 2"); t.HasCheckConstraint("CK_Accounts_Status", "\"Status\" BETWEEN 0 AND 3"); }); e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(255).IsRequired(); e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.PasswordHash).HasColumnType("text");
            e.Property(x => x.GoogleId).HasMaxLength(255); e.HasIndex(x => x.GoogleId).IsUnique();
            e.Property(x => x.GoogleEmail).HasMaxLength(255);
            e.Property(x => x.Role).HasConversion<short>(); e.Property(x => x.Status).HasConversion<short>();
        });
        model.Entity<UserProfile>(e =>
        {
            e.ToTable("UserProfiles", t => t.HasCheckConstraint("CK_UserProfiles_PartnerType", "\"PartnerType\" IS NULL OR \"PartnerType\" BETWEEN 0 AND 4")); e.HasKey(x => x.Id);
            e.HasIndex(x => x.AccountId).IsUnique();
            e.HasOne(x => x.Account).WithOne(x => x.Profile).HasForeignKey<UserProfile>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.DisplayName).HasMaxLength(255).IsRequired();
            e.Property(x => x.UniversityName).HasMaxLength(255); e.Property(x => x.Major).HasMaxLength(255);
            e.Property(x => x.StudentCode).HasMaxLength(50); e.Property(x => x.PartnerType).HasConversion<short?>();
            e.Property(x => x.Industry).HasMaxLength(150); e.Property(x => x.ContactEmail).HasMaxLength(255);
            e.Property(x => x.Phone).HasMaxLength(30); e.Property(x => x.TaxCode).HasMaxLength(50);
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
