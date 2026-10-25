using Microsoft.AspNetCore.SignalR;
using UniNet.API.Hubs;
using UniNet.Application.Interfaces;

namespace UniNet.API.Services;

public sealed class WorkspaceMemberRevoker(
    IWorkspaceConnectionTracker tracker,
    IHubContext<WorkspaceChatHub> hub) : IWorkspaceMemberRevoker
{
    public async Task RevokeMemberAsync(Guid projectId, Guid userProfileId, CancellationToken ct = default)
    {
        foreach (var (connectionId, channelId) in tracker.GetMemberSubscriptions(projectId, userProfileId))
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, WorkspaceChatHub.GroupName(channelId), ct);
            tracker.Remove(connectionId, channelId);
        }
    }
}
