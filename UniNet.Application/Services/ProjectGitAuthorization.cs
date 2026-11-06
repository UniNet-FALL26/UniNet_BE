using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Domain;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

internal static class ProjectGitAuthorization
{
    public static async Task<Guid> RequireActorProfileAsync(
        UniNetDbContext db, Guid accountId, CancellationToken ct)
    {
        return await db.UserProfiles.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Account.Status == AccountStatus.Active &&
                x.Account.Role == AccountRole.Student)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct)
            ?? throw Error("FORBIDDEN", "An active Student account is required.", 403);
    }

    public static async Task<Guid> RequireManagerAsync(
        UniNetDbContext db, Guid projectId, Guid accountId, CancellationToken ct)
    {
        var profileId = await RequireActorProfileAsync(db, accountId, ct);
        var creatorId = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => (Guid?)x.CreatorId)
            .SingleOrDefaultAsync(ct)
            ?? throw Error("PROJECT_NOT_FOUND", "Project not found.", 404);
        if (creatorId == profileId) return profileId;

        var isManager = await db.ProjectGitManagerGrants.AsNoTracking().AnyAsync(x =>
            x.ProjectId == projectId && x.UserProfileId == profileId && x.IsActive &&
            db.ProjectMembers.Any(m => m.ProjectId == projectId && m.UserId == profileId &&
                m.Status == ProjectMemberStatus.Active), ct);
        if (!isManager)
            throw Error("FORBIDDEN", "Project creator or an authorized Git Manager permission is required.", 403);
        return profileId;
    }

    public static async Task<Guid> RequireProjectViewerAsync(
        UniNetDbContext db, Guid projectId, Guid accountId, CancellationToken ct)
    {
        var profileId = await RequireActorProfileAsync(db, accountId, ct);
        var project = await db.Projects.AsNoTracking()
            .Where(x => x.Id == projectId)
            .Select(x => new { x.CreatorId })
            .SingleOrDefaultAsync(ct)
            ?? throw Error("PROJECT_NOT_FOUND", "Project not found.", 404);
        if (project.CreatorId == profileId) return profileId;

        var isMember = await db.ProjectMembers.AsNoTracking().AnyAsync(x =>
            x.ProjectId == projectId && x.UserId == profileId && x.Status == ProjectMemberStatus.Active, ct);
        if (!isMember)
            throw Error("FORBIDDEN", "An active project member is required.", 403);
        return profileId;
    }

    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
