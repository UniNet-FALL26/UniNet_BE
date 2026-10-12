using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class TaskReportReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskReportId { get; set; }
    public Guid ReviewerId { get; set; }
    public TaskReportReviewDecision Decision { get; set; }
    public string? Feedback { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;

    public TaskReport TaskReport { get; set; } = null!;
    public UserProfile Reviewer { get; set; } = null!;
}
