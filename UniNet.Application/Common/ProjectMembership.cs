using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Common;

internal static class ProjectMembership
{
    public static IQueryable<Guid> EffectiveMemberIds(UniNetDbContext db, Guid projectId) =>
        db.Projects.AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => project.CreatorId)
            .Union(db.ProjectMembers.AsNoTracking()
                .Where(member => member.ProjectId == projectId && member.Status == ProjectMemberStatus.Active && member.LeftAt == null)
                .Select(member => member.UserId));

    public static IQueryable<Guid> EffectiveProjectIds(UniNetDbContext db, Guid userProfileId) =>
        db.Projects.AsNoTracking()
            .Where(project => project.CreatorId == userProfileId)
            .Select(project => project.Id)
            .Union(db.ProjectMembers.AsNoTracking()
                .Where(member => member.UserId == userProfileId && member.Status == ProjectMemberStatus.Active && member.LeftAt == null)
                .Select(member => member.ProjectId));

    public static Task<bool> IsEffectiveMemberAsync(
        UniNetDbContext db, Guid projectId, Guid userProfileId, CancellationToken ct) =>
        EffectiveMemberIds(db, projectId).AnyAsync(id => id == userProfileId, ct);
}
