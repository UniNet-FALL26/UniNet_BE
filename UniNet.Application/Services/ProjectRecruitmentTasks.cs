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
            .Where(p => (p.Status == ProjectStatus.Active &&
                (p.RecruitmentStatus == RecruitmentStatus.Open || p.RecruitmentStatus == RecruitmentStatus.Full) &&
                p.RecruitmentDeadline <= now) ||
                ((p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Pending) && p.Recruitment != null &&
                 p.Recruitment.Status == ProjectRecruitmentStatus.Open && p.Recruitment.Deadline <= now))
            .Include(p => p.Members).Include(p => p.JoinRequests)
            .AsSplitQuery().ToListAsync(ct);
        foreach (var project in projects)
        {
            var count = 1 + project.Members.Count(m => m.UserId != project.CreatorId &&
                m.Status == ProjectMemberStatus.Active);
            project.RecruitmentStatus = count >= project.MinimumQuantity
                ? RecruitmentStatus.Closed : RecruitmentStatus.Expired;
            project.UpdatedAt = now;
            var modern = project.Recruitment?.PublishedAt != null;
            if (project.Recruitment is { Status: ProjectRecruitmentStatus.Open } recruitment)
            {
                recruitment.Status = ProjectRecruitmentStatus.Closed;
                recruitment.UpdatedAt = now;
            }
            foreach (var request in project.JoinRequests.Where(r => r.RequestType == ProjectRequestType.Apply && r.Status == ProjectJoinRequestStatus.Pending))
            {
                request.Status = modern ? ProjectJoinRequestStatus.Expired : ProjectJoinRequestStatus.Cancelled;
                request.UpdatedAt = now;
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
