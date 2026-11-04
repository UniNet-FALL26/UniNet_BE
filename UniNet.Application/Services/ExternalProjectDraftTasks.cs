using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ExternalProjectDraftTasks(UniNetDbContext db, ExternalProjectDraftExtractionService extraction,
    ILogger<ExternalProjectDraftTasks> logger)
{
    public async Task RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await db.ExternalProjectDrafts.Where(d => d.Status != ExternalProjectDraftStatus.Confirmed &&
            d.Status != ExternalProjectDraftStatus.Expired && d.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, ExternalProjectDraftStatus.Expired)
                .SetProperty(d => d.LeaseToken, (Guid?)null).SetProperty(d => d.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(d => d.Version, d => d.Version + 1).SetProperty(d => d.UpdatedAt, now), ct);
        var due = await db.ExternalProjectDrafts.AsNoTracking().Where(d => d.Status == ExternalProjectDraftStatus.Processing &&
            (d.NextAttemptAt == null || d.NextAttemptAt <= now) && (d.LeaseExpiresAt == null || d.LeaseExpiresAt <= now))
            .OrderBy(d => d.CreatedAt).Select(d => new { d.Id, d.Version, d.AttemptCount, d.OwnerUser.AccountId }).Take(5).ToListAsync(ct);
        foreach (var item in due)
        {
            var token = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow > now ? DateTimeOffset.UtcNow : now;
            if (item.AttemptCount >= 3)
            {
                await db.ExternalProjectDrafts.Where(d => d.Id == item.Id && d.Version == item.Version)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, ExternalProjectDraftStatus.Failed)
                        .SetProperty(d => d.ErrorCode, "EXTRACTION_RETRY_EXHAUSTED").SetProperty(d => d.Version, d => d.Version + 1)
                        .SetProperty(d => d.UpdatedAt, started).SetProperty(d => d.LeaseToken, (Guid?)null)
                        .SetProperty(d => d.LeaseExpiresAt, (DateTimeOffset?)null), ct);
                continue;
            }
            var claimed = await db.ExternalProjectDrafts.Where(d => d.Id == item.Id && d.Version == item.Version &&
                d.Status == ExternalProjectDraftStatus.Processing && d.ExpiresAt > started)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LeaseToken, token).SetProperty(d => d.LeaseExpiresAt, started.AddMinutes(10))
                    .SetProperty(d => d.AttemptCount, d => d.AttemptCount + 1).SetProperty(d => d.Version, d => d.Version + 1), ct);
            if (claimed == 0) continue;
            db.ChangeTracker.Clear();
            try { await extraction.ExtractAsync(item.Id, item.AccountId, ct, token); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (DbUpdateConcurrencyException) { /* Another edit/lease won; never overwrite it. */ }
            catch (ProjectExtractionException ex)
            {
                logger.LogWarning("Draft {DraftId} extraction failed: {Code}", item.Id, ex.Code);
                if (ex.Transient && item.AttemptCount + 1 < 3)
                {
                    var retryAt = DateTimeOffset.UtcNow.AddMinutes(Math.Pow(2, item.AttemptCount));
                    await db.ExternalProjectDrafts.Where(d => d.Id == item.Id && d.LeaseToken == token && d.Status == ExternalProjectDraftStatus.Failed)
                        .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, ExternalProjectDraftStatus.Processing)
                            .SetProperty(d => d.NextAttemptAt, retryAt)
                            .SetProperty(d => d.Version, d => d.Version + 1), ct);
                }
                else
                    await FailUnfinished(item.Id, token, ex.Code, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Draft {DraftId} extraction stopped unexpectedly", item.Id);
                await FailUnfinished(item.Id, token, "EXTRACTION_WORKER_ERROR", ct);
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    await db.ExternalProjectDrafts.Where(d => d.Id == item.Id && d.LeaseToken == token)
                        .ExecuteUpdateAsync(s => s.SetProperty(d => d.LeaseToken, (Guid?)null)
                            .SetProperty(d => d.LeaseExpiresAt, (DateTimeOffset?)null), ct);
                db.ChangeTracker.Clear();
            }
        }
    }

    private Task<int> FailUnfinished(Guid id, Guid token, string code, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return db.ExternalProjectDrafts.Where(d => d.Id == id && d.LeaseToken == token && d.Status == ExternalProjectDraftStatus.Processing)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, ExternalProjectDraftStatus.Failed).SetProperty(d => d.ErrorCode, code)
                .SetProperty(d => d.UpdatedAt, now).SetProperty(d => d.Version, d => d.Version + 1), ct);
    }
}
