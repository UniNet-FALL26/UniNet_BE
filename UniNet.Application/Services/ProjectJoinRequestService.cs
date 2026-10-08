using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectJoinRequestService(UniNetDbContext db) : IProjectJoinRequestService
{
    public Task<ProjectJoinRequestResponse> SendAsync(Guid projectId, Guid accountId,
        CreateProjectJoinRequestRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        var student = await GetStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        Require(project.CreatorId != student.Id, "CANNOT_JOIN_OWN_PROJECT", "You cannot send a request to join your own project.", 403);
        Require(!string.IsNullOrWhiteSpace(request.Role) && request.Role.Trim().Length <= 150,
            "INVALID_ROLE", "Role must be between 1 and 150 characters.");
        await RequireRecruitingAsync(project, ct);
        Require(!await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == student.Id &&
            m.Status == ProjectMemberStatus.Active, ct), "ALREADY_PROJECT_MEMBER", "You are already a member of this project.", 409);
        Require(!await db.ProjectJoinRequests.AnyAsync(r => r.ProjectId == projectId && r.UserId == student.Id &&
            r.Status == ProjectJoinRequestStatus.Pending, ct), "JOIN_REQUEST_EXISTS", "You already have a pending request.", 409);

        var now = DateTimeOffset.UtcNow;
        var entity = new ProjectJoinRequest
        {
            Id = Guid.NewGuid(), Project = project, User = student,
            ProjectId = project.Id, UserId = student.Id,
            Role = request.Role.Trim(), Message = string.IsNullOrWhiteSpace(request.Message) ? null : request.Message.Trim(),
            Status = ProjectJoinRequestStatus.Pending, CreatedAt = now, UpdatedAt = now
        };
        db.ProjectJoinRequests.Add(entity);
        return entity;
    }, ct);

    public async Task<ProjectJoinRequestsResponse> GetProjectRequestsAsync(Guid projectId, Guid accountId,
        ProjectJoinRequestStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var creator = await GetStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireCreator(project, creator.Id);
        return await ListAsync(Requests().Where(r => r.ProjectId == projectId), status, page, pageSize, ct);
    }

    public async Task<ProjectJoinRequestsResponse> GetMyRequestsAsync(Guid accountId,
        ProjectJoinRequestStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var student = await GetStudentAsync(accountId, ct);
        return await ListAsync(Requests().Where(r => r.UserId == student.Id &&
            r.Project.Visibility == ProjectVisibility.Public && r.Project.Status == ProjectStatus.Active),
            status, page, pageSize, ct);
    }

    public Task<ProjectJoinRequestResponse> CancelAsync(Guid requestId, Guid accountId, CancellationToken ct = default)
        => DecideAsync(requestId, accountId, ProjectJoinRequestStatus.Cancelled, ct);

    public Task<ProjectJoinRequestResponse> AcceptAsync(Guid requestId, Guid accountId, CancellationToken ct = default)
        => DecideAsync(requestId, accountId, ProjectJoinRequestStatus.Accepted, ct);

    public Task<ProjectJoinRequestResponse> RejectAsync(Guid requestId, Guid accountId, CancellationToken ct = default)
        => DecideAsync(requestId, accountId, ProjectJoinRequestStatus.Rejected, ct);

    private Task<ProjectJoinRequestResponse> DecideAsync(Guid requestId, Guid accountId,
        ProjectJoinRequestStatus decision, CancellationToken ct) => MutateAsync(async () =>
    {
        var actor = await GetStudentAsync(accountId, ct);
        var request = await Requests().FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new ProjectJoinRequestException("JOIN_REQUEST_NOT_FOUND", "Join request not found.", 404);
        RequireEligible(request.Project);
        if (decision == ProjectJoinRequestStatus.Cancelled)
            Require(request.UserId == actor.Id, "FORBIDDEN", "You can only cancel your own request.", 403);
        else
            RequireCreator(request.Project, actor.Id);
        Require(request.Status == ProjectJoinRequestStatus.Pending,
            "INVALID_JOIN_REQUEST_STATE", "Only pending requests can be processed.", 409);

        var now = DateTimeOffset.UtcNow;
        if (decision == ProjectJoinRequestStatus.Accepted)
        {
            await GetStudentAsync(request.User.AccountId, ct);
            Require(request.UserId != request.Project.CreatorId,
                "CANNOT_JOIN_OWN_PROJECT", "You cannot send a request to join your own project.", 409);
            await RequireRecruitingAsync(request.Project, ct);
            var member = await db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == request.ProjectId &&
                m.UserId == request.UserId, ct);
            Require(member?.Status != ProjectMemberStatus.Active,
                "ALREADY_PROJECT_MEMBER", "You are already a member of this project.", 409);
            if (member == null)
            {
                member = new ProjectMember
                {
                    Id = Guid.NewGuid(), ProjectId = request.ProjectId,
                    UserId = request.UserId, CreatedAt = now
                };
                db.ProjectMembers.Add(member);
            }
            member.Role = request.Role;
            member.Status = ProjectMemberStatus.Active;
            member.JoinedAt = now;
            member.LeftAt = null;
            member.UpdatedAt = now;
            if (await MemberCountAsync(request.Project, ct) + 1 >= request.Project.MemberTarget)
                request.Project.RecruitmentStatus = RecruitmentStatus.Full;
            request.Project.UpdatedAt = now;
        }
        request.Status = decision;
        request.UpdatedAt = now;
        return request;
    }, ct);

    private IQueryable<ProjectJoinRequest> Requests() => db.ProjectJoinRequests
        .Include(r => r.Project).Include(r => r.User);

    private async Task<ProjectJoinRequestsResponse> ListAsync(IQueryable<ProjectJoinRequest> query,
        ProjectJoinRequestStatus? status, int page, int pageSize, CancellationToken ct)
    {
        Require(!status.HasValue || Enum.IsDefined(status.Value), "INVALID_STATUS", "Invalid request status.");
        if (status.HasValue) query = query.Where(r => r.Status == status.Value);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var requests = await query.AsNoTracking().OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize),
            requests.Select(ProjectJoinRequestMapping.ToResponse).ToList());
    }

    private async Task<UserProfile> GetStudentAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.Include(p => p.Account).FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new ProjectJoinRequestException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(profile.Account.Role == AccountRole.Student && profile.Account.Status == AccountStatus.Active,
            "FORBIDDEN", "Only active student accounts can perform this action.", 403);
        return profile;
    }

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectJoinRequestException("PROJECT_NOT_FOUND", "Project not found.", 404);
        RequireEligible(project);
        return project;
    }

    private static void RequireEligible(Project project) => Require(
        project.Visibility == ProjectVisibility.Public && project.Status == ProjectStatus.Active,
        "INVALID_PROJECT_STATE", "Project must be Public and Active.", 409);

    private static void RequireCreator(Project project, Guid profileId) => Require(project.CreatorId == profileId,
        "FORBIDDEN", "Only the project creator can view and process join requests.", 403);

    private async Task RequireRecruitingAsync(Project project, CancellationToken ct)
    {
        Require(project.RecruitmentStatus == RecruitmentStatus.Open && project.RecruitmentDeadline > DateTimeOffset.UtcNow,
            "RECRUITMENT_CLOSED", "Project is no longer accepting members.", 409);
        Require(await MemberCountAsync(project, ct) < project.MemberTarget,
            "PROJECT_FULL", "Project has reached its member limit.", 409);
    }

    // The creator occupies one slot even when no ProjectMember row exists for them.
    private async Task<int> MemberCountAsync(Project project, CancellationToken ct) => 1 +
        await db.ProjectMembers.CountAsync(m => m.ProjectId == project.Id && m.UserId != project.CreatorId &&
            m.Status == ProjectMemberStatus.Active, ct);

    private async Task<ProjectJoinRequestResponse> MutateAsync(Func<Task<ProjectJoinRequest>> action, CancellationToken ct)
    {
        try
        {
            // Serialize writes to prevent duplicate pending requests and overfilling a project.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var request = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ProjectJoinRequestMapping.ToResponse(request);
        }
        catch (Exception ex) when (ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } })
        {
            throw new ProjectJoinRequestException("JOIN_REQUEST_CONFLICT", "Data has been modified. Please try again.", 409);
        }
    }

    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new ProjectJoinRequestException(code, message, status);
    }
}
