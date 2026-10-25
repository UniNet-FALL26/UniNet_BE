namespace UniNet.Application.Interfaces;

public interface IWorkspaceChatService
{
    Task<WorkspaceChannelAccessResponse> AuthorizeChannelAsync(
        Guid channelId, Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkspaceChannelResponse>> GetChannelsAsync(
        Guid projectId, Guid accountId, CancellationToken ct = default);
    Task<WorkspaceChannelResponse> CreateChannelAsync(
        Guid projectId, Guid accountId, CreateWorkspaceChannelRequest request, CancellationToken ct = default);
    Task<WorkspaceChannelResponse> GetChannelAsync(
        Guid projectId, Guid channelId, Guid accountId, CancellationToken ct = default);
    Task<WorkspaceChannelResponse> UpdateChannelAsync(
        Guid projectId, Guid channelId, Guid accountId, UpdateWorkspaceChannelRequest request, CancellationToken ct = default);
    Task ArchiveChannelAsync(Guid projectId, Guid channelId, Guid accountId, CancellationToken ct = default);
    Task<WorkspaceMessagePageResponse> GetMessagesAsync(
        Guid projectId, Guid channelId, Guid accountId, int page, int pageSize, CancellationToken ct = default);
    Task<WorkspaceMessageResponse> SendMessageAsync(
        Guid projectId, Guid channelId, Guid accountId, SendWorkspaceMessageRequest request, CancellationToken ct = default);
    Task<WorkspaceMessageResponse> SendMessageAsync(
        Guid channelId, Guid accountId, SendWorkspaceMessageRequest request, CancellationToken ct = default);
}
