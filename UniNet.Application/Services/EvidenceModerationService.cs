using System.Data;
using UniNet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class EvidenceModerationService(UniNetDbContext db, IConfiguration configuration)
{
    private async Task AuthorizeAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.Include(p => p.Account).SingleOrDefaultAsync(p => p.AccountId == accountId, ct);
        if (profile == null || profile.Account.Status != AccountStatus.Active)
            throw new ProjectVerificationException("FORBIDDEN", "An active account is required.", 403);
        var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectVerificationException("PROJECT_NOT_FOUND", "Project not found.", 404);
        if (project.CreatorId != profile.Id && profile.Account.Role != AccountRole.Admin)
            throw new ProjectVerificationException("FORBIDDEN", "Only the project creator or Admin can access level-1 review.", 403);
    }

    public async Task<EvidenceModerationResponse> GetAsync(Guid projectId, Guid accountId, CancellationToken ct = default)
    {
        await AuthorizeAsync(projectId, accountId, ct);
        var review = await db.ProjectModerations.Where(m => m.Purpose == ProjectModerationPurpose.ProjectEvidence).AsNoTracking().Include(m => m.Attempts)
            .OrderByDescending(m => m.Version).FirstOrDefaultAsync(m => m.ProjectId == projectId, ct)
            ?? throw new ProjectVerificationException("LEVEL1_NOT_FOUND", "No level-1 review exists.", 404);
        var source = new EvidenceModerationSource(db, configuration);
        var current = review.SupersededAt == null && review.SourceHash == EvidenceModerationSource.Hash(
            System.Text.Json.JsonSerializer.Serialize(await source.CaptureAsync(projectId, ct))) &&
            review.PolicyHash == EvidenceModerationSource.Hash(System.Text.Json.JsonSerializer.Serialize(source.CapturePolicy()));
        return EvidenceModerationMapping.ToResponse(review) with { IsCurrent = current };
    }

    // An unchanged decision cannot be reset to bypass the worker's five-attempt budget.
    // Source/policy changes create a new immutable version; Error retries retain all attempts.
    public async Task<EvidenceModerationResponse> RetryAsync(Guid projectId, Guid accountId, CancellationToken ct = default)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await AuthorizeAsync(projectId, accountId, ct);
            if (!await db.Projects.AnyAsync(p => p.Id == projectId && p.Status == ProjectStatus.Completed, ct))
                throw new ProjectVerificationException("PROJECT_NOT_COMPLETED", "Complete the project before requesting level-1 review.", 409);
            var review = await new EvidenceModerationSource(db, configuration).RefreshAsync(projectId, DateTimeOffset.UtcNow, ct, createInitial: true);
            if (review!.Status == ProjectModerationStatus.Error)
            {
                if (review.AttemptCount >= 5 || review.NextRetryAt == null)
                    throw new ProjectVerificationException("LEVEL1_RETRY_EXHAUSTED", "Correct the source or policy before requesting another review.", 409);
                // Keep the scheduled backoff. Calling this endpoint never accelerates provider calls.
            }
            else if (review.Status is ProjectModerationStatus.Passed or ProjectModerationStatus.Rejected or ProjectModerationStatus.NeedsEvidence)
                throw new ProjectVerificationException("LEVEL1_UNCHANGED", "Update task/report data before requesting a new review.", 409);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await GetAsync(projectId, accountId, ct);
        }
        catch (Exception ex) when (EvidenceModerationSource.IsWriteConflict(ex))
        {
            throw new ProjectVerificationException("VERIFICATION_CONFLICT", "Review changed concurrently. Reload and retry.", 409);
        }
    }
}
