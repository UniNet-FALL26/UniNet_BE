using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ContributionReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid ReviewerId { get; set; }
    public ContributionReviewDecision Decision { get; set; }
    public string? Feedback { get; set; }
    public DateTimeOffset ReviewedAt { get; set; } = DateTimeOffset.UtcNow;

    public ContributionCalculationRun Run { get; set; } = null!;
    public UserProfile Reviewer { get; set; } = null!;
}
