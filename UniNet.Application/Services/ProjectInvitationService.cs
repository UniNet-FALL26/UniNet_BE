using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectInvitationService(UniNetDbContext db) : IProjectInvitationService
{
    public Task<ProjectInvitationResponse> SendAsync(Guid projectId, Guid accountId,
        CreateProjectInvitationRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        var inviter = await GetStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireCreator(project, inviter.Id);
        Require(!string.IsNullOrWhiteSpace(request.Role) && request.Role.Trim().Length <= 150,
            "INVALID_ROLE", "Role must be between 1 and 150 characters.");
        var invitee = await db.UserProfiles.Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.Id == request.InviteeId, ct)
            ?? throw new ProjectInvitationException("INVITEE_NOT_FOUND", "Invitee profile not found.", 404);
        RequireActiveStudent(invitee);
        Require(invitee.Id != project.CreatorId, "CANNOT_INVITE_SELF", "You cannot invite yourself to your own project.", 409);
        Require(ProjectHubService.AllowsFriendInvitations(project),
            "INVALID_PROJECT_STATE", "Project cannot receive friend invitations.", 409);
        Require(await db.Friendships.AnyAsync(f => f.Status == FriendshipStatus.Accepted &&
            ((f.RequesterAccountId == accountId && f.ReceiverAccountId == invitee.AccountId) ||
             (f.ReceiverAccountId == accountId && f.RequesterAccountId == invitee.AccountId)), ct),
            "FRIENDSHIP_REQUIRED", "Only accepted student friends can be invited.", 409);
        Require(!await ProjectMembership.IsEffectiveMemberAsync(db, projectId, invitee.Id, ct),
            "ALREADY_PROJECT_MEMBER", "The invitee is already a member of this project.", 409);
        Require(!await db.ProjectJoinRequests.AnyAsync(i => i.RequestType == ProjectRequestType.Invite && i.ProjectId == projectId && i.UserId == invitee.Id &&
            i.Status == ProjectJoinRequestStatus.Pending, ct), "INVITATION_EXISTS", "The invitee already has a pending invitation to this project.", 409);

        var now = DateTimeOffset.UtcNow;
        var invitation = new ProjectJoinRequest
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Project = project,
            RequestType = ProjectRequestType.Invite, InviterId = inviter.Id, Inviter = inviter, UserId = invitee.Id, User = invitee,
            Role = request.Role.Trim(), Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
            Status = ProjectJoinRequestStatus.Pending, CreatedAt = now, UpdatedAt = now
        };
        db.ProjectJoinRequests.Add(invitation);
        await NotificationService.EnqueueAsync(db, [invitee.AccountId],
            new("project.invitation.received", "Project", "Lời mời tham gia dự án",
                $"{inviter.FullName} mời bạn tham gia dự án {project.Title} với vai trò {invitation.Role}.",
                "ProjectInvitation", invitation.Id, EventKey: $"project-invitation:{invitation.Id}:sent", SenderUserId: accountId), ct);
        return invitation;
    }, ct);

    public async Task<ProjectInvitationsResponse> GetProjectInvitationsAsync(Guid projectId, Guid accountId,
        ProjectInvitationStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var creator = await GetStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireCreator(project, creator.Id);
        return await ListAsync(Invitations().Where(i => i.ProjectId == projectId), status, page, pageSize, ct);
    }

    public async Task<ProjectInvitationsResponse> GetMyInvitationsAsync(Guid accountId,
        ProjectInvitationStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var invitee = await GetStudentAsync(accountId, ct);
        return await ListAsync(Invitations().Where(i => i.UserId == invitee.Id), status, page, pageSize, ct);
    }

    public Task<ProjectInvitationResponse> CancelAsync(Guid invitationId, Guid accountId, CancellationToken ct = default)
        => DecideAsync(invitationId, accountId, ProjectInvitationStatus.Cancelled, ct);

    public Task<ProjectInvitationResponse> DeclineAsync(Guid invitationId, Guid accountId, CancellationToken ct = default)
        => DecideAsync(invitationId, accountId, ProjectInvitationStatus.Declined, ct);

    public Task<ProjectInvitationResponse> AcceptAsync(Guid invitationId, Guid accountId, CancellationToken ct = default)
        => DecideAsync(invitationId, accountId, ProjectInvitationStatus.Accepted, ct);

    private Task<ProjectInvitationResponse> DecideAsync(Guid invitationId, Guid accountId,
        ProjectInvitationStatus decision, CancellationToken ct) => MutateAsync(async () =>
    {
        var actor = await GetStudentAsync(accountId, ct);
        var invitation = await Invitations().FirstOrDefaultAsync(i => i.Id == invitationId, ct)
            ?? throw new ProjectInvitationException("INVITATION_NOT_FOUND", "Project invitation not found.", 404);
        if (decision == ProjectInvitationStatus.Cancelled)
            RequireCreator(invitation.Project, actor.Id);
        else
            Require(invitation.UserId == actor.Id, "FORBIDDEN", "Only the invitee can accept or decline this invitation.", 403);
        Require(invitation.Status == ProjectJoinRequestStatus.Pending,
            "INVALID_INVITATION_STATE", "Only sent invitations can be processed.", 409);

        var now = DateTimeOffset.UtcNow;
        if (decision == ProjectInvitationStatus.Accepted)
        {
            Require(actor.Id != invitation.Project.CreatorId,
                "CANNOT_JOIN_OWN_PROJECT", "You cannot join your own project through an invitation.", 409);
            Require(ProjectHubService.AllowsFriendInvitations(invitation.Project),
                "INVALID_PROJECT_STATE", "Project cannot receive friend invitations.", 409);
            var member = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == invitation.ProjectId &&
                m.UserId == actor.Id, ct);
            Require(member?.Status != ProjectMemberStatus.Active,
                "ALREADY_PROJECT_MEMBER", "You are already a member of this project.", 409);
            if (member == null)
            {
                member = new ProjectMember
                {
                    Id = Guid.NewGuid(), ProjectId = invitation.ProjectId,
                    UserId = actor.Id, CreatedAt = now
                };
                db.ProjectMembers.Add(member);
            }
            member.JoinRequestId = invitation.Id;
            member.PositionId = invitation.PositionId;
            member.Role = invitation.Role;
            member.Status = ProjectMemberStatus.Active;
            member.JoinedAt = now;
            member.LeftAt = null;
            member.UpdatedAt = now;
            invitation.Project.UpdatedAt = now;

            var requests = await db.ProjectJoinRequests.Where(r =>
                r.Id != invitation.Id && r.ProjectId == invitation.ProjectId && r.UserId == invitation.UserId &&
                r.Status == ProjectJoinRequestStatus.Pending).ToListAsync(ct);
            foreach (var request in requests)
            {
                request.Status = ProjectJoinRequestStatus.Cancelled;
                request.UpdatedAt = now;
            }
        }
        invitation.Status = (ProjectJoinRequestStatus)(short)decision;
        invitation.ReviewedBy = actor.Id;
        invitation.ReviewedAt = now;
        invitation.UpdatedAt = now;
        var recipientAccountId = decision == ProjectInvitationStatus.Cancelled ? invitation.User.AccountId
            : invitation.Inviter?.AccountId ?? throw new ProjectInvitationException(
                "INVALID_INVITATION", "The invitation has no inviter profile.", 409);
        await NotificationService.EnqueueAsync(db, [recipientAccountId],
            new("project.invitation.updated", "Project", "Cập nhật lời mời dự án",
                decision switch
                {
                    ProjectInvitationStatus.Accepted => $"{actor.FullName} đã chấp nhận lời mời tham gia dự án {invitation.Project.Title}.",
                    ProjectInvitationStatus.Declined => $"{actor.FullName} đã từ chối lời mời tham gia dự án {invitation.Project.Title}.",
                    _ => $"Lời mời tham gia dự án {invitation.Project.Title} đã bị hủy."
                }, "ProjectInvitation", invitation.Id, EventKey: $"project-invitation:{invitation.Id}:{decision}", SenderUserId: accountId), ct);
        return invitation;
    }, ct);

    private IQueryable<ProjectJoinRequest> Invitations() => db.ProjectJoinRequests.Where(i => i.RequestType == ProjectRequestType.Invite)
        .Include(i => i.Project).Include(i => i.Inviter).Include(i => i.User);

    private async Task<ProjectInvitationsResponse> ListAsync(IQueryable<ProjectJoinRequest> query,
        ProjectInvitationStatus? status, int page, int pageSize, CancellationToken ct)
    {
        Require(!status.HasValue || Enum.IsDefined(status.Value), "INVALID_STATUS", "Invalid invitation status.");
        if (status.HasValue) query = query.Where(i => i.Status == (ProjectJoinRequestStatus)(short)status.Value);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var invitations = await query.AsNoTracking().OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize),
            invitations.Select(ProjectInvitationMapping.ToResponse).ToList());
    }

    private async Task<UserProfile> GetStudentAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.Include(p => p.Account).FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new ProjectInvitationException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        RequireActiveStudent(profile);
        return profile;
    }

    private static void RequireActiveStudent(UserProfile profile) => Require(
        profile.Account.Role == AccountRole.Student && profile.Account.Status == AccountStatus.Active,
        "FORBIDDEN", "Only active student accounts can perform this action.", 403);

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
        ?? throw new ProjectInvitationException("PROJECT_NOT_FOUND", "Project not found.", 404);

    private static void RequireCreator(Project project, Guid profileId) => Require(project.CreatorId == profileId,
        "FORBIDDEN", "Only the project creator can send, view sent invitations, or cancel invitations.", 403);

    private async Task<ProjectInvitationResponse> MutateAsync(Func<Task<ProjectJoinRequest>> action, CancellationToken ct)
    {
        try
        {
            // Serialize membership decisions and keep their notifications atomic.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var invitation = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ProjectInvitationMapping.ToResponse(invitation);
        }
        catch (Exception ex) when (ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } })
        {
            throw new ProjectInvitationException("INVITATION_CONFLICT", "Data has been modified. Please try again.", 409);
        }
    }

    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new ProjectInvitationException(code, message, status);
    }
}
