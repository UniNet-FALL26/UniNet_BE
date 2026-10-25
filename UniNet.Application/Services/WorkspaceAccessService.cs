using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class WorkspaceAccessService(UniNetDbContext db) : IWorkspaceAccessService
{
    public async Task<UserProfile> RequireMemberAsync(Guid projectId, Guid accountId, CancellationToken ct = default)
    {
        var (profile, project) = await GetProfileAndProjectAsync(projectId, accountId, ct);
        var isMember = project.CreatorId == profile.Id || await db.ProjectMembers.AsNoTracking()
            .AnyAsync(member => member.ProjectId == project.Id &&
                member.UserId == profile.Id &&
                member.Status == ProjectMemberStatus.Active &&
                member.LeftAt == null, ct);
        Require(isMember, "PROJECT_ACCESS_DENIED", "You are not authorized to access this project.", 403);
        return profile;
    }

    public async Task<UserProfile> RequireLeaderAsync(Guid projectId, Guid accountId, CancellationToken ct = default)
    {
        var (profile, project) = await GetProfileAndProjectAsync(projectId, accountId, ct);
        Require(project.CreatorId == profile.Id, "FORBIDDEN", "Only the project leader can manage workspace channels.", 403);
        return profile;
    }

    private async Task<(UserProfile Profile, Project Project)> GetProfileAndProjectAsync(
        Guid projectId, Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.Include(user => user.Account)
            .FirstOrDefaultAsync(user => user.AccountId == accountId, ct);
        Require(profile is not null, "PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(profile.Account.Status == AccountStatus.Active &&
            profile.Account.Role == AccountRole.Student,
            "FORBIDDEN", "Only active Student accounts can access workspaces.", 403);

        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == projectId, ct);
        Require(project is not null, "PROJECT_NOT_FOUND", "Project not found.", 404);
        return (profile, project);
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    {
        if (!condition)
            throw new ProjectException(code, message, status);
    }
}
