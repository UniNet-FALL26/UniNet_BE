namespace UniNet.Domain.Entities;

public sealed class ProjectPersonalReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid UserId { get; set; }
    public string? Challenges { get; set; }
    public string? LessonsLearned { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Project Project { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
}
