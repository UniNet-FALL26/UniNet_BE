namespace UniNet.Domain.Entities;

public sealed class ContributionResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    public Guid RunId { get; set; }
    public Guid UserId { get; set; }
    public decimal TaskExecutionScore { get; set; }
    public decimal ApprovedDeliverablesScore { get; set; }
    public decimal PeerEvaluationScore { get; set; }
    public decimal TotalScore { get; set; }
    public string CalculationDetails { get; set; } = "{}";

    public ContributionCalculationRun Run { get; set; } = null!;
    public UserProfile User { get; set; } = null!;
}
