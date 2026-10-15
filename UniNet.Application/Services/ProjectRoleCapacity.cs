using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

internal static class ProjectRoleCapacity
{
    // Call inside the membership transaction, after saving the new active member.
    // Historical decisions remain unchanged; only outstanding applications expire their opportunity.
    public static async Task CancelUnavailableAsync(UniNetDbContext db, Project project, CancellationToken ct)
    {
        var requests = await db.ProjectJoinRequests.Where(r => r.ProjectId == project.Id &&
            r.Status == ProjectJoinRequestStatus.Pending).ToListAsync(ct);
        var invitations = await db.ProjectInvitations.Where(i => i.ProjectId == project.Id &&
            i.Status == ProjectInvitationStatus.Sent).ToListAsync(ct);
        var members = await db.ProjectMembers.Where(m => m.ProjectId == project.Id &&
            m.Status == ProjectMemberStatus.Active).Select(m => m.UserId).ToListAsync(ct);
        var full = 1 + members.Count(id => id != project.CreatorId) >= project.MemberTarget;
        var unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in requests.Select(r => r.Role).Concat(invitations.Select(i => i.Role))
                     .Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            if (full || await CheckAsync(db, project, role, ct) != null)
                unavailable.Add(role);

        var now = DateTimeOffset.UtcNow;
        foreach (var request in requests.Where(r => unavailable.Contains(r.Role.Trim()) || members.Contains(r.UserId)))
        {
            request.Status = ProjectJoinRequestStatus.Cancelled;
            request.UpdatedAt = now;
        }
        foreach (var invitation in invitations.Where(i => unavailable.Contains(i.Role.Trim()) || members.Contains(i.InviteeId)))
        {
            invitation.Status = ProjectInvitationStatus.Cancelled;
            invitation.UpdatedAt = now;
        }
    }

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
