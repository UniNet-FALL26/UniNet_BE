using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        await RequireRecruitingAsync(project, ct);
        Require(!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == invitee.Id &&
            m.Status == ProjectMemberStatus.Active, ct), "ALREADY_PROJECT_MEMBER", "The invitee is already a member of this project.", 409);
        Require(!await db.ProjectInvitations.AnyAsync(i => i.ProjectId == projectId && i.InviteeId == invitee.Id &&
            i.Status == ProjectInvitationStatus.Sent, ct), "INVITATION_EXISTS", "The invitee already has a pending invitation to this project.", 409);

        var now = DateTimeOffset.UtcNow;
        var invitation = new ProjectInvitation
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Project = project,
            InviterId = inviter.Id, Inviter = inviter, InviteeId = invitee.Id, Invitee = invitee,
            Role = request.Role.Trim(), Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
            Status = ProjectInvitationStatus.Sent, CreatedAt = now, UpdatedAt = now
        };
        db.ProjectInvitations.Add(invitation);
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
        return await ListAsync(Invitations().Where(i => i.InviteeId == invitee.Id), status, page, pageSize, ct);
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
            Require(invitation.InviteeId == actor.Id, "FORBIDDEN", "Only the invitee can accept or decline this invitation.", 403);
        Require(invitation.Status == ProjectInvitationStatus.Sent,
            "INVALID_INVITATION_STATE", "Only sent invitations can be processed.", 409);

        var now = DateTimeOffset.UtcNow;
        if (decision == ProjectInvitationStatus.Accepted)
        {
            Require(actor.Id != invitation.Project.CreatorId,
                "CANNOT_JOIN_OWN_PROJECT", "You cannot join your own project through an invitation.", 409);
            await RequireRecruitingAsync(invitation.Project, ct);
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
            member.Role = invitation.Role;
            member.Status = ProjectMemberStatus.Active;
            member.JoinedAt = now;
            member.LeftAt = null;
            member.UpdatedAt = now;
            if (await MemberCountAsync(invitation.Project, ct) + 1 >= invitation.Project.MemberTarget)
                invitation.Project.RecruitmentStatus = RecruitmentStatus.Full;
            invitation.Project.UpdatedAt = now;
        }
        invitation.Status = decision;
        invitation.UpdatedAt = now;
        return invitation;
    }, ct);

    private IQueryable<ProjectInvitation> Invitations() => db.ProjectInvitations
        .Include(i => i.Project).Include(i => i.Inviter).Include(i => i.Invitee);

    private async Task<ProjectInvitationsResponse> ListAsync(IQueryable<ProjectInvitation> query,
        ProjectInvitationStatus? status, int page, int pageSize, CancellationToken ct)
    {
        Require(!status.HasValue || Enum.IsDefined(status.Value), "INVALID_STATUS", "Invalid invitation status.");
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
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

    private async Task RequireRecruitingAsync(Project project, CancellationToken ct)
    {
        Require(project.Status == ProjectStatus.Active,
            "INVALID_PROJECT_STATE", "Project must be Active.", 409);
        Require(project.RecruitmentStatus == RecruitmentStatus.Open && project.RecruitmentDeadline > DateTimeOffset.UtcNow,
            "RECRUITMENT_CLOSED", "Project is no longer accepting members.", 409);
        Require(await MemberCountAsync(project, ct) < project.MemberTarget,
            "PROJECT_FULL", "Project has reached its member limit.", 409);
    }

    private async Task<int> MemberCountAsync(Project project, CancellationToken ct) => 1 +
        await db.ProjectMembers.CountAsync(m => m.ProjectId == project.Id && m.UserId != project.CreatorId &&
            m.Status == ProjectMemberStatus.Active, ct);

    private async Task<ProjectInvitationResponse> MutateAsync(Func<Task<ProjectInvitation>> action, CancellationToken ct)
    {
        try
        {
            // Match join requests so concurrent acceptances cannot overfill the project.
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
