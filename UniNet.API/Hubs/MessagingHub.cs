using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Hubs;

[Authorize]
public sealed class MessagingHub(MessagingService messaging, UniNetDbContext db) : Hub
{
    private static readonly ConcurrentDictionary<(Guid, Guid), DateTimeOffset> TypingTimes = new();
    private Guid Actor => Guid.TryParse(Context.UserIdentifier, out var id) ? id : throw new HubException("UNAUTHORIZED");
    public override async Task OnConnectedAsync() { await messaging.Active(Actor, Context.ConnectionAborted); await base.OnConnectedAsync(); }
    public Task SetPresence(ChatPresenceRequest request) => messaging.Presence(Actor, Context.ConnectionId, request, Context.ConnectionAborted);
    public async Task JoinConversation(Guid conversationId)
    {
        await messaging.Member(Actor, conversationId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{conversationId:N}", Context.ConnectionAborted);
    }
    public Task LeaveConversation(Guid conversationId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat:{conversationId:N}", Context.ConnectionAborted);
    public async Task Typing(Guid conversationId, bool typing)
    {
        await messaging.Member(Actor, conversationId, Context.ConnectionAborted);
        var now = DateTimeOffset.UtcNow; var key = (Actor, conversationId);
        if (TypingTimes.TryGetValue(key, out var previous) && now - previous < TimeSpan.FromMilliseconds(750)) return;
        TypingTimes[key] = now;
        if (TypingTimes.Count > 10000) foreach (var item in TypingTimes.Where(p => now - p.Value > TimeSpan.FromMinutes(1))) TypingTimes.TryRemove(item.Key, out _);
        var recipients = await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.AccountId != Actor && m.LeftAt == null &&
            !db.AccountBlocks.Any(b => b.AccountId == Actor && b.BlockedAccountId == m.AccountId || b.AccountId == m.AccountId && b.BlockedAccountId == Actor))
            .Select(m => m.AccountId.ToString()).ToArrayAsync(Context.ConnectionAborted);
        await Clients.Users(recipients).SendAsync("TypingChanged", new { ConversationId = conversationId, AccountId = Actor, IsTyping = typing }, Context.ConnectionAborted);
    }
    public Task Acknowledge(string deviceId, Guid messageId) => messaging.Acknowledge(Actor, deviceId, messageId, Context.ConnectionAborted);
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try { await messaging.Disconnect(Actor, Context.ConnectionId, CancellationToken.None); }
        finally { await base.OnDisconnectedAsync(exception); }
    }
}
