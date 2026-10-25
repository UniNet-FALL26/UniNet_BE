using UniNet.Domain.Entities;

namespace UniNet.Application.Services;

public sealed record NotificationDto(Guid Id, Guid RecipientUserId, Guid? SenderUserId, string Type, string Category,
    string Title, string Message, string Audience, string? RequiredPermission, string? TargetType, Guid? TargetId,
    string? TargetUrl, DateTimeOffset? ReadAt, string? EventKey, Guid? BroadcastId, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt)
{
    public static NotificationDto From(VerificationNotification n) => new(n.Id, n.RecipientUserId, n.SenderUserId,
        n.Type, n.Category, n.Title, n.Message, n.Audience, n.RequiredPermission, n.TargetType, n.TargetId,
        n.TargetUrl, n.ReadAt, n.EventKey, n.BroadcastId, n.CreatedAt, n.ExpiresAt);
}
