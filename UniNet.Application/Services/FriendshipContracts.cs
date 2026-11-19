namespace UniNet.Application.Services;

public sealed record FriendPerson(Guid AccountId, Guid ProfileId, string FullName, string Nickname,
    string? AvatarUrl, short Role, string Relationship, Guid? FriendshipId, long? FriendshipVersion, int MutualFriendsCount = 0);
public sealed record FriendPage(IReadOnlyList<FriendPerson> Items, int Page, int PageSize, int Total);
public sealed record SendFriendRequest(Guid ReceiverAccountId);
public sealed record FriendshipDecision(long Version);
