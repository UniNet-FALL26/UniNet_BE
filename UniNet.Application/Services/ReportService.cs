using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ReportService(UniNetDbContext db) : IReportService
{
    public Task<ReportResponse> SubmitAsync(Guid accountId, SubmitReportRequest request,
        CancellationToken ct = default) => MutateAsync(async () =>
    {
        var actor = await ActorAsync(accountId, true, ct);
        var assignment = await db.VerificationAssignments.Include(a => a.ProjectVerification)
            .Include(a => a.Report).FirstOrDefaultAsync(a => a.Id == request.AssignmentId &&
                a.AssignedToUserId == actor.Id, ct)
            ?? throw new ReportException("ASSIGNMENT_NOT_FOUND", "Assignment not found.", 404);
        Require(assignment.Status is VerificationAssignmentStatus.Assigned or VerificationAssignmentStatus.Overdue &&
            assignment.ReplacedAt == null && assignment.ReplacedBy == null &&
            db.Entry(assignment).Property<Guid?>("ReplacedById").CurrentValue == null,
            "ASSIGNMENT_LOCKED", "Only active, unreplaced assignments allow report submission.", 409);
        Require(assignment.Report == null, "REPORT_EXISTS", "A report already exists. Create a new version instead.", 409);
        var verification = assignment.ProjectVerification;
        Require(verification.Status == ProjectVerificationStatus.Pending && verification.FinalizedAt == null,
            "VERIFICATION_LOCKED", "Only pending verifications allow initial report submission.", 409);
        Require(verification.MeetingStartAt.HasValue && verification.MeetingEndAt.HasValue &&
            verification.MeetingEndAt >= verification.MeetingStartAt && verification.MeetingEndAt <= DateTimeOffset.UtcNow,
            "MEETING_NOT_COMPLETED", "The meeting must end before submitting a report.", 409);
        Validate(request.Summary, request.Findings, request.Recommendations, request.Outcome);
        var now = DateTimeOffset.UtcNow;
        var report = new VerificationReport
        {
            Id = Guid.NewGuid(), VerificationId = assignment.Id, VerificationAssignment = assignment,
            Summary = request.Summary.Trim(), Findings = request.Findings.Trim(),
            Recommendations = request.Recommendations.Trim(), Outcome = request.Outcome,
            SubmittedByUserId = actor.Id, SubmittetAt = now, CreatedAt = now, UpdatedAt = now
        };
        db.VerificationReports.Add(report);
        Audit(report.Id, 1, "Submitted", actor.Id, now, null);
        assignment.Status = VerificationAssignmentStatus.Completed;
        assignment.CompletedAt = now;
        assignment.UpdatedAt = now;
        Finalize(verification, request.Outcome!.Value, actor.Id, now);
        return ReportMapping.ToResponse(report);
    }, ct);

    public async Task<ReportResponse> GetCurrentAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(accountId, false, ct);
        var report = await Reports().AsNoTracking().FirstOrDefaultAsync(r => r.VerificationId == assignmentId, ct)
            ?? throw NotFound();
        AuthorizeRead(report.VerificationAssignment, actor, false);
        var latest = await db.VerificationReportVersions.AsNoTracking().Include(v => v.Report)
            .ThenInclude(r => r.VerificationAssignment).Where(v => v.ReportId == report.Id)
            .OrderByDescending(v => v.Version).FirstOrDefaultAsync(ct);
        return latest == null ? ReportMapping.ToResponse(report) : ReportMapping.ToResponse(latest);
    }

    public async Task<ReportResponse> GetDetailAsync(Guid reportId, Guid accountId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(accountId, false, ct);
        var (report, version) = await ResolveAsync(reportId, ct);
        AuthorizeRead(report.VerificationAssignment, actor, false);
        return version == null ? ReportMapping.ToResponse(report) : ReportMapping.ToResponse(version);
    }

    public async Task<List<ReportResponse>> GetVersionsAsync(Guid reportId, Guid accountId, CancellationToken ct = default)
    {
        var actor = await ActorAsync(accountId, false, ct);
        var (report, _) = await ResolveAsync(reportId, ct);
        AuthorizeRead(report.VerificationAssignment, actor, true);
        var versions = await db.VerificationReportVersions.Include(v => v.Report)
            .ThenInclude(r => r.VerificationAssignment).Where(v => v.ReportId == report.Id)
            .OrderBy(v => v.Version).ToListAsync(ct);
        return new[] { ReportMapping.ToResponse(report) }.Concat(versions.Select(ReportMapping.ToResponse)).ToList();
    }

    public Task<ReportResponse> CreateVersionAsync(Guid reportId, Guid accountId, CreateReportVersionRequest request,
        CancellationToken ct = default) => MutateAsync(async () =>
    {
        var actor = await ActorAsync(accountId, true, ct);
        var (report, suppliedVersion) = await ResolveAsync(reportId, ct);
        var assignment = report.VerificationAssignment;
        Require(assignment.AssignedToUserId == actor.Id, "FORBIDDEN", "Only the assigned verificator can create a report version.", 403);
        Require(assignment.Status == VerificationAssignmentStatus.Completed && assignment.ReplacedAt == null,
            "ASSIGNMENT_LOCKED", "Only completed assignments allow report revisions.", 409);
        var verification = assignment.ProjectVerification;
        Require(verification.Status is ProjectVerificationStatus.Verified or ProjectVerificationStatus.Failed &&
            verification.FinalizedAt.HasValue, "VERIFICATION_LOCKED", "Only finalized verification reports can be revised.", 409);
        // Revising an earlier verification must not override a later verification cycle.
        Require(!await db.ProjectVerifications.AnyAsync(v => v.ProjectId == verification.ProjectId &&
            v.Id != verification.Id && v.CreatedAt >= verification.CreatedAt, ct),
            "VERIFICATION_SUPERSEDED", "A newer verification exists for this project.", 409);
        Validate(request.Summary, request.Findings, request.Recommendations, request.Outcome);
        Require(!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Trim().Length <= 2000,
            "INVALID_REVISION_REASON", "Revision reason must contain 1 to 2000 characters.");
        var latest = await db.VerificationReportVersions.Where(v => v.ReportId == report.Id)
            .MaxAsync(v => (int?)v.Version, ct) ?? 1;
        Require(suppliedVersion == null || suppliedVersion.Version == latest, "REPORT_VERSION_CONFLICT",
            "Create the revision from the latest report version.", 409);
        var now = DateTimeOffset.UtcNow;
        var version = new VerificationReportVersion
        {
            Id = Guid.NewGuid(), ReportId = report.Id, Report = report, Version = latest + 1,
            Summary = request.Summary.Trim(), Findings = request.Findings.Trim(),
            Recommendations = request.Recommendations.Trim(), Outcome = request.Outcome!.Value,
            Reason = request.Reason.Trim(), SubmittedByUserId = actor.Id, SubmittedAt = now
        };
        db.VerificationReportVersions.Add(version);
        Audit(report.Id, version.Version, "VersionCreated", actor.Id, now, version.Reason);
        Finalize(verification, version.Outcome, actor.Id, now);
        return ReportMapping.ToResponse(version);
    }, ct);

    private IQueryable<VerificationReport> Reports() => db.VerificationReports
        .Include(r => r.VerificationAssignment).ThenInclude(a => a.ProjectVerification).ThenInclude(v => v.Project);

    private async Task<(VerificationReport Report, VerificationReportVersion? Version)> ResolveAsync(Guid id, CancellationToken ct)
    {
        var report = await Reports().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report != null) return (report, null);
        var version = await db.VerificationReportVersions.Include(v => v.Report)
            .ThenInclude(r => r.VerificationAssignment).ThenInclude(a => a.ProjectVerification)
            .ThenInclude(v => v.Project).FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw NotFound();
        return (version.Report, version);
    }

    private async Task<UserProfile> ActorAsync(Guid accountId, bool write, CancellationToken ct)
    {
        var actor = await db.UserProfiles.Include(p => p.Account).FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new ReportException("FORBIDDEN", "An active account is required.", 403);
        Require(actor.Account.Status == AccountStatus.Active && (write
            ? actor.Account.Role == AccountRole.Verificator
            : actor.Account.Role is AccountRole.Verificator or AccountRole.Admin or AccountRole.Student or AccountRole.Partner),
            "FORBIDDEN", "Your account cannot access verification reports.", 403);
        return actor;
    }

    private static void AuthorizeRead(VerificationAssignment a, UserProfile actor, bool history) => Require(
        actor.Account.Role == AccountRole.Admin ||
        actor.Account.Role == AccountRole.Verificator && a.AssignedToUserId == actor.Id ||
        !history && (actor.Account.Role is AccountRole.Student or AccountRole.Partner) && a.ProjectVerification.Project.CreatorId == actor.Id,
        "FORBIDDEN", "You cannot access this report.", 403);

    private static void Validate(string summary, string findings, string recommendations, ProjectVerificationStatus? outcome)
    {
        Require(!string.IsNullOrWhiteSpace(summary) && summary.Trim().Length <= 10000,
            "INVALID_SUMMARY", "Summary must contain 1 to 10000 characters.");
        Require(!string.IsNullOrWhiteSpace(findings) && findings.Trim().Length <= 30000,
            "INVALID_FINDINGS", "Findings must contain 1 to 30000 characters.");
        Require(!string.IsNullOrWhiteSpace(recommendations) && recommendations.Trim().Length <= 10000,
            "INVALID_RECOMMENDATIONS", "Recommendations must contain 1 to 10000 characters.");
        Require(outcome is ProjectVerificationStatus.Verified or ProjectVerificationStatus.Failed,
            "INVALID_REPORT_OUTCOME", "Outcome must be Verified or Failed.");
    }

    private static void Finalize(ProjectVerification verification, ProjectVerificationStatus outcome, Guid actorId, DateTimeOffset now)
    {
        verification.Status = outcome;
        verification.FinalizedByUserId = actorId;
        verification.FinalizedAt = now;
        verification.UpdatedAt = now;
    }

    private async Task<ReportResponse> MutateAsync(Func<Task<ReportResponse>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await action();
            // Persist report, assignment, verification and audit entry atomically.
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } })
        {
            throw new ReportException("REPORT_CONFLICT", "Report data changed concurrently. Reload and try again.", 409);
        }
    }

    private void Audit(Guid reportId, int version, string action, Guid actorId, DateTimeOffset now, string? reason)
        => db.ReportAuditLogs.Add(new ReportAuditLog
        {
            Id = Guid.NewGuid(), ReportId = reportId, Version = version, Action = action,
            ActorUserId = actorId, OccurredAt = now, Reason = reason
        });

    private static ReportException NotFound() => new("REPORT_NOT_FOUND", "Report not found.", 404);
    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new ReportException(code, message, status);
    }
}
