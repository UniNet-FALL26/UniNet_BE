namespace UniNet.Domain.Entities;

public enum ExperimentalPeerCohortStatus : short { Open, Collecting, ReadyToClose, Closed }

public sealed class ExperimentalPeerCohort
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string RosterJson { get; set; } = "[]";
    public string RosterHash { get; set; } = "";
    public ExperimentalPeerCohortStatus Status { get; set; }
    public Guid OpenedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class ExperimentalPeerSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CohortId { get; set; }
    public Guid EvaluatorId { get; set; }
    public Guid EvaluateeId { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ExperimentalPeerRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public int Revision { get; set; }
    public int? PreviousScore { get; set; }
    public int Score { get; set; }
    public string? PreviousComment { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

public sealed class ExperimentalPeerSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid CohortId { get; set; }
    public string PolicyVersion { get; set; } = "ExperimentalPeerV1";
    public string InputJson { get; set; } = "{}";
    public string InputHash { get; set; } = "";
    public string ScoresJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
