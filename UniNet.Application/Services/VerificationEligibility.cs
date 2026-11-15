using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

// Paid verification eligibility is independent of legacy evidence moderation records.
public sealed class VerificationEligibility(UniNetDbContext db)
{
    public async Task RequireCompletedProjectAsync(Guid projectId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.Status == ProjectStatus.Completed, ct))
            throw new ProjectVerificationException("PROJECT_NOT_COMPLETED", "Only completed projects are eligible.", 409);
    }

    public Task RequireCaseAsync(ProjectVerification verification, CancellationToken ct) =>
        RequireCompletedProjectAsync(verification.ProjectId, ct);
}
