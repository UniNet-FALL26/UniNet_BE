using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class VerificationAiTasks(UniNetDbContext db, IVerificationAiReviewer ai, VerificationNotifications notifications)
{
    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.ProjectVerifications.AsNoTracking().Where(v => v.Status == ProjectVerificationStatus.Pending &&
            v.FinalizedAt == null && v.EvidenceSubmissions.Any() &&
            (v.AiEvidenceVersion == null || v.AiEvidenceVersion < v.EvidenceSubmissions.Max(e => e.Version)) &&
            (v.AiAttemptEvidenceVersion == null || v.AiAttemptEvidenceVersion < v.EvidenceSubmissions.Max(e => e.Version) ||
             (v.AiAttemptCount < 5 && (v.AiNextRetryAt == null || v.AiNextRetryAt <= now))))
            .OrderBy(v => v.UpdatedAt).Select(v => v.Id).Take(25).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            int version;
            VerificationAiInput input;
            await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
            {
                var v = await db.ProjectVerifications.Include(v => v.Project).Include(v => v.EvidenceSubmissions).SingleAsync(v => v.Id == id, ct);
                var evidence = v.EvidenceSubmissions.MaxBy(e => e.Version)!;
                version = evidence.Version;
                if (v.Status != ProjectVerificationStatus.Pending || v.FinalizedAt != null || v.AiEvidenceVersion == version) continue;
                if (v.AiAttemptEvidenceVersion != version) { v.AiAttemptEvidenceVersion = version; v.AiAttemptCount = 0; v.AiNextRetryAt = null; }
                if (v.AiAttemptCount >= 5 || v.AiNextRetryAt > now) continue;
                v.AiAttemptCount++;
                v.AiNextRetryAt = now.AddMinutes(Math.Pow(2, v.AiAttemptCount - 1));
                v.UpdatedAt = now;
                input = new(v.Project.Title, v.Project.Description,
                    JsonSerializer.Deserialize<List<VerificationEvidenceRequest>>(evidence.EvidenceJson) ?? []);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            VerificationAiReview? result = null;
            VerificationAiException? problem = null;
            try { result = await ai.ReviewAsync(input, ct); }
            catch (VerificationAiException ex) { problem = ex; }
            db.ChangeTracker.Clear();
            await using var persistence = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var current = await db.ProjectVerifications.Include(v => v.EvidenceSubmissions).SingleAsync(v => v.Id == id, ct);
            if (current.Status != ProjectVerificationStatus.Pending || current.FinalizedAt != null ||
                current.EvidenceSubmissions.Max(e => e.Version) != version) continue;
            if (result != null)
            {
                current.AiSummary = result.Summary; current.AiScore = result.Score;
                current.AiAdditionalEvidenceJson = JsonSerializer.Serialize(result.AdditionalEvidenceRequired);
                current.AiEvidenceVersion = version; current.AiReviewedAt = now;
                current.AiLastError = null; current.AiNextRetryAt = null;
            }
            else
            {
                current.AiLastError = problem!.Message;
                if (!problem.Transient) current.AiAttemptCount = 5;
                if (current.AiAttemptCount >= 5)
                    await notifications.AdminsAsync(id, $"ai-exhausted:{id}:{version}", "AI pre-check needs attention: " + problem.Message, now, ct);
            }
            current.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await persistence.CommitAsync(ct);
        }
    }
}
