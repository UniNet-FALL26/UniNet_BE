using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed partial class OfficialContributionService
{
    private readonly UniNetDbContext db;
    private readonly GitExperimentalContributionService engine;
    private readonly ProjectGitEvidenceSnapshotService capture;
    private readonly ProjectGitEvidenceSnapshotReader reader;
    private readonly ExperimentalPeerCohortService peers;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal OfficialContributionService(UniNetDbContext db, GitExperimentalContributionService engine,
        ProjectGitEvidenceSnapshotService capture, ProjectGitEvidenceSnapshotReader reader, ExperimentalPeerCohortService peers)
        => (this.db, this.engine, this.capture, this.reader, this.peers) = (db, engine, capture, reader, peers);

    private static ProjectException Error(string code, string message, int status = 409) => new(code, message, status);
    private static void Check(bool ok, string code, string message, int status = 409) { if (!ok) throw Error(code, message, status); }
    private async Task<Guid> Actor(Guid project, Guid account, bool manage, CancellationToken ct)
    {
        if (db.Database.IsNpgsql() && db.Database.CurrentTransaction is not null)
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Accounts\" WHERE \"Id\"={account} FOR SHARE").ToListAsync(ct);
        var actor = await ProjectGitAuthorization.RequireProjectViewerAsync(db, project, account, ct);
        if (manage) Check(await db.Projects.AnyAsync(x => x.Id == project && x.CreatorId == actor, ct),
            "FORBIDDEN", "Only the project Creator may manage official Contribution.", 403);
        return actor;
    }
    private async Task RequireV2(Guid project, CancellationToken ct)
    {
        await RequireSchema(ct);
        var mode = await db.Set<ProjectContributionWorkflow>().AsNoTracking().Where(x => x.ProjectId == project).Select(x => x.Mode).SingleOrDefaultAsync(ct);
        Check(mode == "OfficialV2", "OFFICIAL_WORKFLOW_NOT_ENABLED", "Historical projects require an explicitly approved workflow selection; legacy results are preserved.");
    }
    public async Task<OfficialWorkflowResponse> StatusAsync(Guid project, Guid account, CancellationToken ct)
    {
        var actor = await Actor(project, account, false, ct);
        await RequireSchema(ct);
        var p = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == project, ct);
        var mode = await db.Set<ProjectContributionWorkflow>().AsNoTracking().Where(x => x.ProjectId == project).Select(x => x.Mode).SingleOrDefaultAsync(ct) ?? "RequiresOptIn";
        var blockers = new List<string>();
        if (mode != "OfficialV2") blockers.Add("HistoricalWorkflowSelectionRequired");
        if (p.Status != ProjectStatus.Completed) blockers.Add("ProjectNotCompleted");
        if (!await db.ProjectGitRepositories.AnyAsync(x => x.ProjectId == project && x.UnlinkedAt == null && x.AccessStatus == ProjectGitRepositoryAccessStatus.Active, ct)) blockers.Add("AuthorizedRepositoryRequired");
        if (!await db.ProjectGitMemberIdentities.AnyAsync(x => x.ProjectId == project && x.Status == ProjectGitMemberIdentityStatus.Active && x.VerifiedByConnectionId != null, ct)) blockers.Add("VerifiedGitIdentityRequired");
        var published = await db.Set<OfficialContributionRelease>().AsNoTracking().Where(x => x.ProjectId == project).OrderByDescending(x=>x.Version).Select(x => (Guid?)x.RunId).FirstOrDefaultAsync(ct);
        var manage = p.CreatorId == actor;
        var cohort = mode == "OfficialV2" ? await db.Set<ExperimentalPeerCohort>().AsNoTracking().Where(x => x.ProjectId == project)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct) : null;
        if (cohort is not null && !manage && !ExperimentalPeerCohortService.Roster(cohort).Contains(actor)) cohort = null;
        var rosterCount = cohort is null ? 0 : ExperimentalPeerCohortService.Roster(cohort).Length;
        return new("1", project, mode, manage, p.Status == ProjectStatus.Completed, blockers, published,
            manage && mode == "OfficialV2" ? await db.Set<ProjectGitEvidenceSnapshot>().AsNoTracking().Where(x => x.ProjectId == project && x.Finalized)
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) : null,
            cohort?.Id, cohort?.Status.ToString(), cohort is null ? null : await db.Set<ExperimentalPeerSubmission>().CountAsync(x => x.CohortId == cohort.Id, ct),
            cohort is null ? null : rosterCount * (rosterCount - 1), cohort is null ? null : await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().Where(x => x.CohortId == cohort.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct),
            manage && mode == "OfficialV2" ? await db.Set<OfficialContributionRun>().AsNoTracking().Where(x => x.ProjectId == project).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) : null);
    }
    private async Task RequireSchema(CancellationToken ct)
    {
        Check(db.Database.IsNpgsql() && await db.Database.SqlQuery<int>($"SELECT CASE WHEN to_regclass('public.\"OfficialContributionRuns\"') IS NOT NULL AND to_regclass('public.\"ProjectContributionWorkflows\"') IS NOT NULL THEN 1 ELSE 0 END AS \"Value\"").SingleAsync(ct) == 1,
            "OFFICIAL_SCHEMA_REQUIRED", "Deploy the reviewed official Contribution schema before enabling this workflow.", 503);
    }
    public async Task<Guid> CaptureAsync(Guid project, Guid account, OfficialSnapshotRequest request, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct);
        Check(request.ObservationCutoff <= DateTimeOffset.UtcNow, "OFFICIAL_CUTOFF_INVALID", "Cutoff cannot be in the future.", 400);
        Check(await db.Projects.AnyAsync(x => x.Id == project && x.Status == ProjectStatus.Completed, ct), "OFFICIAL_PROJECT_NOT_COMPLETED", "Complete the project before capture.");
        return await capture.CaptureAsync(project, account, request.ObservationCutoff, ct);
    }
    public async Task<OfficialSnapshotResponse> SnapshotAsync(Guid project, Guid snapshot, Guid account, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct);
        var result = await reader.ReadAsync(project, snapshot, account, ct);
        Check(result.PolicyVersion == 2, "OFFICIAL_POLICY2_REQUIRED", "Capture a policy-2 snapshot; legacy membership is not upgraded.");
        // Membership metadata only. Never expose frozen facts through the official API.
        return new("1", result.SnapshotId, result.ProjectId, result.CreatedAt,
            result.ObservationCutoff, result.PolicyVersion, result.RestrictedCount, result.Entries.Count,
            result.Entries.Select(x => x.VersionId).Distinct().Count(), result.Coverage?.Count ?? 0);
    }
    public async Task<OfficialReadinessResponse> ReadinessAsync(Guid project, Guid snapshot, Guid account, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct);
        Check((await reader.ReadAsync(project, snapshot, account, ct)).PolicyVersion == 2,
            "OFFICIAL_POLICY2_REQUIRED", "Capture a policy-2 snapshot; legacy membership is not upgraded.");
        var roster = await db.Set<ExperimentalPeerCohort>().AsNoTracking().Where(x => x.ProjectId == project)
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => x.RosterJson).FirstOrDefaultAsync(ct);
        var ids = roster is null ? await UniNet.Application.Common.ProjectMembership.EffectiveMemberIds(db, project).ToArrayAsync(ct) : JsonSerializer.Deserialize<Guid[]>(roster, Json)!;
        using var doc = JsonDocument.Parse(await engine.DiagnoseGitAsync(project, snapshot, account, ids, ct));
        var r = doc.RootElement;
        var share = r.GetProperty("gitShare");
        var members = r.GetProperty("results").EnumerateArray().Select(x => {
            var id = x.GetProperty("userId").GetGuid();
            return new OfficialGitMemberSummary(id, x.GetProperty("provisionalRawGitScore").GetDecimal(),
                x.GetProperty("readyGitScore").ValueKind == JsonValueKind.Null ? null : x.GetProperty("readyGitScore").GetDecimal(),
                share.ValueKind == JsonValueKind.Null ? null : share.GetProperty("members").GetProperty(id.ToString()).GetDecimal());
        }).ToArray();
        return new("1", snapshot, r.GetProperty("gitState").GetString()!, r.GetProperty("inputHash").GetString()!,
            OfficialCategories(r.GetProperty("categories").Deserialize<GitExperimentalCategoryStatus[]>(Json)!), r.GetProperty("limitations").Deserialize<string[]>(Json)!,
            members, share.ValueKind == JsonValueKind.Null ? null : share.GetProperty("externalPercent").GetDecimal());
    }
    public async Task<ExperimentalPeerCohortResponse> OpenPeerAsync(Guid project, Guid account, ExperimentalPeerOpenRequest request, CancellationToken ct)
    {
        await using var tx=db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct) : null;
        await LockProject(project,ct);await Actor(project,account,true,ct);await RequireV2(project,ct);
        Check(await db.Projects.AnyAsync(x=>x.Id==project&&x.Status==ProjectStatus.Completed,ct),"OFFICIAL_PROJECT_NOT_COMPLETED","Complete the project before freezing the roster.");
        var members=await UniNet.Application.Common.ProjectMembership.EffectiveMemberIds(db,project).ToArrayAsync(ct);
        Check(request.ParticipantIds is not null&&request.ParticipantIds.ToHashSet().SetEquals(members),"OFFICIAL_ROSTER_MISMATCH","Freeze all effective project members, including Creator.");
        Check(!await db.Set<ExperimentalPeerCohort>().AnyAsync(x=>x.ProjectId==project&&x.Status!=ExperimentalPeerCohortStatus.Closed,ct),"OFFICIAL_PEER_ROUND_ACTIVE","Finish the existing evaluation round before opening another.");
        var result=await peers.OpenAsync(project,account,request,ct);if(tx is not null) await tx.CommitAsync(ct);return result;
    }    public async Task<ExperimentalPeerCohortResponse> PeerAsync(Guid project, Guid cohort, Guid account, CancellationToken ct)
    { await RequireV2(project, ct); return await peers.ReadAsync(project, cohort, account, ct); }
    public async Task<ExperimentalPeerCohortResponse> SubmitPeerAsync(Guid project, Guid cohort, Guid evaluatee, Guid account, ExperimentalPeerSubmitRequest request, CancellationToken ct)
    { await RequireV2(project, ct); return await peers.SubmitAsync(project, cohort, account, evaluatee, request, ct); }

    // Internal guided-workflow summary. Compare scoring identities/content, not page observations,
    // timestamps of retrieval, or an old SyncReady flag. Existing frozen inputs remain unchanged.
    internal async Task<string> CompareGitAsync(Guid project, Guid snapshot, Guid? baselineRun, Guid account, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct);
        var roster = await UniNet.Application.Common.ProjectMembership.EffectiveMemberIds(db, project).ToArrayAsync(ct);
        using var diagnostic = JsonDocument.Parse(await engine.DiagnoseGitAsync(project, snapshot, account, roster, ct));
        if(diagnostic.RootElement.GetProperty("gitState").GetString()!="Ready") return "Unverified";
        if(baselineRun is null) return "NoPublishedBaseline";
        var baseline=await Calculation(project,baselineRun.Value,ct);
        var oldRun=await Run(project,baselineRun.Value,ct);
        Check(GitExperimentalContributionService.ComputeScoringInputHash(baseline.InputSnapshotJson)==baseline.InputHash&&GitExperimentalContributionService.OfficialResultHash(baseline)==oldRun.ResultHash,
            "OFFICIAL_INPUT_INTEGRITY","Published frozen calculation integrity check failed.");
        using var input=JsonDocument.Parse(baseline.InputSnapshotJson);
        var candidates=input.RootElement.GetProperty("eligibleActivities").EnumerateArray().Select(x=>new {
            Input=x.Clone(),
            Activity=new ExperimentalGitActivity(Enum.Parse<ExperimentalGitActivityCategory>(x.GetProperty("category").GetString()!),
                x.GetProperty("repositoryId").GetGuid(),x.GetProperty("objectId").GetString()!,
                x.GetProperty("userId").ValueKind==JsonValueKind.Null?null:x.GetProperty("userId").GetGuid(),
                x.GetProperty("activityAt").GetDateTimeOffset(),x.GetProperty("relatedCommitShas").Deserialize<string[]>(Json)!)
        }).ToArray();
        var oldRoster=input.RootElement.GetProperty("roster").EnumerateArray().Select(x=>x.GetProperty("userId").GetGuid()).ToArray();
        var scored=ExperimentalGitScoringV1.CalculateGit(oldRoster,candidates.Select(x=>x.Activity).ToArray()).ScoredActivities
            .Select(x=>(x.Category,x.RepositoryId,x.ObjectId)).ToHashSet();
        static string Key(JsonElement x)=>JsonSerializer.Serialize(new {
            Category=x.GetProperty("category").GetString(),Repository=x.GetProperty("repositoryId").GetGuid(),Object=x.GetProperty("objectId").GetString(),
            User=x.GetProperty("userId").ValueKind==JsonValueKind.Null?(Guid?)null:x.GetProperty("userId").GetGuid(),
            Actor=x.GetProperty("providerActorId").GetInt64(),At=x.GetProperty("activityAt").GetDateTimeOffset(),
            Hash=x.GetProperty("versionHash").GetString(),Relations=x.GetProperty("relatedCommitShas").EnumerateArray().Select(v=>v.GetString()).OrderBy(v=>v,StringComparer.Ordinal).ToArray()
        });
        var previous=candidates.Where(x=>scored.Contains((x.Activity.Category,x.Activity.RepositoryId,x.Activity.ObjectId))).Select(x=>Key(x.Input)).ToHashSet(StringComparer.Ordinal);
        var current=diagnostic.RootElement.GetProperty("scoredActivities").EnumerateArray().Select(Key).ToHashSet(StringComparer.Ordinal);
        return previous.SetEquals(current)?"NoEligibleChanges":"EligibleActivitiesChanged";
    }
    public async Task<Guid> ClosePeerAsync(Guid project, Guid cohort, Guid account, CancellationToken ct)
    { await RequireV2(project, ct); return await peers.CloseAsync(project, cohort, account, ct); }
    public async Task<OfficialPeerSnapshotResponse> PeerSnapshotAsync(Guid project, Guid id, Guid account, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct);
        var snapshot = await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == project && x.Id == id, ct)
            ?? throw Error("PEER_SNAPSHOT_NOT_FOUND", "Peer snapshot not found.", 404);
        Check(snapshot.PolicyVersion == "ExperimentalPeerV1" && ContributionCanonicalJson.Hash(snapshot.InputJson) == snapshot.InputHash,
            "OFFICIAL_PEER_INTEGRITY", "Peer snapshot integrity check failed.");
        using var input = JsonDocument.Parse(snapshot.InputJson);
        var roster = input.RootElement.GetProperty("roster").Deserialize<Guid[]>(Json)!;
        var ratings = input.RootElement.GetProperty("ratings").EnumerateArray().Select(x => new ContributionPeerEvaluationInput(
            x.GetProperty("evaluatorId").GetGuid(), x.GetProperty("evaluateeId").GetGuid(), x.GetProperty("score").GetInt32())).ToArray();
        var scores = ContributionCalculator.Calculate(roster, [], [], ratings).Results.ToDictionary(x => x.UserId, x => x.PeerEvaluationScore);
        Check(ContributionCanonicalJson.Hash(JsonSerializer.Serialize(scores.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value), Json)) == ContributionCanonicalJson.Hash(snapshot.ScoresJson),
            "OFFICIAL_PEER_INTEGRITY", "Peer snapshot scores do not match frozen ratings.");
        return new("1", id, snapshot.CohortId, snapshot.PolicyVersion, snapshot.InputHash, snapshot.CreatedAt, roster.Length,
            roster.Length == 1 ? "NotApplicable" : scores.Values.Sum() == 0 ? "InsufficientData" : "Ready", scores);
    }
    public async Task<OfficialPage<OfficialSnapshotMember>> MembershipAsync(Guid project, Guid id, Guid account, int page, int size, string? category, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct); Page(page, size);
        var snapshot = await reader.ReadAsync(project, id, account, ct);
        var rows = snapshot.Entries.Where(x => category is null || x.Category == category).OrderBy(x => x.ObservationId).ToArray();
        return new("1", page, size, rows.Length, rows.Skip((page - 1) * size).Take(size).Select(x => new OfficialSnapshotMember(
            x.AssociationId, x.RepositoryId, x.ObservationId, x.JobId, x.VersionId, x.Category, x.ScopeKey, x.ContentHash, x.Status.ToString(), x.Reason)).ToArray());
    }

    private async Task LockProject(Guid project, CancellationToken ct)
    {
        Check(db.Database.IsNpgsql(), "OFFICIAL_POSTGRES_REQUIRED", "Official lifecycle requires PostgreSQL.");
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Projects\" WHERE \"Id\"={project} FOR UPDATE").ToListAsync(ct);
    }
    private static async Task<T> Concurrency<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" or "23505" })
        { throw Error("OFFICIAL_CONCURRENCY_CONFLICT", "Reload and retry the same request with a fresh transaction."); }
    }
    public Task<OfficialRunResponse> CreateAsync(Guid project, Guid account, OfficialRunRequest request, CancellationToken ct) => Concurrency(async () =>
    {
        Check(request.RequestId != Guid.Empty && request.GitSnapshotId != Guid.Empty && request.PeerSnapshotId != Guid.Empty,
            "OFFICIAL_REQUEST_INVALID", "RequestId and both frozen Snapshot IDs are required.", 400);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        await LockProject(project, ct); var actor = await Actor(project, account, true, ct); await RequireV2(project, ct);
        var existing = await db.Set<OfficialContributionRun>().SingleOrDefaultAsync(x => x.ProjectId == project && x.RequestId == request.RequestId, ct);
        if (existing is not null)
        {
            var calc = await Calculation(project, existing.Id, ct);
            Check(calc.GitEvidenceSnapshotId == request.GitSnapshotId && calc.PeerSnapshotId == request.PeerSnapshotId,
                "OFFICIAL_IDEMPOTENCY_CONFLICT", "RequestId already identifies different frozen inputs.");
            var response = await Map(existing, account, ct); if(tx is not null) await tx.CommitAsync(ct); return response;
        }
        await RequireVersionSchema(ct);
        var peer = await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PeerSnapshotId && x.ProjectId == project, ct);
        Check(peer is not null, "PEER_SNAPSHOT_NOT_FOUND", "A closed Peer Snapshot in this project is required.", 404);
        var cohort = await db.Set<ExperimentalPeerCohort>().AsNoTracking().SingleAsync(x => x.Id == peer!.CohortId && x.ProjectId == project, ct);
        var effective = await UniNet.Application.Common.ProjectMembership.EffectiveMemberIds(db, project).ToArrayAsync(ct);
        Check(ExperimentalPeerCohortService.Roster(cohort).ToHashSet().SetEquals(effective), "OFFICIAL_ROSTER_MISMATCH", "The frozen roster must contain all effective project members.");
        var created = await engine.CreateOfficialAsync(project, account, request, ct);
        Check(created.CalculationStatus == "Ready", "OFFICIAL_CALCULATION_NOT_READY", "Git and Peer must be complete with positive applicable mass.");
        var calculation = await Calculation(project, created.RunId, ct);
        if (await db.Set<OfficialContributionRun>().AnyAsync(x => x.Id == created.RunId, ct))
        { var same = await Run(project, created.RunId, ct); var response = await Map(same, account, ct); if(tx is not null) await tx.CommitAsync(ct); return response with { ReusedInputs = true }; }
        await engine.ValidateOfficialAsync(calculation, account, ct);
        var run = new OfficialContributionRun { Id = created.RunId, ProjectId = project, RequestId = request.RequestId,
            CreatedById = actor, ResultHash = GitExperimentalContributionService.OfficialResultHash(calculation) };
        db.Add(run); db.Add(new OfficialContributionAudit { RunId = run.Id, ActorId = actor, Action = "Created", Revision = 0 });
        await db.SaveChangesAsync(ct); var result = await Map(run, account, ct); if(tx is not null) await tx.CommitAsync(ct); return result;
    });
    private async Task<GitContributionRun> Calculation(Guid project, Guid id, CancellationToken ct) =>
        await db.GitContributionRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ProjectId == project, ct)
        ?? throw Error("OFFICIAL_RUN_NOT_FOUND", "Official run not found.", 404);
    private async Task<OfficialContributionRun> Run(Guid project, Guid id, CancellationToken ct) =>
        await db.Set<OfficialContributionRun>().SingleOrDefaultAsync(x => x.Id == id && x.ProjectId == project, ct)
        ?? throw Error("OFFICIAL_RUN_NOT_FOUND", "Official run not found.", 404);
    private async Task<OfficialRunResponse> Map(OfficialContributionRun row, Guid account, CancellationToken ct)
    {
        var calculation = await Calculation(row.ProjectId, row.Id, ct);
        Check(row.ResultHash == GitExperimentalContributionService.OfficialResultHash(calculation), "OFFICIAL_RESULT_INTEGRITY", "Frozen calculation integrity check failed.");
        var pub = await db.Set<OfficialContributionRelease>().AsNoTracking().Where(x => x.RunId == row.Id).Select(x => (DateTimeOffset?)x.PublishedAt).SingleOrDefaultAsync(ct);
        var response = await engine.GetRunAsync(row.ProjectId, row.Id, account, ct);
        return new("1", row.Id, row.ProjectId, row.RequestId, pub.HasValue ? "Published" : row.State,
            row.Revision, row.ResultHash, response with { Categories = OfficialCategories(response.Categories) }, row.FinalizedAt, pub);
    }
    private static IReadOnlyList<GitExperimentalCategoryStatus> OfficialCategories(IReadOnlyList<GitExperimentalCategoryStatus> categories) =>
        categories.Select(x => x.Status == "EligibleForExperimentalMvp" ? x with { Status = "Complete" } : x).ToArray();
    public async Task<OfficialRunResponse> GetAsync(Guid project, Guid id, Guid account, CancellationToken ct)
    { await Actor(project, account, true, ct); await RequireV2(project, ct); return await Map(await Run(project, id, ct), account, ct); }
    public Task<OfficialRunResponse> TransitionAsync(Guid project, Guid id, Guid account, string action, OfficialTransitionRequest request, CancellationToken ct) => Concurrency(async () =>
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await LockProject(project, ct); var actor = await Actor(project, account, true, ct); await RequireV2(project, ct);
        var row = await Run(project, id, ct); await db.Entry(row).ReloadAsync(ct);
        if (action == "publish")
        {
            Check(row.State == "Finalized", "OFFICIAL_NOT_FINALIZED", "Finalize approved results before publication.");
            if (!await db.Set<OfficialContributionRelease>().AnyAsync(x=>x.RunId==id,ct))
            {
                Check(await CurrentRelease(project,ct) is null,"OFFICIAL_PUBLICATION_CHANGED","Use approve-and-publish with the expected current publication to replace a published result.");
                Check(row.Revision==request.ExpectedRevision,"OFFICIAL_REVISION_CONFLICT","Reload the run before publication.");
                await Validate(row,account,ct);
                await PublishRelease(row,actor,request.Feedback,ct);
            }
        }        else if (action == "finalize" && row.State == "Finalized") { /* same result, idempotent */ }
        else
        {
            Check(row.Revision == request.ExpectedRevision, "OFFICIAL_REVISION_CONFLICT", "Reload the run before changing its state.");
            var next = NextState(row.State, action, request.Feedback);
            await Validate(row, account, ct);
            row.State = next; row.Revision++;
            if (next == "Finalized") { row.FinalizedAt = DateTimeOffset.UtcNow; row.FinalizedById = actor; }
            db.Add(new OfficialContributionAudit { RunId = id, Revision = row.Revision, ActorId = actor, Action = next,
                Feedback = string.IsNullOrWhiteSpace(request.Feedback) ? null : request.Feedback.Trim() });
            await db.SaveChangesAsync(ct);
        }
        var result = await Map(row, account, ct); await tx.CommitAsync(ct); return result;
    });
    internal static string NextState(string state, string action, string? feedback)
    {
        Check(feedback is null || feedback.Length <= 2000, "OFFICIAL_FEEDBACK_INVALID", "Feedback maximum is 2000 characters.", 400);
        Check(action != "reject" || !string.IsNullOrWhiteSpace(feedback), "OFFICIAL_FEEDBACK_REQUIRED", "Rejection requires feedback.", 400);
        return (state, action) switch {
            ("Draft", "submit") or ("Rejected", "submit") => "Review", ("Review", "approve") => "Approved",
            ("Review", "reject") => "Rejected", ("Approved", "finalize") => "Finalized",
            _ => throw Error("OFFICIAL_TRANSITION_INVALID", "This lifecycle transition is not allowed.") };
    }
    private async Task Validate(OfficialContributionRun row, Guid account, CancellationToken ct)
    {
        var calculation = await Calculation(row.ProjectId, row.Id, ct);
        Check(row.ResultHash == GitExperimentalContributionService.OfficialResultHash(calculation), "OFFICIAL_RESULT_INTEGRITY", "Frozen result hash mismatch.");
        await engine.ValidateOfficialAsync(calculation, account, ct);
    }
    public async Task<OfficialPage<OfficialAuditResponse>> AuditAsync(Guid project, Guid id, Guid account, int page, int size, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await Run(project, id, ct); Page(page, size);
        var q = db.Set<OfficialContributionAudit>().AsNoTracking().Where(x => x.RunId == id);
        return new("1", page, size, await q.CountAsync(ct), await q.OrderBy(x => x.Revision).Skip((page - 1) * size).Take(size)
            .Select(x => new OfficialAuditResponse(x.Revision, x.Action, x.ActorId, x.At, x.Feedback)).ToArrayAsync(ct));
    }
    public async Task<OfficialPage<GitExperimentalExcludedActivityResponse>> ExclusionsAsync(Guid project, Guid id, Guid account, int page, int size, string? category, CancellationToken ct)
    {
        var run = await GetAsync(project, id, account, ct); Page(page, size);
        var all = run.Calculation.ExcludedActivities.Where(x => category is null || x.Category == category).ToArray();
        return new("1", page, size, all.Length, all.Skip((page - 1) * size).Take(size).ToArray());
    }
    private static void Page(int page, int size) => Check(page is > 0 and <= 100000 && size is > 0 and <= 100,
        "OFFICIAL_PAGE_INVALID", "Page must be 1..100000 and pageSize 1..100.", 400);
    public async Task<OfficialPage<OfficialRunListItem>> ListAsync(Guid project, Guid account, int page, int size, string? state, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct); Page(page, size);
        Check(state is null or "Draft" or "Review" or "Approved" or "Rejected" or "Finalized" or "Published", "OFFICIAL_STATE_INVALID", "Unknown lifecycle state.", 400);
        var q = from r in db.Set<OfficialContributionRun>().AsNoTracking()
                join p in db.Set<OfficialContributionRelease>().AsNoTracking() on r.Id equals p.RunId into publications
                from p in publications.DefaultIfEmpty()
                where r.ProjectId == project
                select new { r.Id, State = p == null ? r.State : "Published", r.Revision, r.CreatedAt, r.FinalizedAt, PublishedAt = p == null ? (DateTimeOffset?)null : p.PublishedAt };
        if (state is not null) q = q.Where(x => x.State == state);
        var count = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        return new("1", page, size, count, rows.Select(x => new OfficialRunListItem(x.Id, x.State, x.Revision, x.CreatedAt, x.FinalizedAt, x.PublishedAt)).ToArray());
    }
    public async Task<OfficialPage<OfficialScoredActivity>> ScoredAsync(Guid project, Guid id, Guid account, int page, int size, string? category, CancellationToken ct)
    {
        await Actor(project, account, true, ct); await RequireV2(project, ct); Page(page, size);
        var row = await Run(project, id, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await Validate(row, account, ct); // evidence IDs are disclosed only with current repository authorization
        using var input = JsonDocument.Parse((await Calculation(project, id, ct)).InputSnapshotJson);
        var candidates = input.RootElement.GetProperty("eligibleActivities").EnumerateArray().Select(x => new {
            Reference = x.Deserialize<OfficialScoredActivity>(Json)!,
            Activity = new ExperimentalGitActivity(Enum.Parse<ExperimentalGitActivityCategory>(x.GetProperty("category").GetString()!),
                x.GetProperty("repositoryId").GetGuid(), x.GetProperty("objectId").GetString()!,
                x.GetProperty("userId").ValueKind == JsonValueKind.Null ? null : x.GetProperty("userId").GetGuid(),
                x.GetProperty("activityAt").GetDateTimeOffset(), x.GetProperty("relatedCommitShas").Deserialize<string[]>(Json)!)
        }).ToArray();
        var roster = input.RootElement.GetProperty("roster").EnumerateArray().Select(x => x.GetProperty("userId").GetGuid()).ToArray();
        var scored = ExperimentalGitScoringV1.CalculateGit(roster, candidates.Select(x => x.Activity).ToArray()).ScoredActivities
            .Select(x => (x.Category, x.RepositoryId, x.ObjectId)).ToHashSet();
        var all = candidates.Where(x => scored.Contains((x.Activity.Category, x.Activity.RepositoryId, x.Activity.ObjectId)))
            .Select(x => x.Reference).Where(x => category is null || x.Category == category).ToArray();
        await tx.CommitAsync(ct);
        return new("1", page, size, all.Length, all.OrderBy(x => x.Category).ThenBy(x => x.ObjectId, StringComparer.Ordinal).Skip((page - 1) * size).Take(size).ToArray());
    }
    public async Task<OfficialMemberResultResponse> MyResultAsync(Guid project, Guid account, CancellationToken ct)
    {
        var actor = await Actor(project, account, false, ct); await RequireV2(project, ct);
        var pub = await db.Set<OfficialContributionRelease>().AsNoTracking().Where(x => x.ProjectId == project).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(ct)
            ?? throw Error("OFFICIAL_NOT_PUBLISHED", "No official result has been published.", 404);
        var row = await Run(project, pub.RunId, ct); var calc = await Calculation(project, row.Id, ct);
        Check(row.ResultHash == GitExperimentalContributionService.OfficialResultHash(calc), "OFFICIAL_RESULT_INTEGRITY", "Frozen result integrity check failed.");
        using var input = JsonDocument.Parse(calc.InputSnapshotJson); using var normalized = JsonDocument.Parse(calc.NormalizedResultsJson!);
        var ni = input.RootElement.GetProperty("normalizationInputs");
        var git = ni.GetProperty("gitWeights").Deserialize<Dictionary<Guid, decimal>>(Json)!;
        var peer = ni.GetProperty("peerScores").Deserialize<Dictionary<Guid, decimal>>(Json)!;
        Check(git.ContainsKey(actor) && peer.ContainsKey(actor), "FORBIDDEN", "Only frozen participants may read their result.", 403);
        var gsum = git.Values.Sum() + ni.GetProperty("externalGitWeight").GetDecimal(); var psum = peer.Values.Sum();
        return new("1", project, row.Id, pub.PublishedAt, actor, gsum == 0 ? 0 : git[actor] / gsum * 100,
            psum == 0 ? 0 : peer[actor] / psum * 100, normalized.RootElement.GetProperty("members").GetProperty(actor.ToString()).GetDecimal(),
            calc.NormalizationPolicyVersion!, calc.InputHash, row.ResultHash);
    }
}
