using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class ExternalProjectDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public UserProfile OwnerUser { get; set; } = null!;
    public ExternalProjectDraftStatus Status { get; set; } = ExternalProjectDraftStatus.Processing;
    public string? ExtractedDataJson { get; set; }
    public string? EditedDataJson { get; set; }
    // Increment on each edit; EF uses this value for optimistic concurrency.
    public int Version { get; set; } = 1;
    public Guid? ConfirmedProjectId { get; set; }
    public Project? ConfirmedProject { get; set; }
    public string? ErrorCode { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public List<ExternalProjectDraftFile> Files { get; set; } = [];
}
