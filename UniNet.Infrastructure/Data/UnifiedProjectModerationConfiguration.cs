using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public sealed class UnifiedProjectModerationConfiguration : IEntityTypeConfiguration<ProjectModeration>
{
    public void Configure(EntityTypeBuilder<ProjectModeration> e)
    {
        e.Property(x => x.Purpose).HasConversion<short>();
        e.Property(x => x.Purpose).Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        e.ToTable("ProjectModerations", t => t.HasCheckConstraint("CK_ProjectModerations_Purpose", "\"Purpose\" = 0"));
    }
}
