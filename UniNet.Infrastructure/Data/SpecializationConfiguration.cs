using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;

namespace UniNet.Infrastructure.Data;

public static class SpecializationConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        var e = model.Entity<Specialization>();
        e.ToTable("Specializations");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(100).IsRequired();
        e.Property(x => x.Name).HasMaxLength(255).IsRequired();
        e.HasIndex(x => x.Code).IsUnique();
        e.HasData(
            new Specialization { Id = Guid.Parse("a4900000-0000-0000-0000-000000000001"), Code = "SOFTWARE_ENGINEERING", Name = "Kỹ thuật phần mềm" },
            new Specialization { Id = Guid.Parse("a4900000-0000-0000-0000-000000000002"), Code = "ARTIFICIAL_INTELLIGENCE", Name = "Trí tuệ nhân tạo" },
            new Specialization { Id = Guid.Parse("a4900000-0000-0000-0000-000000000003"), Code = "INFORMATION_SECURITY", Name = "An toàn thông tin" });

        model.Entity<Project>().HasOne(x => x.Specialization).WithMany()
            .HasForeignKey(x => x.SpecializationId).OnDelete(DeleteBehavior.Restrict);
        // All project response paths, including nested verification/assignment views, use the same catalog data.
        model.Entity<Project>().Navigation(x => x.Specialization).AutoInclude();

        var a = model.Entity<AssessorSpecialization>();
        a.ToTable("AssessorSpecializations");
        a.HasKey(x => new { x.AssessorUserId, x.SpecializationId });
        a.HasOne(x => x.Assessor).WithMany().HasForeignKey(x => x.AssessorUserId).OnDelete(DeleteBehavior.Cascade);
        a.HasOne(x => x.Specialization).WithMany().HasForeignKey(x => x.SpecializationId).OnDelete(DeleteBehavior.Restrict);
    }
}
