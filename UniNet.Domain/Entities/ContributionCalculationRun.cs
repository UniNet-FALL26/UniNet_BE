using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ContributionCalculationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CycleId { get; set; }
    public int RunNumber { get; set; }
    public string FormulaVersion { get; set; } = "1";
    public string InputSnapshot { get; set; } = "{}";
    public string InputHash { get; set; } = string.Empty;
    public string WarningsJson { get; set; } = "[]";
    public Guid CalculatedById { get; set; }
    public DateTimeOffset CalculatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ContributionCalculationRunStatus Status { get; set; } = ContributionCalculationRunStatus.PendingReview;

    public ContributionCycle Cycle { get; set; } = null!;
    public UserProfile CalculatedBy { get; set; } = null!;
    public List<ContributionResult> Results { get; set; } = [];
    public List<ContributionReview> Reviews { get; set; } = [];
}
