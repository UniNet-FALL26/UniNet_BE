namespace UniNet.Domain.Entities;

public sealed class ContributionPeerEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    public Guid EvaluatorUserId { get; set; }
    public Guid EvaluateeUserId { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ContributionCycle Cycle { get; set; } = null!;
    public UserProfile Evaluator { get; set; } = null!;
    public UserProfile Evaluatee { get; set; } = null!;
    public List<ContributionPeerEvaluationHistory> History { get; set; } = [];
}
