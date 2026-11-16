using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ProjectRecruitment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string? Description { get; set; }
    public string Positions { get; set; } = "[]";
    public DateTimeOffset? Deadline { get; set; }
    public ProjectRecruitmentStatus Status { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Project Project { get; set; } = null!;
}
