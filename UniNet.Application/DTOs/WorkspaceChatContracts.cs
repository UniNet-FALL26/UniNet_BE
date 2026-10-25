using System.ComponentModel.DataAnnotations;

namespace UniNet.Application;

public sealed record CreateWorkspaceChannelRequest(
    [Required, StringLength(100)] string Name,
    [StringLength(500)] string? Description = null);

public sealed record UpdateWorkspaceChannelRequest(
    [StringLength(100)] string? Name,
    [StringLength(500)] string? Description);

public sealed record SendWorkspaceMessageRequest(
    [Required, StringLength(4000)] string Content);

public sealed record WorkspaceChannelResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    Guid CreatedById,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceMessageResponse(
    Guid Id,
    Guid ChannelId,
    Guid SenderId,
    string SenderName,
    string SenderNickname,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record WorkspaceMessagePageResponse(
    Guid ChannelId,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    IReadOnlyList<WorkspaceMessageResponse> Data);

public sealed record WorkspaceChannelAccessResponse(Guid ProjectId, Guid UserProfileId);
