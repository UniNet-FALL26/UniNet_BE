using System.ComponentModel.DataAnnotations.Schema;

namespace UniNet.Domain.Entities;

public sealed class VerificationNotification
{
    public Guid Id { get; set; }
    // Legacy API adapters. Only TargetType/TargetId are persisted.
    [NotMapped] public Guid? VerificationId { get => TargetType == "ProjectVerification" ? TargetId : null; set { if (value.HasValue) { TargetType = "ProjectVerification"; TargetId = value; } } }
    [NotMapped] public Guid? EvidenceModerationId { get => TargetType == "EvidenceModeration" ? TargetId : null; set { if (value.HasValue) { TargetType = "EvidenceModeration"; TargetId = value; } } }
    [NotMapped] public Guid? UserVerificationId { get => TargetType == "UserVerification" ? TargetId : null; set { if (value.HasValue) { TargetType = "UserVerification"; TargetId = value; } } }
    // Accounts.Id, never UserProfiles.Id.
    public Guid RecipientUserId { get; set; }
    public Guid? SenderUserId { get; set; }
    public string Type { get; set; } = "VerificationUpdated";
    public string Category { get; set; } = "verification";
    public string Title { get; set; } = "Cập nhật xác minh";
    public string Audience { get; set; } = "Personal";
    public string? RequiredPermission { get; set; }
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }
    public string? TargetUrl { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public string? EventKey { get; set; }
    public Guid? BroadcastId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Message { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
