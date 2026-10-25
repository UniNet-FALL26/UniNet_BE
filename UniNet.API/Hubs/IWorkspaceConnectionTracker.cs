namespace UniNet.API.Hubs;

public interface IWorkspaceConnectionTracker
{
    void Add(string connectionId, Guid projectId, Guid userProfileId, Guid channelId);
    bool Remove(string connectionId, Guid channelId);
    IReadOnlyList<(string ConnectionId, Guid ChannelId)> GetMemberSubscriptions(Guid projectId, Guid userProfileId);
    void RemoveConnection(string connectionId);
}
