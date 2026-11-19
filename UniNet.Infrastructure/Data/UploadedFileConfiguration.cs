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
