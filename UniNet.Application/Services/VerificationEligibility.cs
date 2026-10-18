using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

// Recompute both hashes at every transition; never silently rebind an existing case.
public sealed class VerificationEligibility(UniNetDbContext db, IConfiguration configuration)
{
    public async Task<ProjectEvidenceModeration> RequirePassedAsync(Guid projectId, Guid? pinnedId, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.Status == ProjectStatus.Completed, ct))
            throw new ProjectVerificationException("PROJECT_NOT_COMPLETED", "Only completed projects are eligible.", 409);
        var current = await db.ProjectEvidenceModerations.Include(m => m.Attempts)
            .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.SupersededAt == null, ct);
        if (current == null || current.Status != EvidenceModerationStatus.Passed)
            throw new ProjectVerificationException("LEVEL1_REQUIRED", "A current passed level-1 review is required.", 409);
        var source = new EvidenceModerationSource(db, configuration);
        if ((pinnedId.HasValue && current.Id != pinnedId) ||
            current.SourceHash != EvidenceModerationSource.Hash(JsonSerializer.Serialize(await source.CaptureAsync(projectId, ct))) ||
            current.PolicyHash != EvidenceModerationSource.Hash(JsonSerializer.Serialize(source.CapturePolicy())))
            throw new ProjectVerificationException("LEVEL1_STALE", "The level-1 decision no longer matches the source or policy. Existing booking and payment are preserved.", 409);
        return current;
    }

    public async Task RequireCaseAsync(ProjectVerification verification, CancellationToken ct)
    {
        if (!verification.EvidenceModerationId.HasValue)
            throw new ProjectVerificationException("LEVEL1_LINK_REQUIRED", "This legacy case requires an explicit level-1 migration review.", 409);
        await RequirePassedAsync(verification.ProjectId, verification.EvidenceModerationId, ct);
    }
}
