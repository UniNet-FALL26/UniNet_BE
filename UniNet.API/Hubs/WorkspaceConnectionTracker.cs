namespace UniNet.API.Hubs;

public sealed class WorkspaceConnectionTracker : IWorkspaceConnectionTracker
{
    private readonly object gate = new();
    private readonly Dictionary<string, HashSet<Subscription>> subscriptionsByConnection = new(StringComparer.Ordinal);

    public void Add(string connectionId, Guid projectId, Guid userProfileId, Guid channelId)
    {
        lock (gate)
        {
            if (!subscriptionsByConnection.TryGetValue(connectionId, out var subscriptions))
                subscriptionsByConnection[connectionId] = subscriptions = [];
            subscriptions.Add(new(projectId, userProfileId, channelId));
        }
    }

    public bool Remove(string connectionId, Guid channelId)
    {
        lock (gate)
        {
            if (!subscriptionsByConnection.TryGetValue(connectionId, out var subscriptions))
                return false;
            var removed = subscriptions.RemoveWhere(subscription => subscription.ChannelId == channelId) > 0;
            if (subscriptions.Count == 0)
                subscriptionsByConnection.Remove(connectionId);
            return removed;
        }
    }

    public IReadOnlyList<(string ConnectionId, Guid ChannelId)> GetMemberSubscriptions(
        Guid projectId, Guid userProfileId)
    {
        lock (gate)
            return subscriptionsByConnection
                .SelectMany(pair => pair.Value
                    .Where(subscription => subscription.ProjectId == projectId &&
                        subscription.UserProfileId == userProfileId)
                    .Select(subscription => (pair.Key, subscription.ChannelId)))
                .ToArray();
    }

    public void RemoveConnection(string connectionId)
    {
        lock (gate)
            subscriptionsByConnection.Remove(connectionId);
    }

    private sealed record Subscription(Guid ProjectId, Guid UserProfileId, Guid ChannelId);
}
