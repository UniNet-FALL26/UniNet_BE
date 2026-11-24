using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ExperimentalPeerCohortService(UniNetDbContext db, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ExperimentalPeerCohortResponse> OpenAsync(Guid projectId, Guid accountId, ExperimentalPeerOpenRequest request, CancellationToken ct)
    {
        if (request.ParticipantIds is null) throw Error("PEER_ROSTER_INVALID", "A participant roster is required.");
        var actor = await ActorAsync(projectId, accountId, ct);
        await RequireLeaderAsync(projectId, actor, ct);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var creator = await db.Projects.Where(x => x.Id == projectId).Select(x => x.CreatorId).SingleAsync(ct);
        var roster = request.ParticipantIds.Append(creator).Distinct().Order().ToArray();
        var eligible = await ProjectMembership.EffectiveMemberIds(db, projectId).Where(id => roster.Contains(id))
            .Join(db.UserProfiles.Where(x => x.Account.Status == AccountStatus.Active && x.Account.Role == AccountRole.Student), id => id, p => p.Id, (id, _) => id).ToArrayAsync(ct);
        Require(roster.Length == eligible.Length && !roster.Contains(Guid.Empty), "PEER_ROSTER_INVALID", "All participants must be active Student project members.");
        var json = JsonSerializer.Serialize(roster, Json);
        var cohort = new ExperimentalPeerCohort { ProjectId = projectId, OpenedById = actor, RosterJson = json, RosterHash = Hash(json), CreatedAt = clock.GetUtcNow(),
            Status = roster.Length == 1 ? ExperimentalPeerCohortStatus.ReadyToClose : ExperimentalPeerCohortStatus.Open };
        db.Add(cohort); await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        return await ReadAsync(projectId, cohort.Id, accountId, ct);
    }

    public Task<ExperimentalPeerCohortResponse> SubmitAsync(Guid projectId, Guid cohortId, Guid accountId, Guid evaluateeId, ExperimentalPeerSubmitRequest request, CancellationToken ct) =>
        ConcurrencyAsync(() => SubmitCoreAsync(projectId, cohortId, accountId, evaluateeId, request, ct));

    private async Task<ExperimentalPeerCohortResponse> SubmitCoreAsync(Guid projectId, Guid cohortId, Guid accountId, Guid evaluateeId, ExperimentalPeerSubmitRequest request, CancellationToken ct)
    {
        var actor = await ActorAsync(projectId, accountId, ct);
        Require(request.Score is >= 1 and <= 5 && request.Comment?.Length is not > 2000, "PEER_RATING_INVALID", "Ratings must be 1–5 and comments at most 2000 characters.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var cohort = await LockAsync(projectId, cohortId, ct);
        var roster = Roster(cohort);
        Require(cohort.Status != ExperimentalPeerCohortStatus.Closed, "PEER_COHORT_CLOSED", "The cohort is closed.", 409);
        Require(roster.Contains(actor) && roster.Contains(evaluateeId), "FORBIDDEN", "Both users must belong to the cohort roster.", 403);
        Require(actor != evaluateeId, "PEER_SELF_REVIEW_FORBIDDEN", "Self evaluation is not allowed.");
        var row = await db.Set<ExperimentalPeerSubmission>().SingleOrDefaultAsync(x => x.CohortId == cohortId && x.EvaluatorId == actor && x.EvaluateeId == evaluateeId, ct);
        Require(row is null ? request.ExpectedRevision is null or 0 : request.ExpectedRevision == row.Revision,
            "PEER_REVISION_CONFLICT", "Reload your evaluation before editing it.", 409);
        var now = clock.GetUtcNow();
        var oldScore = row?.Score; var oldComment = row?.Comment;
        if (row is null)
        {
            row = new ExperimentalPeerSubmission { CohortId = cohortId, EvaluatorId = actor, EvaluateeId = evaluateeId, SubmittedAt = now };
            db.Add(row);
        }
        else row.Revision++;
        row.Score = request.Score; row.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(); row.UpdatedAt = now;
        // Enforce the database revision guard before inserting the corresponding audit row.
        await db.SaveChangesAsync(ct);
        db.Add(new ExperimentalPeerRevision { SubmissionId = row.Id, Revision = row.Revision, PreviousScore = oldScore,
            Score = row.Score, PreviousComment = oldComment, Comment = row.Comment, ChangedAt = now });
        await db.SaveChangesAsync(ct);
        var count = await db.Set<ExperimentalPeerSubmission>().CountAsync(x => x.CohortId == cohortId, ct);
        cohort.Status = count == roster.Length * (roster.Length - 1) ? ExperimentalPeerCohortStatus.ReadyToClose : ExperimentalPeerCohortStatus.Collecting;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await ReadAsync(projectId, cohortId, accountId, ct);
    }

    public Task<Guid> CloseAsync(Guid projectId, Guid cohortId, Guid accountId, CancellationToken ct) =>
        ConcurrencyAsync(() => CloseCoreAsync(projectId, cohortId, accountId, ct));

    private async Task<Guid> CloseCoreAsync(Guid projectId, Guid cohortId, Guid accountId, CancellationToken ct)
    {
        var actor = await ActorAsync(projectId, accountId, ct); await RequireLeaderAsync(projectId, actor, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var cohort = await LockAsync(projectId, cohortId, ct);
        if (cohort.Status == ExperimentalPeerCohortStatus.Closed)
        {
            var id = await db.Set<ExperimentalPeerSnapshot>().Where(x => x.CohortId == cohortId).Select(x => x.Id).SingleAsync(ct);
            await tx.CommitAsync(ct); return id;
        }
        var roster = Roster(cohort);
        var activeRoster = await ProjectMembership.EffectiveMemberIds(db, projectId).Where(id => roster.Contains(id))
            .Join(db.UserProfiles.Where(x => x.Account.Status == AccountStatus.Active && x.Account.Role == AccountRole.Student), id => id, p => p.Id, (id, _) => id).CountAsync(ct);
        Require(activeRoster == roster.Length, "PEER_ROSTER_NO_LONGER_ELIGIBLE", "The frozen cohort contains an inactive participant; resolve eligibility before closing.", 409);
        var ratings = await db.Set<ExperimentalPeerSubmission>().AsNoTracking().Where(x => x.CohortId == cohortId)
            .OrderBy(x => x.EvaluatorId).ThenBy(x => x.EvaluateeId).ToArrayAsync(ct);
        var scores = ContributionCalculator.Calculate(roster, [], [], ratings.Select(x => new ContributionPeerEvaluationInput(x.EvaluatorId, x.EvaluateeId, x.Score)).ToArray())
            .Results.ToDictionary(x => x.UserId, x => x.PeerEvaluationScore);
        var frozen = JsonSerializer.Serialize(new { PolicyVersion = "ExperimentalPeerV1", cohort.Id, cohort.ProjectId, cohort.RosterHash,
            Roster = roster, Ratings = ratings.Select(x => new { x.Id, x.EvaluatorId, x.EvaluateeId, x.Score, x.Comment, x.Revision, x.UpdatedAt }).ToArray() }, Json);
        var snapshot = new ExperimentalPeerSnapshot { ProjectId = projectId, CohortId = cohortId, InputJson = frozen, InputHash = Hash(frozen),
            ScoresJson = JsonSerializer.Serialize(scores.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value), Json), CreatedAt = clock.GetUtcNow() };
        db.Add(snapshot); await db.SaveChangesAsync(ct);
        cohort.Status = ExperimentalPeerCohortStatus.Closed; cohort.ClosedAt = snapshot.CreatedAt;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return snapshot.Id;
    }

    public async Task<ExperimentalPeerCohortResponse> ReadAsync(Guid projectId, Guid cohortId, Guid accountId, CancellationToken ct)
    {
        var actor = await ActorAsync(projectId, accountId, ct);
        var cohort = await db.Set<ExperimentalPeerCohort>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == cohortId && x.ProjectId == projectId, ct)
            ?? throw Error("PEER_COHORT_NOT_FOUND", "Cohort not found.", 404);
        var leader = await IsLeaderAsync(projectId, actor, ct);
        var roster = Roster(cohort); Require(leader || roster.Contains(actor), "FORBIDDEN", "You are not a cohort participant.", 403);
        var submissions = db.Set<ExperimentalPeerSubmission>().AsNoTracking().Where(x => x.CohortId == cohortId);
        var count = await submissions.CountAsync(ct);
        // No recipient-side rating, comment or evaluator identity is returned to members.
        var ratings = await submissions.Where(x => leader || x.EvaluatorId == actor).OrderBy(x => x.EvaluatorId).ThenBy(x => x.EvaluateeId)
            .Select(x => new ExperimentalPeerRating(x.EvaluatorId, x.EvaluateeId, x.Score, x.Comment, x.Revision, x.UpdatedAt)).ToListAsync(ct);
        var history = leader ? await (from h in db.Set<ExperimentalPeerRevision>().AsNoTracking()
            join s in db.Set<ExperimentalPeerSubmission>().AsNoTracking() on h.SubmissionId equals s.Id
            where s.CohortId == cohortId orderby s.EvaluatorId, s.EvaluateeId, h.Revision
            select new ExperimentalPeerHistory(s.EvaluatorId, s.EvaluateeId, h.Revision, h.PreviousScore, h.Score, h.PreviousComment, h.Comment, h.ChangedAt)).ToListAsync(ct) : [];
        var snapshotId = await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().Where(x => x.CohortId == cohortId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (!leader && snapshotId.HasValue && await db.GitContributionRuns.AsNoTracking().AnyAsync(x => x.PeerSnapshotId == snapshotId && x.Status == GitContributionRunStatus.Finalized, ct)) ratings.Clear();
        return new(cohort.Id, projectId, cohort.Status, roster, count, roster.Length * (roster.Length - 1), snapshotId, ratings, history);
    }

    private async Task<ExperimentalPeerCohort> LockAsync(Guid project, Guid cohort, CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"ExperimentalPeerCohorts\" WHERE \"Id\"={cohort} AND \"ProjectId\"={project} FOR UPDATE").ToListAsync(ct);
        var entity = await db.Set<ExperimentalPeerCohort>().SingleOrDefaultAsync(x => x.Id == cohort && x.ProjectId == project, ct)
            ?? throw Error("PEER_COHORT_NOT_FOUND", "Cohort not found.", 404);
        // A scoped DbContext may already track a cohort from an earlier request in a test/operator flow.
        await db.Entry(entity).ReloadAsync(ct); return entity;
    }
    private async Task<Guid> ActorAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var actor = await ProjectGitAuthorization.RequireActorProfileAsync(db, accountId, ct);
        Require(await ProjectMembership.IsEffectiveMemberAsync(db, projectId, actor, ct), "FORBIDDEN", "An active project member is required.", 403);
        return actor;
    }
    private Task<bool> IsLeaderAsync(Guid project, Guid actor, CancellationToken ct) =>
        db.Projects.AnyAsync(x => x.Id == project && x.CreatorId == actor, ct);
    private async Task RequireLeaderAsync(Guid project, Guid actor, CancellationToken ct) =>
        Require(await IsLeaderAsync(project, actor, ct), "FORBIDDEN", "Only the existing experimental project leader can manage cohorts.", 403);
    internal static Guid[] Roster(ExperimentalPeerCohort cohort)
    {
        var roster = JsonSerializer.Deserialize<Guid[]>(cohort.RosterJson, Json) ?? [];
        Require(roster.Length > 0 && !roster.Contains(Guid.Empty) && roster.Distinct().Count() == roster.Length && Hash(cohort.RosterJson) == cohort.RosterHash,
            "PEER_ROSTER_INTEGRITY_ERROR", "The frozen peer roster is invalid.", 409);
        return roster;
    }
    private static string Hash(string json) => ContributionCanonicalJson.Hash(json);
    private static async Task<T> ConcurrencyAsync<T>(Func<Task<T>> work)
    {
        try { return await work(); }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" or "23505" })
        { throw Error("PEER_CONCURRENCY_CONFLICT", "Reload cohort state and retry the request.", 409); }
    }
    private static ProjectException Error(string code, string message, int status = 400) => new(code, message, status);
    private static void Require(bool condition, string code, string message, int status = 400) { if (!condition) throw Error(code, message, status); }
}
