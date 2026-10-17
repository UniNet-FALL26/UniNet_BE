using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ContributionPeerEvaluationHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    public Guid EvaluatorUserId { get; set; }
    public Guid EvaluateeUserId { get; set; }
    public Guid ContributionPeerEvaluationId { get; set; }
    public int? PreviousScore { get; set; }
    public string? PreviousComment { get; set; }
    public int NewScore { get; set; }
    public string? NewComment { get; set; }
    public ContributionPeerEvaluationAction Action { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;

    public ContributionPeerEvaluation ContributionPeerEvaluation { get; set; } = null!;
}
