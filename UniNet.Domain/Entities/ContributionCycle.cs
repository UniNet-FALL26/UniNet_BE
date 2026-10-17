using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ContributionCycle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public ContributionCycleStatus Status { get; set; } = ContributionCycleStatus.CollectingEvaluations;
    public Guid OpenedById { get; set; }
    public DateTimeOffset OpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinalizedAt { get; set; }
    public string FormulaVersion { get; set; } = "1";

    public Project Project { get; set; } = null!;
    public UserProfile OpenedBy { get; set; } = null!;
    public List<ContributionParticipant> Participants { get; set; } = [];
    public List<ContributionPeerEvaluation> PeerEvaluations { get; set; } = [];
    public List<ContributionCalculationRun> CalculationRuns { get; set; } = [];
}
