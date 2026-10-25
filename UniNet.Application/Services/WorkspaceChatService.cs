using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class WorkspaceChatService(UniNetDbContext db, IWorkspaceAccessService access) : IWorkspaceChatService
{
    public async Task<IReadOnlyList<WorkspaceChannelResponse>> GetChannelsAsync(
        Guid projectId, Guid accountId, CancellationToken ct = default)
    {
        await access.RequireMemberAsync(projectId, accountId, ct);
        return await db.WorkspaceChannels.AsNoTracking()
            .Where(channel => channel.ProjectId == projectId && !channel.IsDeleted)
            .OrderBy(channel => channel.CreatedAt)
            .ThenBy(channel => channel.Id)
            .Select(channel => new WorkspaceChannelResponse(
                channel.Id,
                channel.ProjectId,
                channel.Name,
                channel.Description,
                channel.CreatedById,
                channel.CreatedAt,
                channel.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<WorkspaceChannelResponse> CreateChannelAsync(
        Guid projectId,
        Guid accountId,
        CreateWorkspaceChannelRequest request,
        CancellationToken ct = default)
    {
        var profile = await access.RequireLeaderAsync(projectId, accountId, ct);

        var name = request.Name?.Trim();
        Require(!string.IsNullOrWhiteSpace(name), "INVALID_CHANNEL_NAME", "Channel name is required.");
        Require(name.Length <= 100, "INVALID_CHANNEL_NAME", "Channel name cannot exceed 100 characters.");
        var description = NormalizeDescription(request.Description);
        var now = DateTimeOffset.UtcNow;
        var channel = new WorkspaceChannel
        {
            ProjectId = projectId,
            CreatedById = profile.Id,
            Name = name,
            Description = description,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.WorkspaceChannels.Add(channel);
        await db.SaveChangesAsync(ct);
        return ToChannelResponse(channel);
    }

    public async Task<WorkspaceChannelResponse> UpdateChannelAsync(
        Guid projectId,
        Guid channelId,
        Guid accountId,
        UpdateWorkspaceChannelRequest request,
        CancellationToken ct = default)
    {
        var channel = await GetActiveChannelAsync(channelId, projectId, ct);
        await access.RequireLeaderAsync(projectId, accountId, ct);
        Require(request.Name is not null || request.Description is not null,
            "INVALID_CHANNEL_UPDATE", "At least one channel field must be provided.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            Require(!string.IsNullOrWhiteSpace(name), "INVALID_CHANNEL_NAME", "Channel name is required.");
            Require(name.Length <= 100, "INVALID_CHANNEL_NAME", "Channel name cannot exceed 100 characters.");
            channel.Name = name;
        }

        if (request.Description is not null)
            channel.Description = NormalizeDescription(request.Description);

        channel.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToChannelResponse(channel);
    }

    public async Task ArchiveChannelAsync(
        Guid projectId, Guid channelId, Guid accountId, CancellationToken ct = default)
    {
        var channel = await GetActiveChannelAsync(channelId, projectId, ct);
        var profile = await access.RequireLeaderAsync(projectId, accountId, ct);

        channel.IsDeleted = true;
        channel.DeletedAt = DateTimeOffset.UtcNow;
        channel.DeletedById = profile.Id;
        channel.UpdatedAt = channel.DeletedAt.Value;
        await db.SaveChangesAsync(ct);
    }

    public async Task<WorkspaceChannelResponse> GetChannelAsync(
        Guid projectId, Guid channelId, Guid accountId, CancellationToken ct = default)
    {
        var channel = await GetActiveChannelAsync(channelId, projectId, ct);
        await access.RequireMemberAsync(projectId, accountId, ct);
        return ToChannelResponse(channel);
    }

    public async Task<WorkspaceMessagePageResponse> GetMessagesAsync(
        Guid projectId, Guid channelId, Guid accountId, int page, int pageSize, CancellationToken ct = default)
    {
        ValidatePagination(page, pageSize);
        var channel = await GetActiveChannelAsync(channelId, projectId, ct);
        await access.RequireMemberAsync(projectId, accountId, ct);

        var query = db.WorkspaceMessages.AsNoTracking()
            .Where(message => message.ChannelId == channelId);
        var totalItems = await query.CountAsync(ct);
        var offset = (long)(page - 1) * pageSize;
        Require(offset <= int.MaxValue, "INVALID_PAGINATION", "Requested page is out of range.");
        var messages = await query
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .Skip((int)offset)
            .Take(pageSize)
            .Select(message => new WorkspaceMessageResponse(
                message.Id,
                message.ChannelId,
                message.SenderId,
                message.Sender.FullName,
                message.Sender.Nickname,
                message.Content,
                message.CreatedAt))
            .ToListAsync(ct);

        return new(
            channelId,
            page,
            pageSize,
            totalItems,
            (int)Math.Ceiling(totalItems / (double)pageSize),
            messages);
    }

    public async Task<WorkspaceChannelAccessResponse> AuthorizeChannelAsync(
        Guid channelId, Guid accountId, CancellationToken ct = default)
    {
        var channel = await GetActiveChannelAsync(channelId, null, ct);
        var profile = await access.RequireMemberAsync(channel.ProjectId, accountId, ct);
        return new(channel.ProjectId, profile.Id);
    }

    public async Task<WorkspaceMessageResponse> SendMessageAsync(
        Guid projectId,
        Guid channelId,
        Guid accountId,
        SendWorkspaceMessageRequest request,
        CancellationToken ct = default)
        => await SendMessageCoreAsync(projectId, channelId, accountId, request, ct);

    public async Task<WorkspaceMessageResponse> SendMessageAsync(
        Guid channelId,
        Guid accountId,
        SendWorkspaceMessageRequest request,
        CancellationToken ct = default)
        => await SendMessageCoreAsync(null, channelId, accountId, request, ct);

    private async Task<WorkspaceMessageResponse> SendMessageCoreAsync(
        Guid? projectId,
        Guid channelId,
        Guid accountId,
        SendWorkspaceMessageRequest request,
        CancellationToken ct)
    {
        var content = request.Content?.Trim();
        Require(!string.IsNullOrWhiteSpace(content), "INVALID_MESSAGE", "Message content is required.");
        Require(content.Length <= 4000, "INVALID_MESSAGE", "Message content cannot exceed 4000 characters.");

        var channel = await GetActiveChannelAsync(channelId, projectId, ct);
        var profile = await access.RequireMemberAsync(channel.ProjectId, accountId, ct);
        var message = new WorkspaceMessage
        {
            ChannelId = channel.Id,
            SenderId = profile.Id,
            Content = content,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.WorkspaceMessages.Add(message);
        await db.SaveChangesAsync(ct);
        return ToMessageResponse(message, profile);
    }

    private async Task<WorkspaceChannel> GetActiveChannelAsync(
        Guid channelId, Guid? expectedProjectId, CancellationToken ct)
    {
        var channel = await db.WorkspaceChannels
            .FirstOrDefaultAsync(item => item.Id == channelId && !item.IsDeleted, ct);
        Require(channel is not null, "WORKSPACE_CHANNEL_NOT_FOUND", "Workspace channel not found.", 404);
        Require(expectedProjectId is null || channel.ProjectId == expectedProjectId,
            "WORKSPACE_CHANNEL_NOT_FOUND", "Workspace channel not found.", 404);
        return channel;
    }

    private static WorkspaceChannelResponse ToChannelResponse(WorkspaceChannel channel) =>
        new(channel.Id, channel.ProjectId, channel.Name, channel.Description, channel.CreatedById,
            channel.CreatedAt, channel.UpdatedAt);

    private static WorkspaceMessageResponse ToMessageResponse(WorkspaceMessage message, UserProfile profile) =>
        new(message.Id, message.ChannelId, profile.Id, profile.FullName, profile.Nickname,
            message.Content, message.CreatedAt);

    private static string? NormalizeDescription(string? description)
    {
        var normalized = description?.Trim();
        Require(normalized is null || normalized.Length <= 500,
            "INVALID_CHANNEL_DESCRIPTION", "Channel description cannot exceed 500 characters.");
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static void ValidatePagination(int page, int pageSize)
    {
        Require(page >= 1, "INVALID_PAGINATION", "Page must be at least 1.");
        Require(pageSize is >= 1 and <= 100, "INVALID_PAGINATION", "Page size must be between 1 and 100.");
    }

    private static void Require(
        [DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    {
        if (!condition)
            throw new ProjectException(code, message, status);
    }
}
