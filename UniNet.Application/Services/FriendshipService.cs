using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class FriendshipService(UniNetDbContext db)
{
    private async Task<UserProfile> Actor(Guid accountId, CancellationToken ct) => await db.UserProfiles.AsNoTracking()
        .SingleOrDefaultAsync(p => p.AccountId == accountId && p.Account.Status == AccountStatus.Active && p.Account.Role == AccountRole.Student, ct)
        ?? throw new ProjectException("FORBIDDEN", "Chỉ tài khoản sinh viên đang hoạt động được sử dụng chức năng bạn bè.", 403);

    private IQueryable<Friendship> Pair(Guid actor, Guid other) => db.Friendships.Where(f =>
        f.RequesterAccountId == actor && f.ReceiverAccountId == other || f.RequesterAccountId == other && f.ReceiverAccountId == actor);

    private IQueryable<FriendPerson> People(IQueryable<UserProfile> profiles, Guid actor) => profiles.Select(p => new FriendPerson(
        p.AccountId, p.Id, p.FullName, p.Nickname, p.AvatarUrl, (short)p.Account.Role,
        p.AccountId == actor ? "self" : db.Friendships.Any(f => f.Status == FriendshipStatus.Accepted &&
            (f.RequesterAccountId == actor && f.ReceiverAccountId == p.AccountId || f.ReceiverAccountId == actor && f.RequesterAccountId == p.AccountId)) ? "friends" :
        db.Friendships.Any(f => f.Status == FriendshipStatus.Pending && f.RequesterAccountId == actor && f.ReceiverAccountId == p.AccountId) ? "outgoing" :
        db.Friendships.Any(f => f.Status == FriendshipStatus.Pending && f.ReceiverAccountId == actor && f.RequesterAccountId == p.AccountId) ? "incoming" : "none",
        db.Friendships.Where(f => f.RequesterAccountId == actor && f.ReceiverAccountId == p.AccountId || f.ReceiverAccountId == actor && f.RequesterAccountId == p.AccountId).Select(f => (Guid?)f.Id).FirstOrDefault(),
        db.Friendships.Where(f => f.RequesterAccountId == actor && f.ReceiverAccountId == p.AccountId || f.ReceiverAccountId == actor && f.RequesterAccountId == p.AccountId).Select(f => (long?)f.Version).FirstOrDefault(),
        p.AccountId == actor ? 0 : db.UserProfiles.Count(common => common.AccountId != actor && common.AccountId != p.AccountId &&
            common.Account.Status == AccountStatus.Active && common.Account.Role == AccountRole.Student &&
            db.Friendships.Any(f => f.Status == FriendshipStatus.Accepted &&
                (f.RequesterAccountId == actor && f.ReceiverAccountId == common.AccountId || f.ReceiverAccountId == actor && f.RequesterAccountId == common.AccountId)) &&
            db.Friendships.Any(f => f.Status == FriendshipStatus.Accepted &&
                (f.RequesterAccountId == p.AccountId && f.ReceiverAccountId == common.AccountId || f.ReceiverAccountId == p.AccountId && f.RequesterAccountId == common.AccountId)))));

    public async Task<FriendPerson> RelationshipAsync(Guid accountId, Guid profileId, CancellationToken ct)
    {
        await Actor(accountId, ct);
        return await People(db.UserProfiles.AsNoTracking().Where(p => p.Id == profileId && p.Account.Status == AccountStatus.Active && p.Account.Role == AccountRole.Student), accountId)
            .SingleOrDefaultAsync(ct) ?? throw new ProjectException("USER_NOT_FOUND", "Không tìm thấy tài khoản sinh viên đang hoạt động.", 404);
    }

    public async Task<FriendPage> ListAsync(Guid accountId, string kind, int page, int pageSize, string? q, Guid? projectId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        if (page < 1 || pageSize is < 1 or > 50 || page > 100000 || q?.Length > 100)
            throw new ProjectException("INVALID_FRIEND_QUERY", "Trang, số lượng hoặc từ khóa không hợp lệ.");
        var profiles = db.UserProfiles.AsNoTracking().Where(p => p.AccountId != accountId && p.Account.Status == AccountStatus.Active && p.Account.Role == AccountRole.Student);
        if (kind == "friends") profiles = profiles.Where(p => db.Friendships.Any(f => f.Status == FriendshipStatus.Accepted &&
            (f.RequesterAccountId == accountId && f.ReceiverAccountId == p.AccountId || f.ReceiverAccountId == accountId && f.RequesterAccountId == p.AccountId)));
        else if (kind == "received") profiles = profiles.Where(p => db.Friendships.Any(f => f.Status == FriendshipStatus.Pending && f.ReceiverAccountId == accountId && f.RequesterAccountId == p.AccountId));
        else if (kind == "sent") profiles = profiles.Where(p => db.Friendships.Any(f => f.Status == FriendshipStatus.Pending && f.RequesterAccountId == accountId && f.ReceiverAccountId == p.AccountId));
        else if (kind != "people") throw new ProjectException("INVALID_FRIEND_QUERY", "Danh sách không hợp lệ.");
        if (projectId.HasValue)
        {
            var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId && p.CreatorId == actor.Id, ct)
                ?? throw new ProjectException("FORBIDDEN", "Chỉ chủ dự án được tìm người để mời.", 403);
            profiles = profiles.Where(p => p.Account.Role == AccountRole.Student && p.Id != project.CreatorId &&
                !db.ProjectMembers.Any(m => m.ProjectId == project.Id && m.UserId == p.Id && m.Status == ProjectMemberStatus.Active) &&
                !db.ProjectJoinRequests.Any(r => r.ProjectId == project.Id && r.UserId == p.Id && r.RequestType == ProjectRequestType.Invite && r.Status == ProjectJoinRequestStatus.Pending));
        }
        var term = q?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term)) profiles = profiles.Where(p => p.FullName.ToLower().Contains(term) || p.Nickname.ToLower().Contains(term));
        var total = await profiles.CountAsync(ct);
        var ordered = profiles.OrderByDescending(p => db.Friendships.Any(f => f.Status == FriendshipStatus.Accepted &&
            (f.RequesterAccountId == accountId && f.ReceiverAccountId == p.AccountId || f.ReceiverAccountId == accountId && f.RequesterAccountId == p.AccountId)))
            .ThenBy(p => p.FullName).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize);
        return new(await People(ordered, accountId).ToListAsync(ct), page, pageSize, total);
    }

    public async Task<FriendPerson> SendAsync(Guid accountId, Guid receiverAccountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        if (accountId == receiverAccountId) throw new ProjectException("CANNOT_FRIEND_SELF", "Bạn không thể kết bạn với chính mình.");
        var target = await db.UserProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.AccountId == receiverAccountId && p.Account.Status == AccountStatus.Active && p.Account.Role == AccountRole.Student, ct)
            ?? throw new ProjectException("USER_NOT_FOUND", "Không tìm thấy tài khoản sinh viên đang hoạt động.", 404);
        return await Mutate(accountId, receiverAccountId, async () => {
            await Actor(accountId, ct);
            if (!await db.Accounts.AnyAsync(a => a.Id == receiverAccountId && a.Status == AccountStatus.Active && a.Role == AccountRole.Student, ct))
                throw new ProjectException("USER_NOT_FOUND", "Không tìm thấy tài khoản sinh viên đang hoạt động.", 404);
            var row = await Pair(accountId, receiverAccountId).SingleOrDefaultAsync(ct);
            if (row != null) await db.Entry(row).ReloadAsync(ct);
            if (row?.Status is FriendshipStatus.Pending or FriendshipStatus.Accepted)
                throw new ProjectException("FRIENDSHIP_EXISTS", "Đã có quan hệ bạn bè hoặc lời mời đang chờ. Vui lòng kiểm tra lại danh sách.", 409);
            var now = DateTimeOffset.UtcNow;
            if (row == null) { row = new(); db.Friendships.Add(row); }
            else row.Version++;
            row.RequesterAccountId = accountId; row.ReceiverAccountId = receiverAccountId;
            row.Status = FriendshipStatus.Pending; row.CreatedAt = now; row.UpdatedAt = now; row.RespondedAt = null;
            await NotificationService.EnqueueAsync(db, [receiverAccountId], new("friendship.request.received", "community", "Lời mời kết bạn",
                $"{actor.FullName} đã gửi cho bạn lời mời kết bạn.", "Friendship", row.Id,
                EventKey: $"friendship:{row.Id}:{row.Version}:received", SenderUserId: accountId), ct);
            await db.SaveChangesAsync(ct);
            return await RelationshipAsync(accountId, target.Id, ct);
        }, ct);
    }

    public async Task<FriendPerson> DecideAsync(Guid accountId, Guid id, string action, long version, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        var pair = await db.Friendships.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id && (f.RequesterAccountId == accountId || f.ReceiverAccountId == accountId), ct)
            ?? throw new ProjectException("FRIENDSHIP_NOT_FOUND", "Không tìm thấy quan hệ kết bạn.", 404);
        return await Mutate(pair.RequesterAccountId, pair.ReceiverAccountId, async () => {
            await Actor(accountId, ct);
            var row = await db.Friendships.SingleAsync(f => f.Id == id, ct);
            await db.Entry(row).ReloadAsync(ct);
            if (row.Version != version) throw new ProjectException("FRIENDSHIP_CHANGED", "Quan hệ đã thay đổi. Vui lòng tải lại.", 409);
            var isRecipient = row.ReceiverAccountId == accountId;
            if (action is "accept" or "reject" ? !isRecipient : action == "cancel" ? row.RequesterAccountId != accountId : action != "remove")
                throw new ProjectException("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này.", 403);
            if (action == "remove" ? row.Status != FriendshipStatus.Accepted : row.Status != FriendshipStatus.Pending)
                throw new ProjectException("FRIENDSHIP_CHANGED", "Quan hệ không còn ở trạng thái phù hợp.", 409);
            var other = row.RequesterAccountId == accountId ? row.ReceiverAccountId : row.RequesterAccountId;
            if (action == "accept" && !await db.Accounts.AnyAsync(a => a.Id == other && a.Status == AccountStatus.Active && a.Role == AccountRole.Student, ct))
                throw new ProjectException("USER_INACTIVE", "Tài khoản này không còn là sinh viên đang hoạt động.", 409);
            row.Status = action switch { "accept" => FriendshipStatus.Accepted, "reject" => FriendshipStatus.Rejected, "cancel" => FriendshipStatus.Cancelled, _ => FriendshipStatus.Removed };
            row.Version++; row.UpdatedAt = DateTimeOffset.UtcNow; row.RespondedAt = row.UpdatedAt;
            if (action == "accept") await NotificationService.EnqueueAsync(db, [other], new("friendship.accepted", "community", "Đã chấp nhận kết bạn",
                $"{actor.FullName} đã chấp nhận lời mời kết bạn của bạn.", "Friendship", row.Id,
                EventKey: $"friendship:{row.Id}:{row.Version}:accepted", SenderUserId: accountId), ct);
            await db.SaveChangesAsync(ct);
            return await People(db.UserProfiles.AsNoTracking().Where(p => p.AccountId == other), accountId).SingleAsync(ct);
        }, ct);
    }

    private async Task<T> Mutate<T>(Guid a, Guid b, Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (db.Database.IsNpgsql()) {
                var pair = string.CompareOrdinal(a.ToString("N"), b.ToString("N")) < 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
                var key = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes("friendship:" + pair)));
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            }
            var result = await action(); await transaction.CommitAsync(ct); return result;
        }
        catch (DbUpdateConcurrencyException) { throw new ProjectException("FRIENDSHIP_CHANGED", "Quan hệ vừa thay đổi. Vui lòng tải lại.", 409); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        { throw new ProjectException("FRIENDSHIP_EXISTS", "Đã có quan hệ cho hai tài khoản này. Vui lòng tải lại.", 409); }
    }
}
