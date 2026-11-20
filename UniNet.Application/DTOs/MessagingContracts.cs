namespace UniNet.Application;

public sealed record ChatPerson(Guid AccountId, Guid ProfileId, string FullName, string? AvatarUrl);
public sealed record ChatMember(ChatPerson Person, short Role, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt, Guid? LastReadMessageId);
public sealed record ChatProject(Guid Id, string Title, string? LogoUrl, string Description);
public sealed record ChatAttachment(Guid Id, short FileType, string FileName, string FileUrl, long FileSize, string MimeType);
public sealed record ChatReaction(Guid AccountId, string ReactionType);
public sealed record ChatReply(Guid Id, string? Content, string SenderName, bool Recalled);
public sealed record ChatMessageDto(Guid Id, Guid ConversationId, ChatPerson Sender, string? Content, short Type, long Number,
    Guid ClientMessageId, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, DateTimeOffset? RecalledAt,
    ChatReply? ReplyTo, ChatAttachment[] Attachments, ChatReaction[] Reactions);
public sealed record ChatConversation(Guid Id, short Type, string Name, string? AvatarUrl, ChatPerson? Peer,
    string Relationship, bool IsBlocked, bool BlockedByMe, short MyRole, bool IsPinned, DateTimeOffset? MutedUntil,
    int UnreadCount, DateTimeOffset CreatedAt, DateTimeOffset? LastMessageAt, ChatMessageDto? LastMessage,
    ChatProject? Project, ChatMember[] Members);
public sealed record ChatPage<T>(T[] Items, bool HasMore, long? NextCursor = null);
public sealed record DirectChatResult(Guid? ConversationId, ChatPerson Peer, bool IsBlocked);
public sealed record ChatSendRequest(Guid ClientMessageId, string? Content, Guid? ReplyToMessageId = null, string? DeviceId = null);
public sealed record ChatGroupRequest(string? Name, string? AvatarUrl, Guid[] AccountIds, Guid? ProjectId = null, Guid[]? MemberProfileIds = null);
public sealed record ChatGroupUpdate(string? Name, string? AvatarUrl);
public sealed record ChatMemberRequest(Guid[] AccountIds, Guid[]? ProfileIds = null);
public sealed record ChatMemberSettings(bool IsPinned, DateTimeOffset? MutedUntil);
public sealed record ChatReadRequest(Guid MessageId, string? DeviceId = null);
public sealed record ChatRoleRequest(short Role);
public sealed record ChatEditRequest(string Content);
public sealed record ChatReactionRequest(string? ReactionType);
public sealed record ChatProjectLinkRequest(Guid? ProjectId);
public sealed record PushDeviceRequest(string DeviceId, string Token, string Platform, bool ShowPreview = true);
public sealed record ChatPresenceRequest(string DeviceId, Guid? ConversationId, bool IsForeground);
public sealed record ChatUploadedFile(Guid Id, short FileType, string FileName, long FileSize, string MimeType, string StorageKey);
public sealed record ChatPushPayload(string Type, Guid ConversationId, Guid MessageId, short ConversationType,
    Guid SenderAccountId, string SenderName, string? SenderAvatarUrl, string ConversationName,
    string? ConversationAvatarUrl, string MessagePreview, string DeepLink);
public sealed record ChatEvent(string Kind, Guid ConversationId, Guid? MessageId, ChatMessageDto? Message,
    ChatPushPayload? Notification, bool Notify, DateTimeOffset OccurredAt);
