using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class EvidenceModerationSource(UniNetDbContext db, IConfiguration configuration)
{
    public static bool IsWriteConflict(Exception ex) => ex is DbUpdateConcurrencyException ||
        ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } ||
        ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } };

    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    public async Task<EvidenceSourceSnapshot> CaptureAsync(Guid projectId, CancellationToken ct)
    {
        // Keep cancelled tasks and latest reports in every review state. There is no soft-delete flag.
        var tasks = await db.ProjectTasks.AsNoTracking().Include(t => t.Reports).ThenInclude(r => r.Evidence)
            .Where(t => t.ProjectId == projectId).AsSplitQuery().ToListAsync(ct);
        return new(projectId, tasks.OrderBy(t => t.Id).Select(t => new EvidenceTaskSnapshot(t.Id, t.Status,
            t.Reports.GroupBy(r => r.SubmittedById).Select(g => g.OrderByDescending(r => r.Version).First())
                .OrderBy(r => r.SubmittedById).Select(r => new EvidenceReportSnapshot(r.Id, r.Version,
                    r.SubmittedById, r.Content, r.Status, r.SubmittedAt,
                    r.Evidence.OrderBy(e => e.Id).Select(e => new EvidenceFileSnapshot(e.Id, e.FileUrl,
                        e.PublicId, e.ResourceType, e.FileName, e.Format)).ToList())).ToList())).ToList());
    }

    public EvidencePolicySnapshot CapturePolicy()
    {
        var s = configuration.GetSection("EvidenceModeration");
        return new(s["PolicyVersion"], s["ContentInstructions"], Values(s.GetSection("ForbiddenTerms")),
            s["TermMatchMode"], s["TextNormalization"], bool.TryParse(s["CaseSensitive"], out var b) ? b : null,
            configuration["Gemini:Model"] ?? "gemini-3.1-flash-lite",
            Values(configuration.GetSection("SafeBrowsing:ThreatTypes")),
            Values(configuration.GetSection("SafeBrowsing:PlatformTypes")));
    }

    private static string[] Values(IConfiguration section) => section.GetChildren().Select(c => c.Value ?? "")
        .Order(StringComparer.Ordinal).ToArray();

    // Caller owns a Serializable transaction including the source write, invalidation and new snapshot.
    public async Task<ProjectEvidenceModeration?> RefreshAsync(Guid projectId, DateTimeOffset now,
        CancellationToken ct, bool createInitial = false)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("A source transaction is required.");
        if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId && p.Status == ProjectStatus.Completed, ct)) return null;
        var latest = await db.ProjectEvidenceModerations.OrderByDescending(m => m.Version)
            .FirstOrDefaultAsync(m => m.ProjectId == projectId, ct);
        // No automatic backfill of pre-existing completed projects in phases 1–2.
        if (latest == null && !createInitial) return null;
        var snapshot = JsonSerializer.Serialize(await CaptureAsync(projectId, ct));
        var policy = JsonSerializer.Serialize(CapturePolicy());
        var hash = Hash(snapshot); var policyHash = Hash(policy);
        if (latest != null && latest.SourceHash == hash && latest.PolicyHash == policyHash && latest.SupersededAt == null)
            return latest;
        if (latest != null)
        {
            latest.SupersededAt = now;
            latest.LeaseToken = null; latest.LeaseExpiresAt = null; latest.NextRetryAt = null;
            // Free the unique current-version index before inserting the next version.
            await db.SaveChangesAsync(ct);
        }
        var next = new ProjectEvidenceModeration
        {
            ProjectId = projectId, Version = (latest?.Version ?? 0) + 1, SourceHash = hash, SnapshotJson = snapshot,
            PolicyHash = policyHash, PolicyJson = policy, CreatedAt = now, Status = EvidenceModerationStatus.Pending
        };
        db.ProjectEvidenceModerations.Add(next);
        await db.SaveChangesAsync(ct);
        return next;
    }
}
