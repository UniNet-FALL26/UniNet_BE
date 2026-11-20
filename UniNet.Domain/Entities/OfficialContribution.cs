namespace UniNet.Domain.Entities;

// Selection is persisted; timestamps and E2E identifiers are never routing rules.
public sealed class ProjectContributionWorkflow
{
    public Guid ProjectId { get; set; }
    public string Mode { get; set; } = "OfficialV2";
    public DateTimeOffset SelectedAt { get; set; } = DateTimeOffset.UtcNow;
}

// The calculation is shared with GitContributionRun; lifecycle is deliberately separate.
public sealed class OfficialContributionRun
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequestId { get; set; }
    public string State { get; set; } = "Draft";
    public int Revision { get; set; }
    public string ResultHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedById { get; set; }
    public DateTimeOffset? FinalizedAt { get; set; }
    public Guid? FinalizedById { get; set; }
}

public sealed class OfficialContributionAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public int Revision { get; set; }
    public string Action { get; set; } = "";
    public Guid ActorId { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public string? Feedback { get; set; }
}

// Publication is append-only, so finalization never needs to unlock frozen results.
public sealed class OfficialContributionPublication
{
    public Guid ProjectId { get; set; }
    public Guid RunId { get; set; }
    public Guid PublishedById { get; set; }
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}
