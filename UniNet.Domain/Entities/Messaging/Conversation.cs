namespace UniNet.Domain.Entities.Messaging;

public sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public short Type { get; set; }
    public string? Name { get; set; }
    public string? AvatarUrl { get; set; }
    public Guid CreatedByAccountId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? DirectAccountLowId { get; set; }
    public Guid? DirectAccountHighId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastMessageAt { get; set; }
    public long NextMessageNumber { get; set; }
}

public sealed class ConversationMember
{
    public Guid ConversationId { get; set; }
    public Guid AccountId { get; set; }
    public short Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LeftAt { get; set; }
    public Guid? LastReadMessageId { get; set; }
    public DateTimeOffset? MutedUntil { get; set; }
    public bool IsPinned { get; set; }
}

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid SenderAccountId { get; set; }
    public string? Content { get; set; }
    public short Type { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public Guid ClientMessageId { get; set; }
    public long Number { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? RecalledAt { get; set; }
}

public sealed class MessageAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public short FileType { get; set; }
    public string FileName { get; set; } = "";
    public string FileUrl { get; set; } = "";
    public long FileSize { get; set; }
    public string MimeType { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    // Authenticated Cloudinary storage identifier; never exposed in message DTOs.
    public string StorageKey { get; set; } = "";
}

public sealed class MessageReaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    public Guid AccountId { get; set; }
    public string ReactionType { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AccountBlock
{
    public Guid AccountId { get; set; }
    public Guid BlockedAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PushDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = "";
    public string Token { get; set; } = "";
    public string Platform { get; set; } = "";
    public int SessionVersion { get; set; }
    public bool ShowPreview { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ChatDeviceSession
{
    public string Id { get; set; } = "";
    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = "";
    public Guid? ActiveConversationId { get; set; }
    public bool IsForeground { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
