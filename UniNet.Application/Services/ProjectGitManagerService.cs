using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectGitManagerService(UniNetDbContext db) : IProjectGitManagerService
{
    public async Task<IReadOnlyList<ProjectGitManagerResponse>> GetAsync(
        Guid projectId, Guid accountId, CancellationToken ct)
    {
        await RequireCreatorAsync(projectId, accountId, ct);
        var grants = await db.ProjectGitManagerGrants.AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .Join(db.UserProfiles.AsNoTracking(), x => x.UserProfileId, user => user.Id,
                (grant, user) => new { Grant = grant, User = user })
            .OrderBy(x => x.Grant.GrantedAt)
            .ToListAsync(ct);
        return grants.Select(x => new ProjectGitManagerResponse(
            x.Grant.Id, x.Grant.UserProfileId, x.User.FullName, x.User.AvatarUrl,
            x.Grant.GrantedAt, x.Grant.IsActive, x.Grant.RevokedAt,
            x.Grant.GrantedByProfileId, x.Grant.RevokedByProfileId)).ToArray();
    }

    public async Task<ProjectGitManagerResponse> GrantAsync(
        Guid projectId, Guid accountId, GrantProjectGitManagerRequest request, CancellationToken ct)
    {
        var actorId = await RequireCreatorAsync(projectId, accountId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var target = await db.ProjectMembers.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.UserId == request.UserProfileId &&
                x.Status == ProjectMemberStatus.Active &&
                x.User.Account.Status == AccountStatus.Active && x.User.Account.Role == AccountRole.Student)
            .Select(x => new { x.User.Id, x.User.FullName, x.User.AvatarUrl })
            .SingleOrDefaultAsync(ct)
            ?? throw Error("ACTIVE_MEMBER_REQUIRED", "Git Managers must be active project members.", 400);
        if (target.Id == actorId)
            throw Error("CREATOR_ALREADY_HAS_ACCESS", "The project creator already manages repository integration.", 409);
        if (await db.ProjectGitManagerGrants.AnyAsync(x =>
                x.ProjectId == projectId && x.UserProfileId == target.Id && x.IsActive, ct))
            throw Error("GIT_MANAGER_EXISTS", "This member is already a Git Manager.", 409);

        var grant = new ProjectGitManagerGrant
        {
            ProjectId = projectId,
            UserProfileId = target.Id,
            GrantedByProfileId = actorId,
            GrantedAt = DateTimeOffset.UtcNow
        };
        db.ProjectGitManagerGrants.Add(grant);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw Error("GIT_MANAGER_EXISTS", "This member is already a Git Manager.", 409);
        }
        return new(grant.Id, target.Id, target.FullName, target.AvatarUrl,
            grant.GrantedAt, true, null, grant.GrantedByProfileId, null);
    }

    public async Task RevokeAsync(
        Guid projectId, Guid userProfileId, Guid accountId, CancellationToken ct)
    {
        var actorId = await RequireCreatorAsync(projectId, accountId, ct);
        var grant = await db.ProjectGitManagerGrants.SingleOrDefaultAsync(x =>
            x.ProjectId == projectId && x.UserProfileId == userProfileId && x.IsActive, ct)
            ?? throw Error("GIT_MANAGER_NOT_FOUND", "Active Git Manager grant not found.", 404);
        grant.IsActive = false;
        grant.RevokedAt = DateTimeOffset.UtcNow;
        grant.RevokedByProfileId = actorId;
        await db.SaveChangesAsync(ct);
    }

    private async Task<Guid> RequireCreatorAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var actor = await db.UserProfiles.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Account.Status == AccountStatus.Active &&
                x.Account.Role == AccountRole.Student)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct)
            ?? throw Error("FORBIDDEN", "An active Student account is required.", 403);
        var creatorId = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => (Guid?)x.CreatorId)
            .SingleOrDefaultAsync(ct)
            ?? throw Error("PROJECT_NOT_FOUND", "Project not found.", 404);
        if (creatorId != actor)
            throw Error("FORBIDDEN", "Only the project creator can manage Git Manager grants.", 403);
        return actor;
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
