using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class EvidenceModerationTasks(UniNetDbContext db, IConfiguration configuration,
    EvidenceModerationChecks checks, VerificationNotifications notifications)
{
    // Reuse existing verification retry budget/backoff and project moderation's abandoned-work interval.
    private const int MaxAttempts = 5;

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var projects = await db.ProjectEvidenceModerations.AsNoTracking().Where(m => m.SupersededAt == null)
            .Select(m => m.ProjectId).ToListAsync(ct);
        foreach (var projectId in projects)
        {
            db.ChangeTracker.Clear();
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await new EvidenceModerationSource(db, configuration).RefreshAsync(projectId, now, ct);
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) when (EvidenceModerationSource.IsWriteConflict(ex)) { db.ChangeTracker.Clear(); }
        }
        var ids = await db.ProjectEvidenceModerations.AsNoTracking().Where(m => m.SupersededAt == null &&
                (m.Status == EvidenceModerationStatus.Pending ||
                 (m.Status == EvidenceModerationStatus.Error && m.NextRetryAt != null && m.NextRetryAt <= now) ||
                 (m.Status == EvidenceModerationStatus.Processing && m.LeaseExpiresAt <= now)))
            .OrderBy(m => m.CreatedAt).Select(m => m.Id).Take(25).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            try { await ProcessAsync(id, now, ct); }
            catch (Exception ex) when (EvidenceModerationSource.IsWriteConflict(ex)) { db.ChangeTracker.Clear(); }
        }
    }

    private async Task ProcessAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        EvidenceSourceSnapshot source; EvidencePolicySnapshot policy;
        Guid attemptId; Guid token;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var m = await db.ProjectEvidenceModerations.Include(m => m.Attempts).SingleAsync(m => m.Id == id, ct);
            if (m.SupersededAt != null) return;
            if (m.Status == EvidenceModerationStatus.Processing)
            {
                if (m.LeaseExpiresAt > now) return;
                var abandoned = m.Attempts.Single(a => a.Number == m.AttemptCount);
                abandoned.FinishedAt = now; abandoned.ErrorCode = "WORKER_LEASE_EXPIRED";
                abandoned.Error = "Worker stopped before persisting its result."; abandoned.Transient = true;
                m.LeaseToken = null; m.LeaseExpiresAt = null;
                await FailAsync(m, abandoned.ErrorCode, abandoned.Error, true, now, ct);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return;
            }
            if (m.Status != EvidenceModerationStatus.Pending &&
                !(m.Status == EvidenceModerationStatus.Error && m.NextRetryAt != null && m.NextRetryAt <= now)) return;
            if (m.AttemptCount >= MaxAttempts) return;
            source = JsonSerializer.Deserialize<EvidenceSourceSnapshot>(m.SnapshotJson)!;
            policy = JsonSerializer.Deserialize<EvidencePolicySnapshot>(m.PolicyJson)!;
            var startedAt = DateTimeOffset.UtcNow > now ? DateTimeOffset.UtcNow : now;
            token = Guid.NewGuid(); m.LeaseToken = token; m.LeaseExpiresAt = startedAt.AddMinutes(10);
            m.Status = EvidenceModerationStatus.Processing; m.NextRetryAt = null; m.CompletedAt = null;
            var attempt = new EvidenceModerationAttempt { ModerationId = id, Number = ++m.AttemptCount, StartedAt = startedAt };
            attemptId = attempt.Id; db.EvidenceModerationAttempts.Add(attempt);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }

        var results = new List<EvidenceCheckResult>(); VerificationAiException? problem = null;
        var hasEvidence = source.Tasks.SelectMany(t => t.Reports).Any(r => r.Evidence.Count > 0);
        try
        {
            if (hasEvidence)
            {
                EvidenceModerationChecks.ValidatePolicy(policy);
                results = await checks.RunAsync(source, policy, ct);
            }
        }
        catch (VerificationAiException ex) { problem = ex; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        { problem = new("Evidence checker failed unexpectedly; operator review is required.", false, "EVIDENCE_CHECK_ERROR"); }

        db.ChangeTracker.Clear();
        await using var persistence = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await db.ProjectEvidenceModerations.SingleAsync(m => m.Id == id, ct);
        var finished = await db.EvidenceModerationAttempts.SingleAsync(a => a.Id == attemptId, ct);
        // Re-read the source and current policy in the same transaction as publishing the verdict.
        await new EvidenceModerationSource(db, configuration).RefreshAsync(current.ProjectId, DateTimeOffset.UtcNow, ct);
        if (finished.FinishedAt != null) return; // A newer lease already recovered this attempt.
        finished.FinishedAt = DateTimeOffset.UtcNow > now ? DateTimeOffset.UtcNow : now;
        finished.ResultsJson = JsonSerializer.Serialize(results);
        if (current.SupersededAt != null || current.LeaseToken != token)
        {
            finished.ErrorCode = "SOURCE_SUPERSEDED"; finished.Error = "Result retained for audit, not applied to the current source.";
        }
        else
        {
            current.LeaseToken = null; current.LeaseExpiresAt = null;
            var errors = results.Where(r => r.Status == EvidenceCheckStatus.Error).ToList();
            if (problem == null && errors.Count > 0)
                problem = new(errors[0].Reason, errors.All(r => r.Transient), errors[0].ErrorCode ?? "EVIDENCE_CHECK_ERROR");
            if (problem != null)
            {
                finished.ErrorCode = problem.Code; finished.Error = problem.Message; finished.Transient = problem.Transient;
                await FailAsync(current, problem.Code, problem.Message, problem.Transient, finished.FinishedAt.Value, ct);
            }
            else
            {
                var required = results.Where(r => r.Kind != "FileContent").ToList();
                current.Status = !hasEvidence || required.Count == 0 || required.Any(r => r.Status == EvidenceCheckStatus.NotChecked)
                    ? EvidenceModerationStatus.NeedsEvidence
                    : required.Any(r => r.Status == EvidenceCheckStatus.Rejected)
                        ? EvidenceModerationStatus.Rejected : EvidenceModerationStatus.Passed;
                current.Summary = current.Status switch
                {
                    EvidenceModerationStatus.NeedsEvidence => "Missing evidence or report text; no safety verdict was granted.",
                    EvidenceModerationStatus.Rejected => "Report text or a URL violated the configured policy. File content was not inspected.",
                    _ => "Report text and evidence/report URLs passed the configured checks. File content was not inspected."
                };
                current.LastError = null; current.LastErrorCode = null; current.NextRetryAt = null;
                current.CompletedAt = finished.FinishedAt;
            }
        }
        await db.SaveChangesAsync(ct); await persistence.CommitAsync(ct);
    }

    private async Task FailAsync(ProjectEvidenceModeration m, string code, string message, bool transient,
        DateTimeOffset now, CancellationToken ct)
    {
        m.Status = EvidenceModerationStatus.Error; m.LastErrorCode = code; m.LastError = message;
        m.Summary = "Checks incomplete; no safety verdict was granted. File content was not inspected.";
        m.NextRetryAt = transient && m.AttemptCount < MaxAttempts
            ? now.AddMinutes(Math.Pow(2, m.AttemptCount - 1)) : null;
        m.CompletedAt = m.NextRetryAt == null ? now : null;
        if (m.NextRetryAt == null)
            await notifications.EvidenceAdminsAsync(m.Id, $"evidence-attention:{m.Id}",
                $"Project evidence moderation requires attention: {code}. {message}", now, ct);
    }

}
