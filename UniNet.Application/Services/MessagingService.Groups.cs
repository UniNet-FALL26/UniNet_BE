using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Messaging;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed partial class MessagingService
{
    private static string? Name(string? value) { Require(value?.Trim().Length <= 150 || value == null, "CHAT_NAME_INVALID", "Tên nhóm tối đa 150 ký tự."); return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    private static string? Avatar(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        Require(value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo), "CHAT_AVATAR_INVALID", "Ảnh nhóm phải là liên kết HTTPS hợp lệ.");
        return value;
    }
    private async Task<(Conversation Conversation, ConversationMember Member)> Admin(Guid actor, Guid id, CancellationToken ct)
    {
        var member = await Member(actor, id, ct); var conversation = await db.Conversations.SingleAsync(c => c.Id == id, ct);
        Require(conversation.Type == 1 && member.Role >= 1, "CHAT_ADMIN_REQUIRED", "Chỉ chủ nhóm hoặc quản trị viên được thực hiện thao tác này.", 403);
        return (conversation, member);
    }
    private async Task<Project> ManageProject(Guid actor, Guid projectId, CancellationToken ct) => await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && db.UserProfiles.Any(u => u.Id == p.CreatorId && u.AccountId == actor), ct)
        ?? throw Error("PROJECT_FORBIDDEN", "Bạn không có quyền quản lý dự án này.", 403);
    private async Task<bool> ProjectParticipant(Guid accountId, Project project, CancellationToken ct) => await db.UserProfiles.AnyAsync(p => p.AccountId == accountId &&
        (p.Id == project.CreatorId || db.ProjectMembers.Any(m => m.ProjectId == project.Id && m.UserId == p.Id && m.Status == ProjectMemberStatus.Active)), ct);
    private async Task TargetAccount(Guid id, CancellationToken ct) => Require(await db.Accounts.AnyAsync(a => a.Id == id && a.Status == AccountStatus.Active, ct), "CHAT_USER_NOT_FOUND", "Không tìm thấy người dùng đang hoạt động.", 404);
    public async Task<ChatPerson[]> People(Guid actor, string? query, CancellationToken ct)
    {
        await Active(actor, ct); var q = query?.Trim() ?? ""; Require(q.Length <= 100, "INVALID_SEARCH", "Từ khóa quá dài.");
        if (q.Length == 0) return [];
        return await db.UserProfiles.AsNoTracking().Where(p => p.AccountId != actor && p.Account.Status == AccountStatus.Active &&
            (p.FullName.Contains(q) || p.Nickname.Contains(q))).OrderBy(p => p.FullName).Take(20)
            .Select(p => new ChatPerson(p.AccountId, p.Id, p.Nickname == "" ? p.FullName : p.Nickname, p.AvatarUrl)).ToArrayAsync(ct);
    }
    public async Task<ChatConversation> CreateGroup(Guid actor, ChatGroupRequest request, CancellationToken ct)
    {
        await Active(actor, ct);
        Require((request.AccountIds?.Length ?? 0) <= 100 && (request.MemberProfileIds?.Length ?? 0) <= 100, "CHAT_GROUP_SIZE", "Nhóm tối đa 100 thành viên.");
        var name = Name(request.Name); var avatar = Avatar(request.AvatarUrl);
        var id = await Transaction(request.ProjectId.HasValue ? $"chat:project:{request.ProjectId:N}" : $"chat:groups:{actor:N}", async () => {
            Project? project = null;
            var accounts = (request.AccountIds ?? []).Append(actor).Distinct().ToList();
            if (request.ProjectId.HasValue) {
                project = await ManageProject(actor, request.ProjectId.Value, ct);
                var existing = await db.Conversations.SingleOrDefaultAsync(c => c.ProjectId == project.Id, ct);
                Require(existing == null, "PROJECT_CHAT_EXISTS", "Dự án đã có phòng trò chuyện. Hãy mở phòng đã liên kết.", 409);
                var profileIds = request.MemberProfileIds ?? [];
                accounts.AddRange(await db.UserProfiles.Where(p => profileIds.Contains(p.Id)).Select(p => p.AccountId).ToListAsync(ct));
                Require(await db.UserProfiles.CountAsync(p => profileIds.Contains(p.Id), ct) == profileIds.Distinct().Count(), "CHAT_MEMBER_INVALID", "Thành viên dự án không hợp lệ.");
            }
            accounts = accounts.Distinct().ToList();
            Require(accounts.Count >= (project == null ? 3 : 2) && accounts.Count <= 100, "CHAT_GROUP_SIZE", project == null ? "Chọn ít nhất hai người bạn để tạo nhóm." : "Nhóm dự án cần ít nhất hai thành viên.");
            foreach (var accountId in accounts.Where(a => a != actor)) {
                await TargetAccount(accountId, ct);
                Require(!await Blocked(actor, accountId, ct), "CHAT_BLOCKED", "Không thể thêm tài khoản có quan hệ chặn.", 403);
                Require(project != null ? await ProjectParticipant(accountId, project, ct) : await Friends(actor, accountId, ct), "CHAT_MEMBER_FORBIDDEN", project != null ? "Chỉ chọn thành viên đang tham gia dự án." : "Chỉ được thêm bạn bè vào nhóm mới.", 403);
            }
            var group = new Conversation { Type = 1, Name = name ?? project?.Title ?? "Nhóm trò chuyện", AvatarUrl = avatar ?? project?.LogoUrl, ProjectId = project?.Id, CreatedByAccountId = actor };
            db.Conversations.Add(group);
            foreach (var accountId in accounts) db.ConversationMembers.Add(new() { ConversationId = group.Id, AccountId = accountId, Role = (short)(accountId == actor ? 2 : 0) });
            if (project != null) project.ChatRoomId = group.Id;
            await db.SaveChangesAsync(ct); await Queue(group.Id, "group", null, ct); return group.Id;
        }, ct);
        return await Get(actor, id, ct);
    }
    public async Task<ChatConversation> UpdateGroup(Guid actor, Guid id, ChatGroupUpdate request, CancellationToken ct)
    {
        var name = Name(request.Name); var avatar = Avatar(request.AvatarUrl);
        await Transaction($"chat:conversation:{id:N}", async () => {
            var (group, _) = await Admin(actor, id, ct); group.Name = name ?? "Nhóm trò chuyện"; group.AvatarUrl = avatar;
            await Queue(id, "group", null, ct); return true;
        }, ct);
        return await Get(actor, id, ct);
    }
    public async Task AddMembers(Guid actor, Guid id, ChatMemberRequest request, CancellationToken ct)
    {
        Require((request.AccountIds?.Length ?? 0) <= 50 && (request.ProfileIds?.Length ?? 0) <= 50, "CHAT_MEMBER_INVALID", "Chọn tối đa 50 người để thêm.");
        await Transaction($"chat:conversation:{id:N}", async () => {
            var (group, _) = await Admin(actor, id, ct);
            var accounts = (request.AccountIds ?? []).ToList();
            if (request.ProfileIds is { Length: > 0 }) {
                var resolved = await db.UserProfiles.Where(p => request.ProfileIds.Contains(p.Id)).Select(p => p.AccountId).ToListAsync(ct);
                Require(resolved.Count == request.ProfileIds.Distinct().Count(), "CHAT_MEMBER_INVALID", "Có thành viên không hợp lệ."); accounts.AddRange(resolved);
            }
            accounts = accounts.Distinct().ToList(); Require(accounts.Count is > 0 and <= 50, "CHAT_MEMBER_INVALID", "Chọn từ 1 đến 50 người để thêm.");
            var present = await db.ConversationMembers.Where(m => m.ConversationId == id && m.LeftAt == null).Select(m => m.AccountId).ToListAsync(ct);
            accounts = accounts.Except(present).ToList();
            var project = group.ProjectId.HasValue ? await db.Projects.SingleOrDefaultAsync(p => p.Id == group.ProjectId, ct) : null;
            Require(present.Count + accounts.Count <= 100, "CHAT_GROUP_SIZE", "Nhóm tối đa 100 thành viên.");
            foreach (var accountId in accounts) {
                await TargetAccount(accountId, ct);
                Require(!await Blocked(actor, accountId, ct), "CHAT_BLOCKED", "Không thể thêm tài khoản có quan hệ chặn.", 403);
                Require(await Friends(actor, accountId, ct) || project != null && await ProjectParticipant(accountId, project, ct), "CHAT_MEMBER_FORBIDDEN", "Chỉ được thêm bạn bè hoặc thành viên dự án liên kết.", 403);
                var member = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.AccountId == accountId, ct);
                if (member?.LeftAt == null && member != null) continue;
                if (member == null) db.ConversationMembers.Add(new() { ConversationId = id, AccountId = accountId });
                else { member.LeftAt = null; member.JoinedAt = DateTimeOffset.UtcNow; member.Role = 0; }
            }
            await db.SaveChangesAsync(ct); await Queue(id, "members", null, ct); return true;
        }, ct);
    }
    public async Task RemoveMember(Guid actor, Guid id, Guid accountId, CancellationToken ct)
    {
        await Transaction($"chat:conversation:{id:N}", async () => {
            var own = await Member(actor, id, ct); var group = await db.Conversations.SingleAsync(c => c.Id == id, ct);
            Require(group.Type == 1, "CHAT_GROUP_REQUIRED", "Không thể rời cuộc trò chuyện cá nhân.");
            var target = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.AccountId == accountId && m.LeftAt == null, ct) ?? throw Error("CHAT_MEMBER_NOT_FOUND", "Người này không còn trong nhóm.", 404);
            Require(target.Role != 2, "CHAT_OWNER_TRANSFER_REQUIRED", "Chuyển quyền chủ nhóm trước khi rời nhóm.", 409);
            Require(actor == accountId || own.Role == 2 || own.Role == 1 && target.Role == 0, "CHAT_FORBIDDEN", "Bạn không có quyền xóa thành viên này.", 403);
            target.LeftAt = DateTimeOffset.UtcNow;
            var sessions = await db.ChatDeviceSessions.Where(s => s.AccountId == accountId && s.ActiveConversationId == id).ToListAsync(ct);
            foreach (var session in sessions) session.ActiveConversationId = null;
            await db.SaveChangesAsync(ct); await Queue(id, "members", null, ct, accountId); return true;
        }, ct);
    }
    public async Task Role(Guid actor, Guid id, Guid accountId, short role, CancellationToken ct)
    {
        Require(role is >= 0 and <= 2, "CHAT_ROLE_INVALID", "Vai trò không hợp lệ.");
        await Transaction($"chat:conversation:{id:N}", async () => {
            var (_, own) = await Admin(actor, id, ct);
            Require(own.Role == 2 && actor != accountId, "CHAT_OWNER_REQUIRED", "Chỉ chủ nhóm được đổi quyền người khác.", 403);
            var target = await db.ConversationMembers.SingleOrDefaultAsync(m => m.ConversationId == id && m.AccountId == accountId && m.LeftAt == null, ct) ?? throw Error("CHAT_MEMBER_NOT_FOUND", "Thành viên không còn trong nhóm.", 404);
            if (role == 2) { own.Role = 1; await db.SaveChangesAsync(ct); }
            target.Role = role; await db.SaveChangesAsync(ct); await Queue(id, "members", null, ct); return true;
        }, ct);
    }
    public async Task Settings(Guid actor, Guid id, ChatMemberSettings request, CancellationToken ct)
    {
        await Transaction($"chat:conversation:{id:N}", async () => {
            var own = await Member(actor, id, ct); own.IsPinned = request.IsPinned; own.MutedUntil = request.MutedUntil?.ToUniversalTime();
            await Queue(id, "settings", null, ct); return true;
        }, ct);
    }
    public async Task<ChatProject?> LinkProject(Guid actor, Guid id, Guid? projectId, CancellationToken ct)
    {
        await Transaction($"chat:conversation:{id:N}", async () => {
            var (group, _) = await Admin(actor, id, ct);
            if (group.ProjectId == projectId) return true;
            foreach (var key in new[] { group.ProjectId, projectId }.Where(p => p.HasValue).Select(p => p!.Value).Distinct().Order()) await Lock(db, $"chat:project:{key:N}", ct);
            if (group.ProjectId.HasValue) { var old = await ManageProject(actor, group.ProjectId.Value, ct); old.ChatRoomId = null; }
            if (projectId.HasValue) {
                var project = await ManageProject(actor, projectId.Value, ct);
                Require(!await db.Conversations.AnyAsync(c => c.ProjectId == project.Id && c.Id != id, ct), "PROJECT_CHAT_EXISTS", "Dự án đã có nhóm trò chuyện khác. Hãy mở nhóm đã liên kết.", 409);
                project.ChatRoomId = id;
            }
            group.ProjectId = projectId; await Queue(id, "project", null, ct); return true;
        }, ct);
        return (await Get(actor, id, ct)).Project;
    }
    public async Task<object> ProjectChat(Guid actor, Guid projectId, CancellationToken ct)
    {
        await Active(actor, ct); var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct) ?? throw Error("PROJECT_NOT_FOUND", "Không tìm thấy dự án.", 404);
        Require(await ProjectParticipant(actor, project, ct), "PROJECT_FORBIDDEN", "Bạn không tham gia dự án này.", 403);
        var id = await db.Conversations.Where(c => c.ProjectId == projectId).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
        var accessible = id.HasValue && await db.ConversationMembers.AnyAsync(m => m.ConversationId == id && m.AccountId == actor && m.LeftAt == null, ct);
        return new { ConversationId = id, CanAccess = accessible };
    }
    public async Task Block(Guid actor, Guid other, bool block, CancellationToken ct)
    {
        await Active(actor, ct); Require(actor != other, "CHAT_SELF", "Không thể chặn chính mình.");
        await TargetAccount(other, ct);
        await Transaction($"chat:block:{Pair(actor, other)}", async () => {
            var current = await db.AccountBlocks.SingleOrDefaultAsync(b => b.AccountId == actor && b.BlockedAccountId == other, ct);
            if (block && current == null) db.AccountBlocks.Add(new() { AccountId = actor, BlockedAccountId = other });
            else if (!block && current != null) db.AccountBlocks.Remove(current);
            var pair = Pair(actor, other); var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Type == 0 && c.DirectAccountLowId == pair.Low && c.DirectAccountHighId == pair.High, ct);
            if (conversation != null) await Queue(conversation.Id, "blocked", null, ct); return true;
        }, ct);
    }
    public async Task<ChatAttachment[]> SharedFiles(Guid actor, Guid id, int page, CancellationToken ct)
    {
        await Member(actor, id, ct); Require(page is >= 1 and <= 10000, "INVALID_PAGE", "Trang không hợp lệ.");
        return await (from file in db.MessageAttachments join message in db.Messages on file.MessageId equals message.Id
            where message.ConversationId == id && message.RecalledAt == null orderby message.Number descending, file.Id
            select new ChatAttachment(file.Id, file.FileType, file.FileName, file.FileUrl, file.FileSize, file.MimeType)).Skip((page - 1) * 30).Take(30).ToArrayAsync(ct);
    }
}
