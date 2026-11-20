using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Messaging;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed partial class MessagingService(UniNetDbContext db)
{
    private static ProjectException Error(string code, string message, int status = 400) => new(code, message, status);
    private static void Require(bool condition, string code, string message, int status = 400) { if (!condition) throw Error(code, message, status); }
    public async Task<Account> Active(Guid actor, CancellationToken ct) => await db.Accounts.AsNoTracking()
        .SingleOrDefaultAsync(a => a.Id == actor && a.Status == AccountStatus.Active, ct)
        ?? throw Error("UNAUTHORIZED", "Tài khoản không còn hoạt động.", 401);
    public async Task<ConversationMember> Member(Guid actor, Guid conversationId, CancellationToken ct)
    {
        await Active(actor, ct);
        return await db.ConversationMembers.SingleOrDefaultAsync(m => m.AccountId == actor && m.ConversationId == conversationId && m.LeftAt == null, ct)
            ?? throw Error("CHAT_FORBIDDEN", "Bạn không có quyền truy cập cuộc trò chuyện này.", 403);
    }
    private Task<bool> Friends(Guid a, Guid b, CancellationToken ct) => db.Friendships.AnyAsync(f => f.Status == FriendshipStatus.Accepted &&
        (f.RequesterAccountId == a && f.ReceiverAccountId == b || f.RequesterAccountId == b && f.ReceiverAccountId == a), ct);
    public Task<bool> Blocked(Guid a, Guid b, CancellationToken ct) => db.AccountBlocks.AnyAsync(x => x.AccountId == a && x.BlockedAccountId == b || x.AccountId == b && x.BlockedAccountId == a, ct);
    private async Task<ChatPerson> PersonByProfile(Guid profileId, CancellationToken ct) => await db.UserProfiles.AsNoTracking()
        .Where(p => p.Id == profileId && p.Account.Status == AccountStatus.Active)
        .Select(p => new ChatPerson(p.AccountId, p.Id, p.Nickname == "" ? p.FullName : p.Nickname, p.AvatarUrl)).SingleOrDefaultAsync(ct)
        ?? throw Error("CHAT_USER_NOT_FOUND", "Không tìm thấy người dùng này.", 404);
    private static (Guid Low, Guid High) Pair(Guid a, Guid b) => string.CompareOrdinal(a.ToString("N"), b.ToString("N")) < 0 ? (a, b) : (b, a);
    private static async Task Lock(UniNetDbContext context, string resource, CancellationToken ct)
    {
        if (!context.Database.IsNpgsql()) return;
        var key = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }
    private async Task<T> Transaction<T>(string resource, Func<Task<T>> action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try { await Lock(db, resource, ct); var result = await action(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result; }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw Error("CHAT_CONFLICT", "Cuộc trò chuyện vừa thay đổi. Vui lòng tải lại.", 409); }
    }

    // Also used inside FriendshipService's existing acceptance transaction.
    public static async Task<Conversation> EnsureDirect(UniNetDbContext context, Guid a, Guid b, CancellationToken ct)
    {
        Require(a != b, "CHAT_SELF", "Không thể tạo cuộc trò chuyện với chính mình.");
        var pair = Pair(a, b);
        await Lock(context, $"chat:direct:{pair.Low:N}:{pair.High:N}", ct);
        var current = await context.Conversations.SingleOrDefaultAsync(c => c.Type == 0 && c.DirectAccountLowId == pair.Low && c.DirectAccountHighId == pair.High, ct);
        if (current != null) return current;
        var conversation = new Conversation { Type = 0, CreatedByAccountId = a, DirectAccountLowId = pair.Low, DirectAccountHighId = pair.High };
        context.Conversations.Add(conversation);
        context.ConversationMembers.AddRange(new ConversationMember { ConversationId = conversation.Id, AccountId = a }, new ConversationMember { ConversationId = conversation.Id, AccountId = b });
        return conversation;
    }

    public async Task<DirectChatResult> Direct(Guid actor, Guid profileId, CancellationToken ct)
    {
        await Active(actor, ct); var peer = await PersonByProfile(profileId, ct);
        Require(peer.AccountId != actor, "CHAT_SELF", "Bạn đang xem hồ sơ của mình.");
        var pair = Pair(actor, peer.AccountId);
        var id = await db.Conversations.Where(c => c.Type == 0 && c.DirectAccountLowId == pair.Low && c.DirectAccountHighId == pair.High).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
        return new(id, peer, await Blocked(actor, peer.AccountId, ct));
    }

    public async Task<ChatPage<ChatConversation>> List(Guid actor, string? filter, string? query, int page, CancellationToken ct)
    {
        await Active(actor, ct); Require(page is >= 1 and <= 10000, "INVALID_PAGE", "Trang không hợp lệ.");
        var q = query?.Trim() ?? ""; Require(q.Length <= 100, "INVALID_SEARCH", "Từ khóa quá dài.");
        var rows = from c in db.Conversations.AsNoTracking()
                   join own in db.ConversationMembers on c.Id equals own.ConversationId
                   where own.AccountId == actor && own.LeftAt == null
                   select new { Conversation = c, Own = own };
        if (filter == "groups") rows = rows.Where(x => x.Conversation.Type == 1);
        if (filter is "friends" or "strangers") {
            var friend = filter == "friends";
            rows = rows.Where(x => x.Conversation.Type == 0 && db.Friendships.Any(f => f.Status == FriendshipStatus.Accepted &&
                (f.RequesterAccountId == x.Conversation.DirectAccountLowId && f.ReceiverAccountId == x.Conversation.DirectAccountHighId ||
                 f.RequesterAccountId == x.Conversation.DirectAccountHighId && f.ReceiverAccountId == x.Conversation.DirectAccountLowId)) == friend);
        }
        if (q != "") rows = rows.Where(x => (x.Conversation.Name != null && x.Conversation.Name.Contains(q)) || db.ConversationMembers.Any(m => m.ConversationId == x.Conversation.Id && m.AccountId != actor && db.UserProfiles.Any(p => p.AccountId == m.AccountId && (p.FullName.Contains(q) || p.Nickname.Contains(q)))));
        var conversations = await rows.OrderByDescending(x => x.Own.IsPinned).ThenByDescending(x => x.Conversation.LastMessageAt ?? x.Conversation.CreatedAt).ThenByDescending(x => x.Conversation.Id)
            .Skip((page - 1) * 25).Take(26).Select(x => x.Conversation).ToArrayAsync(ct);
        return new(await Describe(actor, conversations.Take(25).ToArray(), ct), conversations.Length > 25);
    }
    public async Task<ChatConversation> Get(Guid actor, Guid id, CancellationToken ct)
    {
        await Member(actor, id, ct);
        return (await Describe(actor, [await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == id, ct)], ct))[0];
    }
    public async Task<int> Unread(Guid actor, CancellationToken ct)
    {
        await Active(actor, ct);
        return await (from m in db.Messages join own in db.ConversationMembers on m.ConversationId equals own.ConversationId
            where own.AccountId == actor && own.LeftAt == null && m.SenderAccountId != actor && m.RecalledAt == null &&
                m.Number > (db.Messages.Where(last => last.Id == own.LastReadMessageId).Select(last => (long?)last.Number).FirstOrDefault() ?? 0)
            select m.Id).CountAsync(ct);
    }

    private async Task<ChatConversation[]> Describe(Guid actor, Conversation[] conversations, CancellationToken ct)
    {
        if (conversations.Length == 0) return [];
        var ids = conversations.Select(c => c.Id).ToArray();
        var memberships = await db.ConversationMembers.AsNoTracking().Where(m => ids.Contains(m.ConversationId)).ToArrayAsync(ct);
        var accountIds = memberships.Select(m => m.AccountId).Distinct().ToArray();
        var people = await db.UserProfiles.AsNoTracking().Where(p => accountIds.Contains(p.AccountId)).Select(p => new ChatPerson(p.AccountId, p.Id, p.Nickname == "" ? p.FullName : p.Nickname, p.AvatarUrl)).ToDictionaryAsync(p => p.AccountId, ct);
        var lastMessages = await (from message in db.Messages.AsNoTracking() join c in db.Conversations on new { message.ConversationId, Number = message.Number } equals new { ConversationId = c.Id, Number = c.NextMessageNumber }
            where ids.Contains(c.Id) select message).ToArrayAsync(ct);
        var messages = (await MessageDtos(lastMessages, ct)).ToDictionary(m => m.ConversationId);
        var counts = await (from message in db.Messages join own in db.ConversationMembers on message.ConversationId equals own.ConversationId
            where ids.Contains(message.ConversationId) && own.AccountId == actor && message.SenderAccountId != actor && message.RecalledAt == null &&
                message.Number > (db.Messages.Where(last => last.Id == own.LastReadMessageId).Select(last => (long?)last.Number).FirstOrDefault() ?? 0)
            group message by message.ConversationId into g select new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var friends = await db.Friendships.AsNoTracking().Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterAccountId == actor || f.ReceiverAccountId == actor))
            .Select(f => f.RequesterAccountId == actor ? f.ReceiverAccountId : f.RequesterAccountId).ToArrayAsync(ct);
        var blocks = await db.AccountBlocks.AsNoTracking().Where(b => b.AccountId == actor || b.BlockedAccountId == actor).ToArrayAsync(ct);
        var projectIds = conversations.Where(c => c.ProjectId != null).Select(c => c.ProjectId!.Value).ToArray();
        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).Select(p => new ChatProject(p.Id, p.Title, p.LogoUrl, p.Description)).ToDictionaryAsync(p => p.Id, ct);
        return conversations.Select(c => {
            var own = memberships.Single(m => m.ConversationId == c.Id && m.AccountId == actor);
            var members = memberships.Where(m => m.ConversationId == c.Id && people.ContainsKey(m.AccountId)).Select(m => new ChatMember(people[m.AccountId], m.Role, m.JoinedAt, m.LeftAt, m.LastReadMessageId)).ToArray();
            var peer = c.Type == 0 ? members.First(m => m.Person.AccountId != actor).Person : null;
            return new ChatConversation(c.Id, c.Type, c.Type == 0 ? peer!.FullName : c.Name ?? "Nhóm trò chuyện", c.Type == 0 ? peer!.AvatarUrl : c.AvatarUrl,
                peer, c.Type == 1 ? "group" : friends.Contains(peer!.AccountId) ? "friends" : "strangers", peer != null && blocks.Any(b => b.AccountId == peer.AccountId || b.BlockedAccountId == peer.AccountId), peer != null && blocks.Any(b => b.AccountId == actor && b.BlockedAccountId == peer.AccountId), own.Role, own.IsPinned, own.MutedUntil,
                counts.GetValueOrDefault(c.Id), c.CreatedAt, c.LastMessageAt, messages.GetValueOrDefault(c.Id), c.ProjectId.HasValue ? projects.GetValueOrDefault(c.ProjectId.Value) : null, members);
        }).ToArray();
    }

    public async Task<ChatPage<ChatMessageDto>> History(Guid actor, Guid id, long? before, int limit, CancellationToken ct)
    {
        await Member(actor, id, ct); Require(limit is >= 1 and <= 50, "INVALID_PAGE", "Tối đa 50 tin nhắn mỗi lần tải.");
        var messages = await db.Messages.AsNoTracking().Where(m => m.ConversationId == id && (before == null || m.Number < before)).OrderByDescending(m => m.Number).Take(limit + 1).ToArrayAsync(ct);
        var page = messages.Take(limit).Reverse().ToArray();
        return new(await MessageDtos(page, ct), messages.Length > limit, page.FirstOrDefault()?.Number);
    }
    public async Task<ChatMessageDto[]> MessageDtos(ChatMessage[] messages, CancellationToken ct)
    {
        if (messages.Length == 0) return [];
        var ids = messages.Select(m => m.Id).ToArray(); var replies = messages.Where(m => m.ReplyToMessageId != null).Select(m => m.ReplyToMessageId!.Value).ToArray();
        var quoted = await db.Messages.AsNoTracking().Where(m => replies.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var accounts = messages.Select(m => m.SenderAccountId).Concat(quoted.Values.Select(m => m.SenderAccountId)).Distinct().ToArray();
        var people = await db.UserProfiles.AsNoTracking().Where(p => accounts.Contains(p.AccountId)).Select(p => new ChatPerson(p.AccountId, p.Id, p.Nickname == "" ? p.FullName : p.Nickname, p.AvatarUrl)).ToDictionaryAsync(p => p.AccountId, ct);
        var files = await db.MessageAttachments.AsNoTracking().Where(f => ids.Contains(f.MessageId)).ToArrayAsync(ct);
        var reactions = await db.MessageReactions.AsNoTracking().Where(r => ids.Contains(r.MessageId)).ToArrayAsync(ct);
        return messages.Select(m => {
            var quote = m.ReplyToMessageId.HasValue ? quoted.GetValueOrDefault(m.ReplyToMessageId.Value) : null;
            return new ChatMessageDto(m.Id, m.ConversationId, people[m.SenderAccountId], m.RecalledAt != null ? null : m.Content, m.Type, m.Number, m.ClientMessageId, m.CreatedAt, m.EditedAt, m.RecalledAt,
                quote == null ? null : new ChatReply(quote.Id, quote.RecalledAt != null ? null : quote.Content, people[quote.SenderAccountId].FullName, quote.RecalledAt != null),
                m.RecalledAt != null ? [] : files.Where(f => f.MessageId == m.Id).Select(f => new ChatAttachment(f.Id, f.FileType, f.FileName, f.FileUrl, f.FileSize, f.MimeType)).ToArray(),
                m.RecalledAt != null ? [] : reactions.Where(r => r.MessageId == m.Id).Select(r => new ChatReaction(r.AccountId, r.ReactionType)).ToArray());
        }).ToArray();
    }

    private async Task Queue(Guid conversationId, string reason, Guid? messageId, CancellationToken ct, Guid? extraAccount = null)
    {
        var accounts = await db.ConversationMembers.Where(m => m.ConversationId == conversationId && m.LeftAt == null).Select(m => m.AccountId).ToListAsync(ct);
        if (extraAccount.HasValue) accounts.Add(extraAccount.Value);
        foreach (var id in accounts.Distinct()) db.Set<NotificationDelivery>().Add(new() { AccountId = id, ConversationId = conversationId, MessageId = messageId, Reason = "chat." + reason });
    }
}
