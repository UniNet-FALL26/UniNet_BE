using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed partial class ProjectHubService
{
    private async Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await action(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return result;
        }
        catch (Exception ex) when (DatabaseWriteConflicts.IsWriteConflict(ex))
        { throw new ProjectException("PROJECT_CONFLICT", "Dự án vừa thay đổi. Hãy tải lại rồi thử lại.", 409); }
    }

    private async Task<Project> Locked(Guid id, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Projects\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        var p = await Project(id, ct);
        await db.Entry(p).ReloadAsync(ct);
        if (p.Recruitment != null) await db.Entry(p.Recruitment).ReloadAsync(ct);
        return p;
    }

    public async Task<HubProject> RecruitmentAsync(Guid id, Guid accountId, SaveHubRecruitmentRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        var positions = ValidPositions(request.Positions);
        Require(request.Status is >= 0 and <= 2, "INVALID_RECRUITMENT", "Trạng thái tuyển thành viên không hợp lệ.");
        var description = Text(request.Description, 5000); var now = DateTimeOffset.UtcNow;
        Require(request.Status != 1 || request.Deadline == null || request.Deadline > now,
            "INVALID_RECRUITMENT_DEADLINE", "Hạn tuyển thành viên phải ở tương lai.");
        await Transaction(async () => {
            var p = await Locked(id, ct); Own(p, actor);
            Require(request.Status != 1 || p.Status is ProjectStatus.Pending or ProjectStatus.Active,
                "PROJECT_COMPLETED", "Chỉ dự án chưa bắt đầu hoặc đang thực hiện mới được mở tuyển.", 409);
            var stableIds = positions.Select(x => x.Id).ToArray();
            var referencedIds = await db.ProjectMembers.Where(m => m.ProjectId == id && m.PositionId != null &&
                m.Status == ProjectMemberStatus.Active).Select(m => m.PositionId!).ToListAsync(ct);
            Require(referencedIds.All(stableIds.Contains), "POSITION_IN_USE", "Không thể xóa vị trí còn thành viên đang tham gia.", 409);
            foreach (var position in positions)
                Require(await db.ProjectMembers.CountAsync(m => m.ProjectId == id && m.PositionId == position.Id &&
                    m.Status == ProjectMemberStatus.Active, ct) <= position.Quantity, "POSITION_IN_USE",
                    "Số lượng tuyển không thể ít hơn số thành viên đang giữ vị trí.", 409);
            var recruitment = p.Recruitment;
            if (recruitment == null)
            {
                recruitment = new() { ProjectId = p.Id };
                p.Recruitment = recruitment;
                db.ProjectRecruitments.Add(recruitment);
            }
            recruitment.Description = description; recruitment.Positions = JsonSerializer.Serialize(positions, Json);
            recruitment.Deadline = request.Deadline?.ToUniversalTime(); recruitment.Status = (ProjectRecruitmentStatus)request.Status;
            if (request.Status == 1) { recruitment.PublishedAt ??= now; p.Visibility = ProjectVisibility.Public; }
            recruitment.UpdatedAt = now; p.UpdatedAt = now;
            if (request.Status != 1) await ExpirePending(p.Id, now, ct, applicationsOnly: true);
            // Keep legacy readers coherent during their remaining transition.
            p.MemberTarget = 1 + positions.Sum(x => x.Quantity); p.MinimumQuantity = 1;
            p.RecruitmentDeadline = recruitment.Deadline;
            p.RecruitmentStatus = request.Status == 1 ? RecruitmentStatus.Open : RecruitmentStatus.Closed;
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await GetAsync(id, accountId, ct);
    }

    private static HubPosition[] ValidPositions(HubPosition[]? positions)
    {
        Require(positions != null && positions.Length is > 0 and <= 50, "INVALID_POSITIONS", "Vui lòng khai báo từ 1 đến 50 vị trí tuyển.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return positions.Select(position => {
            Require(position != null, "INVALID_POSITIONS", "Vị trí tuyển không hợp lệ.");
            var id = Text(position.Id, 100, true)!;
            Require(ids.Add(id), "DUPLICATE_POSITION", "Mỗi vị trí phải có mã định danh riêng.");
            Require(position.Quantity is > 0 and <= 1000, "INVALID_POSITIONS", "Số lượng mỗi vị trí phải từ 1 đến 1000.");
            return position with { Id = id, Name = Text(position.Name, 150, true)!,
                JobDescription = Text(position.JobDescription, 5000) ?? "", Technologies = Strings(position.Technologies),
                Skills = Strings(position.Skills), Requirements = Strings(position.Requirements, 40, 1000), Benefits = Strings(position.Benefits, 40, 1000) };
        }).ToArray();
    }

    private static void Recruiting(Project p)
    {
        Require(p.Status is ProjectStatus.Pending or ProjectStatus.Active &&
            p.Recruitment is { Status: ProjectRecruitmentStatus.Open } &&
            (p.Recruitment.Deadline == null || p.Recruitment.Deadline > DateTimeOffset.UtcNow),
            "RECRUITMENT_CLOSED", "Dự án hiện không mở tuyển thành viên.", 409);
    }

    private static HubPosition Position(Project p, string? positionId)
    {
        var id = Text(positionId, 100, true);
        return Positions(p.Recruitment?.Positions).SingleOrDefault(x => x.Id == id)
            ?? throw new ProjectException("POSITION_NOT_FOUND", "Không tìm thấy vị trí tuyển.", 404);
    }

    private async Task Capacity(Guid projectId, HubPosition position, CancellationToken ct)
    {
        var normalizedName = position.Name.Trim().ToLowerInvariant();
        var count = await db.ProjectMembers.CountAsync(m => m.ProjectId == projectId && m.Status == ProjectMemberStatus.Active &&
            m.UserId != m.Project.CreatorId &&
            (m.PositionId == position.Id || (m.PositionId == null && m.Role.Trim().ToLower() == normalizedName)), ct);
        Require(count < position.Quantity, "POSITION_FULL", "Vị trí này đã đủ thành viên.", 409);
    }

    public async Task<HubRequest> RequestAsync(Guid id, Guid accountId, CreateHubRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        Require(request.RequestType is 0 or 1, "INVALID_REQUEST_TYPE", "Loại yêu cầu không hợp lệ.");
        var message = Text(request.Message, 4000);
        return await Transaction(async () => {
            var p = await Locked(id, ct);
            HubPosition? position = null;
            string role;
            var recipient = actor;
            if (request.RequestType == 1)
            {
                Own(p, actor);
                recipient = await db.UserProfiles.Include(u => u.Account).SingleOrDefaultAsync(u => u.Id == request.InvitedUserId &&
                    u.Account.Status == AccountStatus.Active && u.Account.Role == AccountRole.Student, ct)
                    ?? throw new ProjectException("USER_NOT_FOUND", "Không tìm thấy sinh viên đang hoạt động.", 404);
            }
            else
            {
                Recruiting(p);
                position = Position(p, request.PositionId); await Capacity(p.Id, position, ct);
            }
            if (request.RequestType == 1)
            {
                Require(AllowsFriendInvitations(p),
                    "PROJECT_COMPLETED", "Dự án hiện không nhận thêm thành viên.", 409);
                Require(await db.Friendships.AnyAsync(f => f.Status == FriendshipStatus.Accepted &&
                    ((f.RequesterAccountId == accountId && f.ReceiverAccountId == recipient.AccountId) ||
                     (f.ReceiverAccountId == accountId && f.RequesterAccountId == recipient.AccountId)), ct),
                    "FRIENDSHIP_REQUIRED", "Chỉ có thể mời sinh viên trong danh sách bạn bè.", 409);
                // Accept the old position-only payload, but direct invitations do not reserve recruitment slots.
                role = Text(request.Role, 150) ?? Position(p, request.PositionId).Name;
            }
            else role = position!.Name;
            Require(recipient.Id != p.CreatorId, "CANNOT_JOIN_OWN_PROJECT", "Chủ dự án không thể tự ứng tuyển hoặc tự mời mình.", 409);
            Require(!await db.ProjectMembers.AnyAsync(m => m.ProjectId == id && m.UserId == recipient.Id && m.Status == ProjectMemberStatus.Active, ct),
                "ALREADY_PROJECT_MEMBER", "Người dùng đã là thành viên dự án.", 409);
            Require(!await db.ProjectJoinRequests.AnyAsync(r => r.ProjectId == id && r.UserId == recipient.Id &&
                r.RequestType == (ProjectRequestType)request.RequestType && r.Status == ProjectJoinRequestStatus.Pending, ct),
                "REQUEST_EXISTS", "Đã có một yêu cầu đang chờ xử lý cho người dùng này.", 409);
            var now = DateTimeOffset.UtcNow;
            var r = new ProjectJoinRequest { Id = Guid.NewGuid(), ProjectId = id, UserId = recipient.Id,
                RequestType = (ProjectRequestType)request.RequestType, InviterId = request.RequestType == 1 ? actor.Id : null,
                PositionId = position?.Id, Role = role, Message = message, Status = ProjectJoinRequestStatus.Pending,
                CreatedAt = now, UpdatedAt = now };
            db.ProjectJoinRequests.Add(r); await db.SaveChangesAsync(ct);
            var noticeRecipient = request.RequestType == 1 ? recipient.AccountId : p.Creator.AccountId;
            await NotificationService.EnqueueAsync(db, [noticeRecipient],
                new(request.RequestType == 1 ? "project.invitation.received" : "project.join-request.received", "Project",
                    request.RequestType == 1 ? "Lời mời tham gia dự án" : "Yêu cầu tham gia dự án",
                    request.RequestType == 1 ? $"Bạn được mời tham gia dự án {p.Title}." : $"{actor.FullName} muốn tham gia dự án {p.Title}.",
                    request.RequestType == 1 ? "ProjectInvitation" : "Project", request.RequestType == 1 ? r.Id : p.Id,
                    EventKey: $"project-hub-request:{r.Id}:created", SenderUserId: accountId), ct);
            return MapRequest(r, p, recipient);
        }, ct);
    }

    public async Task<HubItems<HubRequest>> RequestsAsync(Guid id, Guid accountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct); var p = await Project(id, ct); Own(p, actor);
        return await RequestPage(db.ProjectJoinRequests.Where(r => r.ProjectId == id), ct);
    }

    public async Task<HubItems<HubRequest>> MyRequestsAsync(Guid accountId, CancellationToken ct, Guid? projectId = null)
    {
        var actor = await Actor(accountId, ct);
        var query = db.ProjectJoinRequests.Where(r => r.UserId == actor.Id || r.InviterId == actor.Id);
        if (projectId.HasValue) query = query.Where(r => r.ProjectId == projectId.Value);
        return await RequestPage(query, ct);
    }

    private async Task<HubItems<HubRequest>> RequestPage(IQueryable<ProjectJoinRequest> query, CancellationToken ct)
    {
        // Bounded management lists; no details/private project content is exposed here.
        var requests = await query.AsNoTracking().Include(r => r.Project).Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Take(200).ToArrayAsync(ct);
        return new(requests.Select(r => MapRequest(r, r.Project, r.User)).ToArray());
    }

    private static HubRequest MapRequest(ProjectJoinRequest r, Project p, UniNet.Domain.UserProfile user) =>
        new(r.Id, r.ProjectId, p.Title, r.UserId, user.FullName, user.AvatarUrl, (short)r.RequestType, r.InviterId,
            r.PositionId, r.Role, r.Message, (short)r.Status, r.CreatedAt, r.ReviewedAt);

    private async Task ExpirePending(Guid projectId, DateTimeOffset now, CancellationToken ct, bool applicationsOnly = false)
    {
        var pending = await db.ProjectJoinRequests.Where(r => r.ProjectId == projectId && r.Status == ProjectJoinRequestStatus.Pending &&
            (!applicationsOnly || r.RequestType == ProjectRequestType.Apply)).ToListAsync(ct);
        foreach (var r in pending) { r.Status = ProjectJoinRequestStatus.Expired; r.UpdatedAt = now; }
    }

    public async Task<HubRequest> DecideAsync(Guid requestId, Guid accountId, HubDecisionRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        var decision = Text(request.Decision, 20, true)!.ToLowerInvariant();
        Require(decision is "accept" or "reject" or "cancel", "INVALID_REQUEST_DECISION", "Quyết định không hợp lệ.");
        var projectId = await db.ProjectJoinRequests.Where(r => r.Id == requestId).Select(r => (Guid?)r.ProjectId).SingleOrDefaultAsync(ct)
            ?? throw new ProjectException("REQUEST_NOT_FOUND", "Không tìm thấy yêu cầu.", 404);
        return await Transaction(async () => {
            var p = await Locked(projectId, ct);
            var r = await db.ProjectJoinRequests.Include(r => r.User).ThenInclude(u => u.Account).SingleAsync(r => r.Id == requestId, ct);
            await db.Entry(r).ReloadAsync(ct);
            var permitted = decision == "cancel" ? r.RequestType == ProjectRequestType.Apply ? r.UserId == actor.Id : r.InviterId == actor.Id
                : r.RequestType == ProjectRequestType.Apply ? p.CreatorId == actor.Id : r.UserId == actor.Id;
            Require(permitted, "FORBIDDEN", "Bạn không có quyền xử lý yêu cầu này.", 403);
            Require(r.Status == ProjectJoinRequestStatus.Pending, "REQUEST_ALREADY_REVIEWED", "Yêu cầu đã được xử lý trước đó.", 409);
            var now = DateTimeOffset.UtcNow;
            if (decision == "accept")
            {
                Require(r.RequestType == ProjectRequestType.Invite ? AllowsFriendInvitations(p) : p.Status is ProjectStatus.Pending or ProjectStatus.Active,
                    "PROJECT_COMPLETED", "Dự án hiện không nhận thêm thành viên.", 409);
                Require(r.User.Account.Status == AccountStatus.Active && r.User.Account.Role == AccountRole.Student,
                    "USER_INACTIVE", "Người nhận không còn tài khoản sinh viên đang hoạt động.", 409);
                HubPosition? position = null;
                if (r.RequestType == ProjectRequestType.Apply)
                {
                    Recruiting(p);
                    position = Position(p, r.PositionId); await Capacity(p.Id, position, ct);
                }
                var member = await db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == p.Id && m.UserId == r.UserId, ct);
                Require(member?.Status != ProjectMemberStatus.Active, "ALREADY_PROJECT_MEMBER", "Người dùng đã là thành viên dự án.", 409);
                if (member == null)
                {
                    member = new() { Id = Guid.NewGuid(), ProjectId = p.Id, UserId = r.UserId, CreatedAt = now };
                    db.ProjectMembers.Add(member);
                }
                member.Role = position?.Name ?? r.Role; member.PositionId = position?.Id; member.JoinRequestId = r.Id;
                member.Status = ProjectMemberStatus.Active; member.JoinedAt = now; member.LeftAt = null; member.UpdatedAt = now;
                var competing = await db.ProjectJoinRequests.Where(other => other.ProjectId == p.Id && other.UserId == r.UserId &&
                    other.Id != r.Id && other.Status == ProjectJoinRequestStatus.Pending).ToListAsync(ct);
                foreach (var other in competing) { other.Status = ProjectJoinRequestStatus.Cancelled; other.UpdatedAt = now; }
            }
            r.Status = decision switch { "accept" => ProjectJoinRequestStatus.Accepted,
                "reject" => ProjectJoinRequestStatus.Rejected, _ => ProjectJoinRequestStatus.Cancelled };
            r.ReviewedBy = actor.Id; r.ReviewedAt = now; r.UpdatedAt = now; p.UpdatedAt = now;
            var recipientId = actor.Id == r.UserId ? p.Creator.AccountId : r.User.AccountId;
            var outcome = decision switch { "accept" => "đã được chấp nhận", "reject" => "đã bị từ chối", _ => "đã được hủy" };
            await NotificationService.EnqueueAsync(db, [recipientId],
                new("project.join-request.reviewed", "Project", "Cập nhật yêu cầu tham gia dự án",
                    $"Yêu cầu tham gia dự án {p.Title} {outcome}.", "Project", p.Id,
                    EventKey: $"project-hub-request:{r.Id}:{decision}", SenderUserId: accountId), ct);
            await db.SaveChangesAsync(ct); return MapRequest(r, p, r.User);
        }, ct);
    }

    public async Task<HubProject> MemberAsync(Guid id, Guid userId, Guid accountId, UpdateHubMemberRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        await Transaction(async () => {
            var p = await Locked(id, ct);
            Require(request.Status is null or >= 0 and <= 2, "INVALID_MEMBER_STATUS", "Trạng thái thành viên không hợp lệ.");
            Require(request.Status is null or (short)ProjectMemberStatus.Active,
                "PROJECT_HUB_MEMBER_REMOVAL_DISABLED", "Project Hub không cho phép rời dự án hoặc loại thành viên.", 409);
            Own(p, actor);
            var member = await db.ProjectMembers.SingleOrDefaultAsync(m => m.ProjectId == id && m.UserId == userId, ct)
                ?? throw new ProjectException("MEMBER_NOT_FOUND", "Không tìm thấy thành viên.", 404);
            Require(!request.Status.HasValue || member.Status == ProjectMemberStatus.Active,
                "MEMBER_REQUIRES_INVITATION", "Thành viên đã rời dự án cần nhận lời mời mới để tham gia lại.", 409);
            if (request.Role != null) member.Role = Text(request.Role, 150, true)!;
            if (request.PositionId != null)
            {
                var position = Position(p, request.PositionId);
                if (position.Id != member.PositionId && member.Status == ProjectMemberStatus.Active) await Capacity(id, position, ct);
                member.PositionId = position.Id; member.Role = position.Name;
            }
            member.UpdatedAt = DateTimeOffset.UtcNow; p.UpdatedAt = member.UpdatedAt;
            await db.SaveChangesAsync(ct); return true;
        }, ct);
        return await Response(await Project(id, ct), actor.Id, ct);
    }
}
