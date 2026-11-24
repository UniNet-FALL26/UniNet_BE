using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Messaging;

namespace UniNet.Application.Services;

public sealed partial class MessagingService
{
    public async Task<ChatMessageDto> SendDirect(Guid actor, Guid profileId, ChatSendRequest request, ChatUploadedFile[] files, CancellationToken ct)
    {
        await Active(actor, ct); var peer = await PersonByProfile(profileId, ct);
        Require(actor != peer.AccountId, "CHAT_SELF", "Không thể nhắn tin cho chính mình.");
        var pair = Pair(actor, peer.AccountId);
        var message = await Transaction($"chat:sender:{actor:N}", async () => {
            Require(!await Blocked(actor, peer.AccountId, ct), "CHAT_BLOCKED", "Không thể gửi tin nhắn do quan hệ chặn.", 403);
            var recent = DateTimeOffset.UtcNow.AddHours(-1);
            if (!await Friends(actor, peer.AccountId, ct) && !await db.Conversations.AnyAsync(c => c.Type == 0 && c.DirectAccountLowId == pair.Low && c.DirectAccountHighId == pair.High, ct))
                Require(await db.Conversations.CountAsync(c => c.Type == 0 && c.CreatedByAccountId == actor && c.CreatedAt > recent, ct) < 10, "CHAT_SPAM_LIMIT", "Bạn đã bắt đầu nhiều cuộc trò chuyện mới. Vui lòng thử lại sau.", 429);
            var conversation = await EnsureDirect(db, actor, peer.AccountId, ct); await db.SaveChangesAsync(ct);
            await Lock(db, $"chat:conversation:{conversation.Id:N}", ct);
            return await SaveMessage(actor, conversation, request, files, ct);
        }, ct);
        return (await MessageDtos([message], ct))[0];
    }
    public async Task<ChatMessageDto> Send(Guid actor, Guid conversationId, ChatSendRequest request, ChatUploadedFile[] files, CancellationToken ct)
    {
        var message = await Transaction($"chat:sender:{actor:N}", async () => {
            await Lock(db, $"chat:conversation:{conversationId:N}", ct);
            await Member(actor, conversationId, ct);
            var conversation = await db.Conversations.SingleAsync(c => c.Id == conversationId, ct);
            return await SaveMessage(actor, conversation, request, files, ct);
        }, ct);
        return (await MessageDtos([message], ct))[0];
    }
    private async Task<ChatMessage> SaveMessage(Guid actor, Conversation conversation, ChatSendRequest request, ChatUploadedFile[] files, CancellationToken ct)
    {
        await Member(actor, conversation.Id, ct);
        var text = request.Content?.Trim();
        Require(request.ClientMessageId != Guid.Empty, "CHAT_CLIENT_ID", "Thiếu mã gửi tin nhắn.");
        Require(text?.Length <= 8000 || text == null, "CHAT_MESSAGE_TOO_LONG", "Tin nhắn tối đa 8.000 ký tự.");
        Require(!string.IsNullOrEmpty(text) || files.Length != 0, "CHAT_MESSAGE_EMPTY", "Nhập nội dung hoặc chọn file để gửi.");
        Require(files.Length <= 6 && files.Sum(f => f.FileSize) <= 50 * 1024 * 1024, "CHAT_FILES_LIMIT", "Mỗi tin nhắn tối đa 6 file và tổng 50 MB.");
        var replay = await db.Messages.SingleOrDefaultAsync(m => m.SenderAccountId == actor && m.ClientMessageId == request.ClientMessageId, ct);
        if (replay != null) {
            Require(replay.ConversationId == conversation.Id && replay.ReplyToMessageId == request.ReplyToMessageId && (replay.RecalledAt != null || replay.Content == text), "CHAT_CLIENT_ID_CONFLICT", "Mã gửi tin nhắn đã được dùng cho nội dung khác.", 409);
            return replay;
        }
        if (conversation.Type == 0) {
            var peer = conversation.DirectAccountLowId == actor ? conversation.DirectAccountHighId!.Value : conversation.DirectAccountLowId!.Value;
            await Active(peer, ct);
            Require(!await Blocked(actor, peer, ct), "CHAT_BLOCKED", "Không thể gửi tin nhắn do quan hệ chặn.", 403);
            if (!await Friends(actor, peer, ct)) {
                var strangerWindow = DateTimeOffset.UtcNow.AddMinutes(-1);
                Require(await db.Messages.CountAsync(m => m.SenderAccountId == actor && m.ConversationId == conversation.Id && m.CreatedAt > strangerWindow, ct) < 12,
                    "CHAT_SPAM_LIMIT", "Bạn đang gửi tin quá nhanh. Vui lòng đợi một phút.", 429);
            }
        }
        if (request.ReplyToMessageId.HasValue) Require(await db.Messages.AnyAsync(m => m.Id == request.ReplyToMessageId && m.ConversationId == conversation.Id, ct), "CHAT_REPLY_INVALID", "Tin nhắn trả lời không thuộc cuộc trò chuyện.");
        var now = DateTimeOffset.UtcNow;
        var message = new ChatMessage { ConversationId = conversation.Id, SenderAccountId = actor, Content = text,
            ClientMessageId = request.ClientMessageId, ReplyToMessageId = request.ReplyToMessageId,
            Number = ++conversation.NextMessageNumber, CreatedAt = now, Type = (short)(files.Length == 0 ? 0 : 1) };
        db.Messages.Add(message); conversation.LastMessageAt = now;
        foreach (var file in files) db.MessageAttachments.Add(new() { Id = file.Id, MessageId = message.Id, FileType = file.FileType,
            FileName = file.FileName, FileSize = file.FileSize, MimeType = file.MimeType, StorageKey = file.StorageKey,
            FileUrl = $"/messaging/attachments/{file.Id}/content", CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await Queue(conversation.Id, "message", message.Id, ct);
        var recipientIds = await db.ConversationMembers.Where(m => m.ConversationId == conversation.Id && m.AccountId != actor && m.LeftAt == null).Select(m => m.AccountId).ToArrayAsync(ct);
        var devices = await db.PushDevices.Where(d => recipientIds.Contains(d.AccountId) && d.IsActive).ToArrayAsync(ct);
        foreach (var device in devices) db.Set<NotificationDelivery>().Add(new() { AccountId = device.AccountId, ConversationId = conversation.Id,
            MessageId = message.Id, PushDeviceId = device.Id, Reason = "chat.push", RetryAt = now.AddSeconds(3) });
        return message;
    }
    private async Task<ChatMessage> MessageFor(Guid actor, Guid id, CancellationToken ct)
    {
        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == id, ct) ?? throw Error("CHAT_MESSAGE_NOT_FOUND", "Không tìm thấy tin nhắn.", 404);
        await Member(actor, message.ConversationId, ct); return message;
    }
    public async Task<ChatMessageDto> Edit(Guid actor, Guid id, string content, CancellationToken ct)
    {
        var original = await MessageFor(actor, id, ct);
        var updated = await Transaction($"chat:conversation:{original.ConversationId:N}", async () => {
            var message = await MessageFor(actor, id, ct); await db.Entry(message).ReloadAsync(ct);
            Require(message.SenderAccountId == actor, "CHAT_FORBIDDEN", "Bạn chỉ được chỉnh sửa tin nhắn của mình.", 403);
            Require(message.RecalledAt == null && message.Type != 2, "CHAT_MESSAGE_RECALLED", "Không thể chỉnh sửa tin nhắn này.", 409);
            Require(!string.IsNullOrWhiteSpace(content) && content.Trim().Length <= 8000, "CHAT_MESSAGE_INVALID", "Nội dung từ 1 đến 8.000 ký tự.");
            message.Content = content.Trim(); message.EditedAt = DateTimeOffset.UtcNow;
            await Queue(message.ConversationId, "edited", message.Id, ct); return message;
        }, ct);
        return (await MessageDtos([updated], ct))[0];
    }
    public async Task Recall(Guid actor, Guid id, CancellationToken ct)
    {
        var original = await MessageFor(actor, id, ct);
        await Transaction($"chat:conversation:{original.ConversationId:N}", async () => {
            var message = await MessageFor(actor, id, ct); await db.Entry(message).ReloadAsync(ct);
            Require(message.SenderAccountId == actor && message.Type != 2, "CHAT_FORBIDDEN", "Bạn chỉ được thu hồi tin nhắn của mình.", 403);
            if (message.RecalledAt != null) return true;
            message.RecalledAt = DateTimeOffset.UtcNow; message.Content = null;
            db.MessageReactions.RemoveRange(await db.MessageReactions.Where(r => r.MessageId == id).ToListAsync(ct));
            await Queue(message.ConversationId, "recalled", id, ct); return true;
        }, ct);
    }
    public async Task React(Guid actor, Guid id, string? reactionType, CancellationToken ct)
    {
        Require(reactionType == null || new[] { "👍", "❤️", "😂", "😮", "😢", "😡" }.Contains(reactionType), "CHAT_REACTION_INVALID", "Cảm xúc không hợp lệ.");
        var original = await MessageFor(actor, id, ct);
        await Transaction($"chat:conversation:{original.ConversationId:N}", async () => {
            var message = await MessageFor(actor, id, ct); await db.Entry(message).ReloadAsync(ct);
            Require(message.RecalledAt == null && message.Type != 2, "CHAT_MESSAGE_RECALLED", "Tin nhắn đã thu hồi.", 409);
            var old = await db.MessageReactions.SingleOrDefaultAsync(r => r.MessageId == id && r.AccountId == actor, ct);
            if (reactionType == null || old?.ReactionType == reactionType) { if (old != null) db.MessageReactions.Remove(old); }
            else if (old != null) { old.ReactionType = reactionType; old.CreatedAt = DateTimeOffset.UtcNow; }
            else db.MessageReactions.Add(new() { MessageId = id, AccountId = actor, ReactionType = reactionType });
            await Queue(message.ConversationId, "reaction", id, ct); return true;
        }, ct);
    }
    public async Task Read(Guid actor, Guid conversationId, ChatReadRequest request, CancellationToken ct)
    {
        await Transaction($"chat:conversation:{conversationId:N}", async () => {
            var member = await Member(actor, conversationId, ct);
            var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == request.MessageId && m.ConversationId == conversationId, ct)
                ?? throw Error("CHAT_READ_INVALID", "Tin nhắn không thuộc cuộc trò chuyện.");
            var previous = member.LastReadMessageId == null ? 0 : await db.Messages.Where(m => m.Id == member.LastReadMessageId).Select(m => m.Number).SingleAsync(ct);
            if (message.Number > previous) { member.LastReadMessageId = message.Id; await Queue(conversationId, "read", message.Id, ct); }
            if (request.DeviceId != null) await AcknowledgeInTransaction(actor, request.DeviceId, conversationId, message.Number, ct);
            return true;
        }, ct);
    }
    private async Task AcknowledgeInTransaction(Guid actor, string deviceId, Guid conversationId, long throughNumber, CancellationToken ct)
    {
        var devices = db.PushDevices.Where(d => d.AccountId == actor && d.DeviceId == deviceId).Select(d => d.Id);
        var messageIds = db.Messages.Where(m => m.ConversationId == conversationId && m.Number <= throughNumber).Select(m => m.Id);
        var queued = await db.Set<NotificationDelivery>().Where(d => d.AccountId == actor && d.Reason == "chat.push" && d.PushDeviceId != null && devices.Contains(d.PushDeviceId.Value) && d.MessageId != null && messageIds.Contains(d.MessageId.Value) && d.DeliveredAt == null).ToListAsync(ct);
        foreach (var row in queued) row.DeliveredAt = DateTimeOffset.UtcNow;
    }
    public async Task Acknowledge(Guid actor, string deviceId, Guid messageId, CancellationToken ct)
    {
        var message = await MessageFor(actor, messageId, ct);
        await AcknowledgeInTransaction(actor, deviceId, message.ConversationId, message.Number, ct); await db.SaveChangesAsync(ct);
    }
    public async Task<MessageAttachment> Attachment(Guid actor, Guid attachmentId, CancellationToken ct)
    {
        var file = await db.MessageAttachments.AsNoTracking().SingleOrDefaultAsync(f => f.Id == attachmentId, ct) ?? throw Error("CHAT_FILE_NOT_FOUND", "Không tìm thấy file.", 404);
        var message = await MessageFor(actor, file.MessageId, ct);
        Require(message.RecalledAt == null, "CHAT_FILE_RECALLED", "File thuộc tin nhắn đã thu hồi.", 410);
        return file;
    }
}
