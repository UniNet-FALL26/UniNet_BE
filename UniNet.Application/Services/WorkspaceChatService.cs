using Microsoft.Extensions.Configuration;
using UniNet.Application.Interfaces;
using UniNet.Infrastructure.Data;
namespace UniNet.Application.Services;

public sealed class WorkspaceChatService : IWorkspaceChatService
{
    public WorkspaceChatService(UniNetDbContext db, IWorkspaceAccessService access) { }
    public Task<WorkspaceChannelAccessResponse> AuthorizeChannelAsync(
        Guid channelId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<IReadOnlyList<WorkspaceChannelResponse>> GetChannelsAsync(
        Guid projectId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<WorkspaceChannelResponse> CreateChannelAsync(
        Guid projectId, Guid accountId, CreateWorkspaceChannelRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<WorkspaceChannelResponse> GetChannelAsync(
        Guid projectId, Guid channelId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<WorkspaceChannelResponse> UpdateChannelAsync(
        Guid projectId, Guid channelId, Guid accountId, UpdateWorkspaceChannelRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task ArchiveChannelAsync(Guid projectId, Guid channelId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<WorkspaceMessagePageResponse> GetMessagesAsync(
        Guid projectId, Guid channelId, Guid accountId, int page, int pageSize, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<WorkspaceMessageResponse> SendMessageAsync(
        Guid projectId, Guid channelId, Guid accountId, SendWorkspaceMessageRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<WorkspaceMessageResponse> SendMessageAsync(
        Guid channelId, Guid accountId, SendWorkspaceMessageRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
}
