using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Messaging;

namespace UniNet.Application.Services;

public sealed partial class MessagingService
{
    private static void DeviceId(string id) => Require(!string.IsNullOrWhiteSpace(id) && id.Length is >= 8 and <= 100 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'), "CHAT_DEVICE_INVALID", "Mã thiết bị không hợp lệ.");
    public async Task RegisterDevice(Guid actor, PushDeviceRequest request, CancellationToken ct)
    {
        var account = await Active(actor, ct); DeviceId(request.DeviceId);
        Require(request.Platform is "Web" or "Android" or "iOS" && request.Token.Length is >= 10 and <= 2048, "PUSH_DEVICE_INVALID", "Thông tin thiết bị nhận thông báo không hợp lệ.");
        await Transaction("push:token:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(request.Token))), async () => {
            var device = await db.PushDevices.SingleOrDefaultAsync(d => d.AccountId == actor && d.DeviceId == request.DeviceId, ct);
            var previous = await db.PushDevices.SingleOrDefaultAsync(d => d.Token == request.Token, ct);
            if (previous != null && previous.Id != device?.Id) { db.PushDevices.Remove(previous); await db.SaveChangesAsync(ct); }
            if (device == null) { device = new() { AccountId = actor, DeviceId = request.DeviceId }; db.PushDevices.Add(device); }
            device.Token = request.Token; device.Platform = request.Platform; device.ShowPreview = request.ShowPreview;
            device.SessionVersion = account.SessionVersion; device.IsActive = true; device.UpdatedAt = device.LastActiveAt = DateTimeOffset.UtcNow;
            return true;
        }, ct);
    }
    public async Task UnregisterDevice(Guid actor, string id, CancellationToken ct)
    {
        await Active(actor, ct); DeviceId(id);
        var devices = await db.PushDevices.Where(d => d.AccountId == actor && d.DeviceId == id).ToListAsync(ct);
        foreach (var device in devices) { device.IsActive = false; device.UpdatedAt = DateTimeOffset.UtcNow; }
        var sessions = await db.ChatDeviceSessions.Where(s => s.AccountId == actor && s.DeviceId == id).ToListAsync(ct);
        db.ChatDeviceSessions.RemoveRange(sessions); await db.SaveChangesAsync(ct);
    }
    public async Task Presence(Guid actor, string connectionId, ChatPresenceRequest request, CancellationToken ct)
    {
        await Active(actor, ct); DeviceId(request.DeviceId);
        if (request.ConversationId.HasValue) await Member(actor, request.ConversationId.Value, ct);
        var session = await db.ChatDeviceSessions.SingleOrDefaultAsync(s => s.Id == connectionId && s.AccountId == actor, ct);
        if (session == null) { session = new() { Id = connectionId, AccountId = actor }; db.ChatDeviceSessions.Add(session); }
        session.DeviceId = request.DeviceId; session.ActiveConversationId = request.IsForeground ? request.ConversationId : null;
        session.IsForeground = request.IsForeground; session.UpdatedAt = DateTimeOffset.UtcNow;
        var device = await db.PushDevices.SingleOrDefaultAsync(d => d.AccountId == actor && d.DeviceId == request.DeviceId, ct);
        if (device != null) device.LastActiveAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task Disconnect(Guid actor, string connectionId, CancellationToken ct)
    {
        var session = await db.ChatDeviceSessions.SingleOrDefaultAsync(s => s.AccountId == actor && s.Id == connectionId, ct);
        if (session != null) { db.ChatDeviceSessions.Remove(session); await db.SaveChangesAsync(ct); }
    }
    public async Task<ChatEvent> EventFor(Guid actor, Guid id, Guid? messageId, string kind, CancellationToken ct)
    {
        var member = await db.ConversationMembers.AsNoTracking().SingleOrDefaultAsync(m => m.AccountId == actor && m.ConversationId == id && m.LeftAt == null, ct);
        if (member == null) return new("removed", id, null, null, null, false, DateTimeOffset.UtcNow);
        var conversation = await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == id, ct);
        ChatMessageDto? message = null;
        if (messageId != null) {
            var row = await db.Messages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id, ct);
            if (row != null) message = (await MessageDtos([row], ct))[0];
        }
        ChatPushPayload? payload = null;
        var notify = false;
        if (message != null && message.Sender.AccountId != actor && message.RecalledAt == null && kind == "message") {
            var blocked = await Blocked(actor, message.Sender.AccountId, ct);
            var readNumber = member.LastReadMessageId.HasValue ? await db.Messages.Where(m => m.Id == member.LastReadMessageId).Select(m => m.Number).SingleOrDefaultAsync(ct) : 0;
            notify = !blocked && readNumber < message.Number && (member.MutedUntil == null || member.MutedUntil <= DateTimeOffset.UtcNow);
            var preview = message.Content ?? (message.Attachments.Length == 0 ? "Tin nhắn mới" : message.Attachments.All(f => f.FileType == 0) ? $"Đã gửi {message.Attachments.Length} ảnh" : message.Attachments.All(f => f.FileType == 1) ? "Đã gửi video" : "Đã gửi tài liệu");
            if (preview.Length > 180) preview = preview[..177] + "…";
            payload = new("chat_message", id, message.Id, conversation.Type, message.Sender.AccountId,
                message.Sender.FullName, message.Sender.AvatarUrl, conversation.Type == 0 ? message.Sender.FullName : conversation.Name ?? "Nhóm trò chuyện",
                conversation.Type == 0 ? message.Sender.AvatarUrl : conversation.AvatarUrl, preview, $"/messages?conversationId={id}&messageId={message.Id}");
        }
        return new(kind, id, messageId, message, payload, notify, DateTimeOffset.UtcNow);
    }
}
