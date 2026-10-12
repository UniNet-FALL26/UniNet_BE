using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

internal static class ProjectRoleCapacity
{
    public static async Task<(string Code, string Message)?> CheckAsync(
        UniNetDbContext db, Project project, string role, CancellationToken ct)
    {
        var requirements = await db.ProjectRoleRequirements.AsNoTracking()
            .Where(r => r.ProjectId == project.Id).ToListAsync(ct);
        var normalizedRole = role.Trim();
        var matching = requirements.Where(r => string.Equals(r.Role.Trim(), normalizedRole,
            StringComparison.OrdinalIgnoreCase)).ToList();
        if (requirements.Count > 0 && matching.Count == 0)
            return ("ROLE_NOT_REQUIRED", "This position is not being recruited for this project.");

        // Projects without configured requirements allow one member per position.
        var capacity = matching.Count > 0 ? matching.Sum(r => r.Quantity) : 1;
        var roles = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == project.Id && m.UserId != project.CreatorId &&
                m.Status == ProjectMemberStatus.Active)
            .Select(m => m.Role).ToListAsync(ct);
        var occupied = roles.Count(r => string.Equals(r.Trim(), normalizedRole,
            StringComparison.OrdinalIgnoreCase));
        return occupied < capacity ? null : ("PROJECT_ROLE_FULL", "This position has no available slots.");
    }
}
