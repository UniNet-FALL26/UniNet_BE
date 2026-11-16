namespace UniNet.Domain.Entities;

public sealed class ProjectDetail
{
    public Guid ProjectId { get; set; }
    public string? Description { get; set; }
    public string? Features { get; set; }
    public string? Results { get; set; }
    public string? Documents { get; set; }
    public string? Media { get; set; }
    public string? Links { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Project Project { get; set; } = null!;
}
