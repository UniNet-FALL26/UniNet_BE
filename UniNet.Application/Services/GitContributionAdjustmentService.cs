using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
namespace UniNet.Application.Services;
public sealed class GitContributionAdjustmentService(UniNetDbContext db, OfficialContributionService official, IOptions<GitSyncOptions> options, IOptions<GitHubAppOptions> appOptions, IConfiguration config)
{
    private static ProjectException Error(string code, string message, int status = 409) => new(code, message, status);
    private static void Check(bool ok, string code, string message, int status = 409) { if (!ok) throw Error(code, message, status); }
    private bool ExecutionAvailable => config.GetValue("BackgroundWorkers:Enabled", true) &&
        (options.Value.Enabled || options.Value.AdjustmentExecutionEnabled && !options.Value.ScopedManual.Enabled);
    private async Task<bool> Schema(CancellationToken ct) => await db.Database.SqlQuery<int>($"SELECT CASE WHEN to_regclass('public.\"GitContributionAdjustments\"') IS NOT NULL THEN 1 ELSE 0 END AS \"Value\"").SingleAsync(ct) == 1;
    private async Task<Guid> Authorize(Guid project, Guid account, CancellationToken ct)
    {
        var actor = await ProjectGitAuthorization.RequireManagerAsync(db, project, account, ct);
        Check(await db.Projects.AnyAsync(x => x.Id == project && x.CreatorId == actor, ct), "FORBIDDEN", "Only Creator manages combined adjustments.", 403);
        Check(await db.Set<ProjectContributionWorkflow>().AnyAsync(x => x.ProjectId == project && x.Mode == "OfficialV2", ct), "OFFICIAL_WORKFLOW_NOT_ENABLED", "Select OfficialV2."); return actor;
    }
    private async Task LockProject(Guid project, CancellationToken ct) => await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Projects\" WHERE \"Id\"={project} FOR UPDATE").ToListAsync(ct);
    private async Task<GitContributionAdjustment> Load(Guid project, Guid id, CancellationToken ct) => await db.Set<GitContributionAdjustment>().SingleOrDefaultAsync(x => x.Id == id && x.ProjectId == project, ct) ?? throw Error("ADJUSTMENT_NOT_FOUND", "Adjustment not found.", 404);
    public async Task<CombinedAdjustmentResponse> LatestAsync(Guid project, Guid account, CancellationToken ct)
    {
        await Authorize(project, account, ct);
        if (!await Schema(ct)) return new(false, false, null, project, "Unavailable", 0, null, null, null, null, null, null, null, [], ["ADJUSTMENT_SCHEMA_REQUIRED"], "CommitterDateRangeNotProjectCompletion");
        var row = await db.Set<GitContributionAdjustment>().AsNoTracking().Where(x => x.ProjectId == project).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        return row is null ? new(true, ExecutionAvailable, null, project, "NotStarted", 0, null, null, null, null, null, null, null, [], ExecutionAvailable ? [] : ["ADJUSTMENT_EXECUTION_DISABLED"], "CommitterDateRangeNotProjectCompletion") : await Describe(row, account, ct);
    }
    public async Task<CombinedAdjustmentResponse> ReadAsync(Guid project, Guid id, Guid account, CancellationToken ct) { await Authorize(project, account, ct); return await Describe(await Load(project, id, ct), account, ct); }
    public async Task<CombinedAdjustmentResponse> ByRequestAsync(Guid project, Guid request, Guid account, CancellationToken ct)
    {
        await Authorize(project, account, ct);
        if (!await Schema(ct)) return new(false, false, null, project, "Unavailable", 0, null, null, null, null, null, null, null, [], ["ADJUSTMENT_SCHEMA_REQUIRED"], "CommitterDateRangeNotProjectCompletion");
        var row = await db.Set<GitContributionAdjustment>().AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == project && x.RequestId == request, ct);
        return row is null ? new(true, ExecutionAvailable, null, project, "NotStarted", 0, null, null, null, null, null, null, null, [], [], "CommitterDateRangeNotProjectCompletion", request) : await Describe(row, account, ct);
    }
    public Task<CombinedAdjustmentResponse> StartAsync(Guid project, Guid account, CombinedAdjustmentStartRequest request, CancellationToken ct) => Conflict(async () =>
    {
        var actor = await Authorize(project, account, ct); Check(await Schema(ct), "ADJUSTMENT_SCHEMA_REQUIRED", "Deploy adjustment schema.", 503);
        Check(request.RequestId != Guid.Empty, "ADJUSTMENT_REQUEST_REQUIRED", "RequestId required.", 400);
        Check(ExecutionAvailable, "ADJUSTMENT_EXECUTION_DISABLED", "Configure an isolated execution consumer before requesting synchronization.", 503);
        Check(!options.Value.ScopedManual.Enabled, "ADJUSTMENT_PINNED_RUNTIME", "A manually pinned consumer cannot accept a combined plan.", 503);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await LockProject(project, ct);
        await Authorize(project, account, ct); Check(await db.Projects.AnyAsync(x => x.Id == project && x.Status == ProjectStatus.Completed, ct), "OFFICIAL_PROJECT_NOT_COMPLETED", "Complete project first.");
        var existing = await db.Set<GitContributionAdjustment>().SingleOrDefaultAsync(x => x.ProjectId == project && x.RequestId == request.RequestId, ct);
        if (existing is not null) { var replay = await Describe(existing, account, ct); await tx.CommitAsync(ct); return replay; }
        var active = await db.Set<GitContributionAdjustment>().Where(x => x.ProjectId == project && x.State != "Published" && x.State != "Failed").ToArrayAsync(ct);
        foreach (var old in active)
        {
            if (old.RunId.HasValue && await db.Set<OfficialContributionRelease>().AnyAsync(x => x.RunId == old.RunId, ct)) { old.State = "Published"; old.Revision++; }
            else throw Error("ADJUSTMENT_ACTIVE", "Finish the current adjustment before starting another.");
        }
        if (active.Length > 0) await db.SaveChangesAsync(ct); // Release the one-active-plan index before inserting its successor, within this transaction.
        var links = await db.ProjectGitRepositories.AsNoTracking().Where(x => x.ProjectId == project && x.UnlinkedAt == null).OrderBy(x => x.Id).ToArrayAsync(ct);
        Check(links.Length is > 0 and <= 20, "ADJUSTMENT_SCOPE_INVALID", "Select between one and twenty linked repository associations.");
        foreach (var l in links) await RequireAssociation(l, ct);
        var ids = links.Select(x => x.Id).ToArray();
        Check(!await db.GitSyncJobs.AnyAsync(x => ids.Contains(x.ProjectGitRepositoryId) && (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.Running || x.Status == GitSyncJobStatus.RetryScheduled || x.Status == GitSyncJobStatus.Paused), ct), "ADJUSTMENT_SYNC_OVERLAP", "Resolve current repository jobs before requesting a new plan.");
        var now = DateTimeOffset.UtcNow; var current = await db.Set<OfficialContributionRelease>().AsNoTracking().Where(x => x.ProjectId == project).OrderByDescending(x => x.Version).Select(x => (Guid?)x.RunId).FirstOrDefaultAsync(ct);
        var row = new GitContributionAdjustment { ProjectId = project, RequestId = request.RequestId, CreatedById = actor, CreatedAt = now, ExecutionDeadline = now.AddMinutes(15), BasePublishedRunId = current }; db.Add(row); await db.SaveChangesAsync(ct);
        foreach (var l in links)
        {
            // Only the already-authorized association is passed to the existing Manual job API.
            var scoped = Scope(project, l.Id, null, options.Value);
            var jobs = new GitSyncJobService(db, Options.Create(scoped), appOptions);
            var result = await jobs.RequestManualSyncAsync(account, project, l.Id, ct);
            Check(result.Job.SyncType == GitSyncType.Manual && !result.WasAlreadyQueued, "ADJUSTMENT_SYNC_OVERLAP", "A concurrent job changed the requested plan.");
            db.Add(new GitContributionAdjustmentRepository { AdjustmentId = row.Id, ProjectId = project, AssociationId = l.Id, RepositoryId = l.GitHubRepositoryId, JobId = result.Job.Id });
        }
        await db.SaveChangesAsync(ct); var response = await Describe(row, account, ct); await tx.CommitAsync(ct); return response;
    });
    public static GitSyncOptions Scope(Guid project, Guid association, Guid? job, GitSyncOptions original, int timeout = 900) => new() { Enabled = false, ManualEnqueueEnabled = true, PollSeconds = 1, MaxRetryAttempts = original.MaxRetryAttempts, RetryBaseDelaySeconds = original.RetryBaseDelaySeconds, JobLeaseDurationSeconds = original.JobLeaseDurationSeconds, BatchSize = original.BatchSize, ScopedManual = new() { Enabled = true, ProjectId = project, AssociationId = association, JobId = job, ExecutionTimeoutSeconds = Math.Clamp(timeout, 1, 900) } };
    private async Task RequireAssociation(ProjectGitRepository link, CancellationToken ct)
    {
        Check(link.UnlinkedAt == null && link.AccessStatus == ProjectGitRepositoryAccessStatus.Active && !link.SyncPaused && link.CredentialSource == GitRepositoryCredentialSource.GitHubApp, "ADJUSTMENT_AUTHORIZATION_REQUIRED", "Restore and activate repository App access before combined synchronization.");
        var valid = await (from g in db.Set<GitHubAppRepositoryGrant>() join i in db.Set<GitHubAppInstallation>() on g.InstallationId equals i.Id join r in db.GitHubRepositories on link.GitHubRepositoryId equals r.Id where g.Id == link.GitHubAppRepositoryGrantId && g.ProviderRepositoryId == r.ProviderRepositoryId && g.RevokedAt == null && i.Status == GitHubAppInstallationStatus.Active && !i.VerificationUnknown select g.Id).AnyAsync(ct);
        Check(valid, "ADJUSTMENT_AUTHORIZATION_REQUIRED", "Repository authorization unavailable.");
    }
    private async Task<bool> Checkpoints(GitSyncJob job, CancellationToken ct)
    {
        if (job.Status != GitSyncJobStatus.Completed || job.CompletedAt is null) return false;
        var cp = await db.GitSyncCheckpoints.AsNoTracking().Where(x => x.SyncJobId == job.Id).ToArrayAsync(ct);
        if (cp.Length == 0 || cp.Any(x => x.Status != GitSyncCheckpointStatus.Completed || x.LastProcessedAt is null || x.LastProcessedAt > job.CompletedAt)) return false;
        if (new[] { "branches", "pull-requests", "issues", "releases" }.Any(c => !cp.Any(x => x.Category == c && x.ScopeKey == ""))) return false;
        var obs = await db.ProjectEvidenceObservations.AsNoTracking().Where(x => x.GitSyncJobId == job.Id).ToArrayAsync(ct);
        if (cp.Any(x => x.ItemsProcessed > 0 && !obs.Any(o => o.Category == x.Category && o.ScopeKey == x.ScopeKey))) return false;
        var branches = await (from o in db.ProjectEvidenceObservations join v in db.GitEvidenceVersions on o.GitEvidenceVersionId equals v.Id join p in db.GitHubProviderObjects on v.GitHubProviderObjectId equals p.Id where o.GitSyncJobId == job.Id && o.Category == "branches" select p.ExternalObjectId).ToArrayAsync(ct);
        if (!branches.All(b => cp.Any(x => x.Category == "commits" && x.ScopeKey == b))) return false;
        var rootFacts = await (from o in db.ProjectEvidenceObservations
                               join v in db.GitEvidenceVersions on o.GitEvidenceVersionId equals v.Id
                               where o.GitSyncJobId == job.Id && (o.Category == "pull-requests" || o.Category == "issues")
                               select v.FrozenFactsJson).ToArrayAsync(ct);
        foreach (var json in rootFacts)
        {
            GitFrozenFacts? facts;
            try { facts = System.Text.Json.JsonSerializer.Deserialize<GitFrozenFacts>(json); }
            catch (System.Text.Json.JsonException) { return false; }
            var number = facts?.Category == "pull-requests" ? facts.Details?.RelatedPullRequestNumber : facts?.Details?.RelatedIssueNumber;
            if (number is null or <= 0) return false;
            var categories = facts!.Category == "pull-requests" ? new[] { "pull-request-commits", "pull-request-files", "pull-request-reviews", "pull-request-review-comments" } : new[] { "issue-comments", "issue-events" };
            if (categories.Any(c => !cp.Any(x => x.Category == c && x.ScopeKey == number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))) return false;
        }
        return true;
    }
    private async Task<CombinedAdjustmentResponse> Describe(GitContributionAdjustment row, Guid account, CancellationToken ct)
    {
        var items = await db.Set<GitContributionAdjustmentRepository>().AsNoTracking().Where(x => x.AdjustmentId == row.Id).OrderBy(x => x.AssociationId).ToArrayAsync(ct); var result = new List<CombinedAdjustmentRepository>(); var blockers = new List<string>();
        foreach (var item in items)
        {
            var link = await db.ProjectGitRepositories.AsNoTracking().SingleAsync(x => x.Id == item.AssociationId && x.ProjectId == row.ProjectId, ct);
            var job = await db.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == item.JobId && x.ProjectGitRepositoryId == item.AssociationId && x.GitHubRepositoryId == item.RepositoryId, ct);
            var repo = await db.GitHubRepositories.AsNoTracking().SingleAsync(x => x.Id == item.RepositoryId, ct);
            try { await RequireAssociation(link, ct); } catch (ProjectException) { blockers.Add("ADJUSTMENT_AUTHORIZATION_REQUIRED"); }
            var complete = await Checkpoints(job, ct); if (job.Status == GitSyncJobStatus.Completed && !complete) blockers.Add("ADJUSTMENT_CHECKPOINTS_INCOMPLETE");
            if (job.Status is GitSyncJobStatus.Failed or GitSyncJobStatus.Cancelled or GitSyncJobStatus.Paused) blockers.Add("ADJUSTMENT_SYNC_FAILED");
            var obs = db.ProjectEvidenceObservations.AsNoTracking().Where(x => x.GitSyncJobId == job.Id);
            var versions = await obs.Select(x => x.GitEvidenceVersionId).Distinct().ToArrayAsync(ct);
            var old = await db.ProjectEvidenceObservations.AsNoTracking().Where(x => x.ProjectGitRepositoryId == link.Id && x.GitSyncJobId != job.Id && x.ObservedAt < row.CreatedAt).Select(x => x.GitEvidenceVersionId).Distinct().ToArrayAsync(ct);
            result.Add(new(link.Id, repo.FullName, job.Id, job.Status.ToString(), job.ErrorCode, await obs.LongCountAsync(ct), versions.Except(old).LongCount(), complete));
        }
        var state = row.State;
        if (row.RunId.HasValue && await db.Set<OfficialContributionRelease>().AnyAsync(x => x.RunId == row.RunId, ct)) state = "Published";
        else if (row.State == "Syncing")
        {
            if (blockers.Count > 0) state = "Failed";
            else if (result.Count > 0 && result.All(x => x.JobStatus == "Completed" && x.CheckpointsComplete)) state = "SyncCompleted";
            else if (DateTimeOffset.UtcNow > row.ExecutionDeadline) { state = "Failed"; blockers.Add("ADJUSTMENT_DEADLINE_EXCEEDED"); }
        }
        var peer = row.PeerCohortId.HasValue ? await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().Where(x => x.CohortId == row.PeerCohortId && x.ProjectId == row.ProjectId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct) : null;
        var current = await db.Set<OfficialContributionRelease>().AsNoTracking().Where(x => x.ProjectId == row.ProjectId).OrderByDescending(x => x.Version).Select(x => (Guid?)x.RunId).FirstOrDefaultAsync(ct);
        return new(true, ExecutionAvailable, row.Id, row.ProjectId, state, row.Revision, row.CreatedAt, row.ExecutionDeadline, row.GitSnapshotId, row.PeerCohortId, peer, row.RunId, current, result, blockers.Distinct().ToArray(), "CommitterDateRangeNotProjectCompletion", row.RequestId, row.State == "Failed",row.GitSnapshotId.HasValue?await official.CompareGitAsync(row.ProjectId,row.GitSnapshotId.Value,row.BasePublishedRunId,account,ct):"NotEvaluated");
    }
    private async Task Fresh(GitContributionAdjustment row, Guid account, CancellationToken ct)
    {
        var read = await Describe(row, account, ct); Check(read.State == "SyncCompleted" && read.Blockers.Count == 0, "ADJUSTMENT_SYNC_NOT_COMPLETE", "Every requested repository must complete with checkpoint proof before capture.");
        var items = await db.Set<GitContributionAdjustmentRepository>().AsNoTracking().Where(x => x.AdjustmentId == row.Id).ToArrayAsync(ct); var ids = items.Select(x => x.AssociationId).ToArray();
        var jobs = items.Select(x => x.JobId).ToArray();
        Check(!await db.GitSyncJobs.AnyAsync(x => ids.Contains(x.ProjectGitRepositoryId) && !jobs.Contains(x.Id) && x.CreatedAt >= row.CreatedAt, ct), "ADJUSTMENT_NEWER_SYNC", "A later synchronization changed the receipt context; start a new adjustment after resolving jobs.");
        var current = await db.ProjectGitRepositories.AsNoTracking().Where(x => x.ProjectId == row.ProjectId && x.UnlinkedAt == null).Select(x => x.Id).ToArrayAsync(ct);
        Check(ids.ToHashSet().SetEquals(current), "ADJUSTMENT_SCOPE_CHANGED", "Linked repository scope changed; do not silently mix attempts.");
        Check(!await db.GitSyncJobs.AnyAsync(x => ids.Contains(x.ProjectGitRepositoryId) && (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.Running || x.Status == GitSyncJobStatus.RetryScheduled || x.Status == GitSyncJobStatus.Paused), ct), "ADJUSTMENT_SYNC_UNRESOLVED", "Wait for all repository synchronization to finish.");
    }
    public Task<CombinedAdjustmentResponse> AbandonAsync(Guid project, Guid id, Guid account, CancellationToken ct) => Conflict(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockProject(project, ct); await Authorize(project, account, ct); var row = await Load(project, id, ct);
        Check(row.PeerCohortId is null && row.RunId is null, "ADJUSTMENT_PEER_ALREADY_OPEN", "Finish the existing Peer workflow before starting another; frozen inputs are retained.");
        var ids = await db.Set<GitContributionAdjustmentRepository>().Where(x => x.AdjustmentId == id).Select(x => x.JobId).OrderBy(x => x).ToArrayAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var jobId in ids)
        {
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"GitSyncJobs\" WHERE \"Id\"={jobId} FOR UPDATE").ToListAsync(ct);
            var job = await db.GitSyncJobs.SingleAsync(x => x.Id == jobId, ct);
            Check(job.Status != GitSyncJobStatus.Running || job.LeaseExpiresAt is null || job.LeaseExpiresAt <= now, "ADJUSTMENT_LEASE_ACTIVE", "Wait for the active consumer to stop or its lease to expire before abandoning.");
            if (job.Status is GitSyncJobStatus.Pending or GitSyncJobStatus.RetryScheduled or GitSyncJobStatus.Paused or GitSyncJobStatus.Running)
            { job.Status = GitSyncJobStatus.Cancelled; job.LeaseId = null; job.LeaseExpiresAt = null; job.NextAttemptAt = null; job.CompletedAt = now; job.UpdatedAt = now; job.ErrorCode = "ADJUSTMENT_ABANDONED"; job.ErrorMessage = "Creator ended the scoped adjustment before capture."; }
        }
        if (row.State != "Failed") { row.State = "Failed"; row.Revision++; await db.SaveChangesAsync(ct); }
        var response = await Describe(row, account, ct); await tx.CommitAsync(ct); return response;
    });
    public Task<CombinedAdjustmentResponse> CaptureAsync(Guid project, Guid id, Guid account, CancellationToken ct) => Conflict(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await LockProject(project, ct); await Authorize(project, account, ct); var row = await Load(project, id, ct);
        if (row.GitSnapshotId is null)
        {
            await Fresh(row, account, ct);
            // Same persisted read point locks association/auth rows before immutable capture.
            var ids = await db.Set<GitContributionAdjustmentRepository>().Where(x => x.AdjustmentId == id).Select(x => x.AssociationId).OrderBy(x => x).ToArrayAsync(ct);
            foreach (var association in ids) await db.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM "ProjectGitRepositories" l JOIN "GitHubRepositories" r ON r."Id"=l."GitHubRepositoryId"
                JOIN "GitHubAppRepositoryGrants" g ON g."Id"=l."GitHubAppRepositoryGrantId"
                JOIN "GitHubAppInstallations" i ON i."Id"=g."InstallationId" WHERE l."Id"={association} FOR SHARE OF l,r,g,i
                """).ToListAsync(ct);
            await Fresh(row, account, ct);
            var cutoff = DateTimeOffset.UtcNow;
            Check(!await db.ProjectGitMemberIdentityHistory.AnyAsync(x => x.ProjectId == project && x.OccurredAt > cutoff, ct), "ADJUSTMENT_IDENTITY_CLOCK", "Identity history has future timestamp.");
            row.GitSnapshotId = await official.CaptureAsync(project, account, new(cutoff), ct); row.State = "GitCaptured"; row.Revision++; await db.SaveChangesAsync(ct);
        }
        var response = await Describe(row, account, ct); await tx.CommitAsync(ct); return response;
    });
    public Task<CombinedAdjustmentResponse> OpenPeerAsync(Guid project, Guid id, Guid account, CancellationToken ct) => Conflict(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await LockProject(project, ct); await Authorize(project, account, ct); var row = await Load(project, id, ct);
        Check(row.State!="Failed","ADJUSTMENT_ENDED","This adjustment was ended; start a new attempt.");
        Check(row.GitSnapshotId.HasValue, "ADJUSTMENT_GIT_SNAPSHOT_REQUIRED", "Capture the new Git snapshot for this adjustment first.");
        if (row.PeerCohortId is null) { var readiness = await official.ReadinessAsync(project, row.GitSnapshotId.Value, account, ct); Check(readiness.GitState == "Ready", "OFFICIAL_CALCULATION_NOT_READY", "Frozen Git evidence is not Ready."); var roster = await ProjectMembership.EffectiveMemberIds(db, project).ToArrayAsync(ct); var peer = await official.OpenPeerAsync(project, account, new(roster), ct); row.PeerCohortId = peer.Id; row.State = "PeerOpen"; row.Revision++; await db.SaveChangesAsync(ct); }
        var response = await Describe(row, account, ct); await tx.CommitAsync(ct); return response;
    });
    public Task<CombinedAdjustmentResponse> CreateRunAsync(Guid project, Guid id, Guid account, CancellationToken ct) => Conflict(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await LockProject(project, ct); await Authorize(project, account, ct); var row = await Load(project, id, ct);
        Check(row.GitSnapshotId.HasValue && row.PeerCohortId.HasValue, "ADJUSTMENT_INPUTS_REQUIRED", "New Git snapshot and new Peer round required.");
        if (row.RunId is null) { var peer = await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().SingleOrDefaultAsync(x => x.CohortId == row.PeerCohortId && x.ProjectId == project, ct) ?? throw Error("PEER_SNAPSHOT_REQUIRED", "Close this adjustment's Peer round."); var run = await official.CreateAsync(project, account, new(row.RequestId, row.GitSnapshotId.Value, peer.Id), ct); row.RunId = run.Id; row.State = "DraftCreated"; row.Revision++; await db.SaveChangesAsync(ct); }
        var response = await Describe(row, account, ct); await tx.CommitAsync(ct); return response;
    });
    private static async Task<T> Conflict<T>(Func<Task<T>> body) { try { return await body(); } catch (Exception e) when (e.GetBaseException() is Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" }) { throw Error("ADJUSTMENT_CONFLICT", "Reload the durable adjustment before retrying the same request."); } }
}
