using System.Data;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectRecruitmentTasks(UniNetDbContext db)
{
    public async Task RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var projects = await db.Projects
            .Where(p => p.Status == ProjectStatus.Active &&
                (p.RecruitmentStatus == RecruitmentStatus.Open || p.RecruitmentStatus == RecruitmentStatus.Full) &&
                p.RecruitmentDeadline <= now)
            .Include(p => p.Members).Include(p => p.JoinRequests).Include(p => p.Invitations)
            .AsSplitQuery().ToListAsync(ct);
        foreach (var project in projects)
        {
            var count = 1 + project.Members.Count(m => m.UserId != project.CreatorId &&
                m.Status == ProjectMemberStatus.Active);
            project.RecruitmentStatus = count >= project.MinimumQuantity
                ? RecruitmentStatus.Closed : RecruitmentStatus.Expired;
            project.UpdatedAt = now;
            foreach (var request in project.JoinRequests.Where(r => r.Status == ProjectJoinRequestStatus.Pending))
            {
                request.Status = ProjectJoinRequestStatus.Cancelled;
                request.UpdatedAt = now;
            }
            foreach (var invitation in project.Invitations.Where(i => i.Status == ProjectInvitationStatus.Sent))
            {
                invitation.Status = ProjectInvitationStatus.Cancelled;
                invitation.UpdatedAt = now;
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
