using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public sealed class UploadedFileConfiguration : IEntityTypeConfiguration<UploadedFile>
{
    public void Configure(EntityTypeBuilder<UploadedFile> e)
    {
        e.ToTable("UploadedFiles", t =>
        {
            t.HasCheckConstraint("CK_UploadedFiles_SizeBytes", "\"SizeBytes\" > 0");
            t.HasCheckConstraint("CK_UploadedFiles_ResourceType", "\"ResourceType\" IN ('image', 'raw', 'video')");
        });
        e.HasKey(x => x.Id);
        e.HasOne(x => x.OwnerAccount).WithMany().HasForeignKey(x => x.OwnerAccountId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.PublicId).HasMaxLength(512).IsRequired();
        e.Property(x => x.Url).IsRequired();
        e.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        e.Property(x => x.Format).HasMaxLength(20).IsRequired();
        e.Property(x => x.ResourceType).HasMaxLength(10).IsRequired();
        e.HasIndex(x => new { x.ResourceType, x.PublicId }).IsUnique();
        e.HasIndex(x => new { x.OwnerAccountId, x.CreatedAt });
    }
}

public sealed class ExternalProjectDraftConfiguration : IEntityTypeConfiguration<ExternalProjectDraft>
{
    public void Configure(EntityTypeBuilder<ExternalProjectDraft> e)
    {
        e.ToTable("ExternalProjectDrafts", t =>
        {
            t.HasCheckConstraint("CK_ExternalProjectDrafts_Status", "\"Status\" BETWEEN 0 AND 4");
            t.HasCheckConstraint("CK_ExternalProjectDrafts_Version", "\"Version\" > 0");
            t.HasCheckConstraint("CK_ExternalProjectDrafts_Confirmation",
                "(\"Status\" = 3 AND \"ConfirmedProjectId\" IS NOT NULL) OR (\"Status\" <> 3 AND \"ConfirmedProjectId\" IS NULL)");
        });
        e.HasKey(x => x.Id);
        e.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Status).HasConversion<short>();
        e.Property(x => x.ExtractedDataJson).HasColumnType("jsonb");
        e.Property(x => x.EditedDataJson).HasColumnType("jsonb");
        e.Property(x => x.Version).IsConcurrencyToken();
        e.Property(x => x.ErrorCode).HasMaxLength(100);
        e.HasOne(x => x.ConfirmedProject).WithMany().HasForeignKey(x => x.ConfirmedProjectId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.ConfirmedProjectId).IsUnique();
        e.HasIndex(x => new { x.OwnerUserId, x.Status, x.CreatedAt });
        e.HasIndex(x => new { x.Status, x.ExpiresAt });
        e.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt });
    }
}

public sealed class ExternalProjectDraftFileConfiguration : IEntityTypeConfiguration<ExternalProjectDraftFile>
{
    public void Configure(EntityTypeBuilder<ExternalProjectDraftFile> e)
    {
        e.ToTable("ExternalProjectDraftFiles");
        e.HasKey(x => new { x.DraftId, x.UploadedFileId });
        e.HasOne(x => x.Draft).WithMany(x => x.Files).HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.UploadedFile).WithMany(x => x.DraftFiles).HasForeignKey(x => x.UploadedFileId).OnDelete(DeleteBehavior.Restrict);
    }
}
