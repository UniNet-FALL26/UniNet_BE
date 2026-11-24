using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Domain.Entities;

namespace UniNet.Application.Services;

internal sealed partial class GitExperimentalContributionService
{
    private async Task RequireExperimentalLifecycleAsync(Guid runId, CancellationToken ct)
    {
        if (db.Database.IsNpgsql() && await db.Database.SqlQuery<int>($"SELECT CASE WHEN to_regclass('public.\"OfficialContributionRuns\"') IS NULL THEN 0 ELSE 1 END AS \"Value\"").SingleAsync(ct) == 0) return;
        Require(!await db.Set<OfficialContributionRun>().AnyAsync(x => x.Id == runId, ct),
            "OFFICIAL_LIFECYCLE_REQUIRED", "Use the official review/finalization workflow for this run.", 409);
    }
    internal Task<GitExperimentalRunResponse> CreateOfficialAsync(Guid project, Guid actor,
        OfficialRunRequest request, CancellationToken ct) => CreateRunCoreAsync(project, actor,
            new(request.GitSnapshotId, true, null, ExperimentalGitScoringV2.PolicyVersion, request.PeerSnapshotId), ct, true);

    internal static string OfficialResultHash(GitContributionRun run) => ContributionCanonicalJson.Hash(
        JsonSerializer.Serialize(new { run.Id, run.ProjectId, run.InputHash, run.ScoringPolicyVersion,
            run.NormalizationPolicyVersion, run.PeerSnapshotId, run.CalculationStatus,
            Results = JsonDocument.Parse(run.ResultsJson).RootElement,
            Normalization = JsonDocument.Parse(run.NormalizedResultsJson ?? "null").RootElement,
            Categories = JsonDocument.Parse(run.CategoryStatusJson).RootElement,
            Excluded = JsonDocument.Parse(run.ExcludedActivitiesJson).RootElement }, JsonOptions));

    // Replays the same calculator against immutable evidence and frozen attribution,
    // not mutable provider payload or a legacy Cycle roster.
    internal async Task ValidateOfficialAsync(GitContributionRun run, Guid accountId, CancellationToken ct)
    {
        Require(run.ScoringPolicyVersion == ExperimentalGitScoringV2.PolicyVersion &&
            run.NormalizationPolicyVersion == GitPeerNormalization.Version && run.CalculationStatus == "Ready" &&
            ComputeScoringInputHash(run.InputSnapshotJson) == run.InputHash,
            "OFFICIAL_INPUT_INTEGRITY", "Official calculation input is invalid.", 409);
        var read = await snapshots.ReadAsync(run.ProjectId, run.GitEvidenceSnapshotId, accountId, ct);
        Require(read.PolicyVersion == 2 && read.RestrictedCount == 0,
            "OFFICIAL_EVIDENCE_RESTRICTED", "Current repository authorization and policy-2 evidence are required.", 409);
        using var input = JsonDocument.Parse(run.InputSnapshotJson);
        var root = input.RootElement;
        var roster = root.GetProperty("roster").EnumerateArray().Select(x => x.GetProperty("userId").GetGuid()).ToArray();
        foreach (var id in roster.Order())
        {
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Users\" p JOIN \"Accounts\" a ON a.\"Id\"=p.\"AccountId\" WHERE p.\"Id\"={id} FOR SHARE OF p,a").ToListAsync(ct);
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"ProjectMembers\" WHERE \"ProjectId\"={run.ProjectId} AND \"UserId\"={id} FOR SHARE").ToListAsync(ct);
        }
        await ResolveRequestedRosterAsync(await GetProjectAsync(run.ProjectId, ct), roster, read.ObservationCutoff, ct);
        var peer = await db.Set<ExperimentalPeerSnapshot>().AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == run.PeerSnapshotId && x.ProjectId == run.ProjectId, ct);
        Require(peer is not null && peer.PolicyVersion == "ExperimentalPeerV1" &&
            ContributionCanonicalJson.Hash(peer.InputJson) == peer.InputHash &&
            root.GetProperty("peerSnapshot").GetProperty("inputHash").GetString() == peer.InputHash,
            "OFFICIAL_PEER_INTEGRITY", "Closed peer input is invalid.", 409);
        var cohort = await db.Set<ExperimentalPeerCohort>().AsNoTracking().SingleAsync(x => x.Id == peer!.CohortId, ct);
        Require(cohort.ProjectId == run.ProjectId && cohort.Status == ExperimentalPeerCohortStatus.Closed &&
            roster.ToHashSet().SetEquals(ExperimentalPeerCohortService.Roster(cohort)),
            "OFFICIAL_ROSTER_MISMATCH", "Frozen V2 roster does not match the closed peer cohort.", 409);
        using var peerDoc = JsonDocument.Parse(peer!.InputJson);
        Require(roster.ToHashSet().SetEquals(peerDoc.RootElement.GetProperty("roster").EnumerateArray().Select(x => x.GetGuid())),
            "OFFICIAL_ROSTER_MISMATCH", "Peer roster differs from calculation roster.", 409);
        var ratings = peerDoc.RootElement.GetProperty("ratings").EnumerateArray().Select(x => new ContributionPeerEvaluationInput(
            x.GetProperty("evaluatorId").GetGuid(), x.GetProperty("evaluateeId").GetGuid(), x.GetProperty("score").GetInt32())).ToArray();
        var peers = ContributionCalculator.Calculate(roster, [], [], ratings).Results.ToDictionary(x => x.UserId, x => x.PeerEvaluationScore);
        var frozenHistory = root.GetProperty("identityHistory").EnumerateArray().Select(x => new ProjectGitMemberIdentityHistory {
            Id = x.GetProperty("id").GetGuid(), IdentityId = x.GetProperty("identityId").GetGuid(), ProjectId = run.ProjectId,
            UserProfileId = x.GetProperty("userProfileId").GetGuid(), Provider = x.GetProperty("provider").GetString()!,
            ExternalAccountId = x.GetProperty("externalAccountId").GetInt64(), OccurredAt = x.GetProperty("occurredAt").GetDateTimeOffset(),
            NewStatus = x.GetProperty("newStatus").Deserialize<UniNet.Domain.Enums.ProjectGitMemberIdentityStatus?>(JsonOptions),
            VerifiedByConnectionId = x.GetProperty("verifiedByConnectionId").ValueKind == JsonValueKind.Null ? null : x.GetProperty("verifiedByConnectionId").GetGuid()
        }).ToArray();
        var excluded = new List<GitExperimentalExcludedActivityResponse>();
        var selected = ExperimentalGitScoringV2.Filter(SelectLatestEvidence(read.Entries, excluded), excluded);
        var selectedJobs = selected.Select(x => x.JobId).Distinct().ToArray();
        Require(await db.GitSyncJobs.AsNoTracking().CountAsync(x => selectedJobs.Contains(x.Id) &&
            x.Status == UniNet.Domain.Enums.GitSyncJobStatus.Completed && x.CompletedAt != null && x.CompletedAt <= read.ObservationCutoff, ct) == selectedJobs.Length,
            "OFFICIAL_RETRIEVAL_INCOMPLETE", "Selected scoring evidence requires completed retrieval jobs within the cutoff.", 409);
        var frozenEvidence = root.GetProperty("evidence").EnumerateArray().Select(x =>
            (x.GetProperty("observationId").GetGuid(), x.GetProperty("versionId").GetGuid(), x.GetProperty("contentHash").GetString())).ToArray();
        Require(frozenEvidence.Length == read.Entries.Count && frozenEvidence.OrderBy(x => x.Item1).SequenceEqual(
            read.Entries.Select(x => (x.ObservationId, x.VersionId, (string?)x.ContentHash)).OrderBy(x => x.ObservationId)),
            "OFFICIAL_EVIDENCE_CHANGED", "Frozen snapshot membership differs from calculation input.", 409);
        Require(ContributionCanonicalJson.Hash(JsonSerializer.Serialize(read.Coverage, JsonOptions)) ==
            ContributionCanonicalJson.Hash(root.GetProperty("coverage").GetRawText()),
            "OFFICIAL_EVIDENCE_CHANGED", "Frozen coverage differs from the calculation input.", 409);
        var work = BuildActivities(run.ProjectId, read, selected, roster, frozenHistory, excluded);
        var timeline = ExperimentalGitScoringV2.ApplyCommitTimeline(work.Select(x => x.Activity).ToArray(), selected, read.ObservationCutoff, excluded);
        work = work.Where(x => timeline.Contains(x.Activity)).ToArray();
        var effective = await ProjectMembership.EffectiveMemberIds(db, run.ProjectId).ToArrayAsync(ct);
        work = work.Where(x => x.Activity.UserId is not null || !frozenHistory.Any(h =>
            h.ExternalAccountId == x.Input.ProviderActorId && effective.Contains(h.UserProfileId))).ToArray();
        Require(ContributionCanonicalJson.Hash(JsonSerializer.Serialize(work.Select(x => x.Input)
                .OrderBy(x => x.Category, StringComparer.Ordinal).ThenBy(x => x.RepositoryId).ThenBy(x => x.ObjectId, StringComparer.Ordinal), JsonOptions)) ==
            ContributionCanonicalJson.Hash(root.GetProperty("eligibleActivities").GetRawText()),
            "OFFICIAL_EVIDENCE_CHANGED", "Frozen activity selection cannot be verified.", 409);
        var scoring = ExperimentalGitScoringV1.Calculate(roster, work.Select(x => x.Activity).ToArray(), peers);
        excluded.AddRange(scoring.ExcludedActivities.Select(x => new GitExperimentalExcludedActivityResponse(CategoryName(x.Category),
            x.RepositoryId, x.ObjectId, PolicyExclusionClassification(x.Reason), x.Reason == "CountedUnderMergedPullRequest" ? "AlreadyRepresentedByPullRequest" : x.Reason)));
        var categories = ExperimentalGitScoringV2.ApplyReadiness(BuildCategoryStatuses(selected, read.RestrictedCount,
            scoring.EligibleActivityCounts, scoring.ExternalActivityCounts, excluded), selected, read.RestrictedCount, excluded,
            GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(read));
        Require(scoring.EligibleActivityCounts.Values.Sum() > 0 && !ExperimentalGitScoringV2.HasEvidenceGap(categories, selected, read.RestrictedCount, excluded),
            "OFFICIAL_CALCULATION_NOT_READY", "Required historical evidence cannot be verified.", 409);
        var normalization = GitPeerNormalization.Calculate(scoring.UnroundedGitWeights, scoring.ExternalGitWeight,
            ScoreState.Ready, peers, roster.Length == 1 ? ScoreState.NotApplicable : ScoreState.Ready);
        Require(normalization.Status == "Ready" && normalization.Members.Values.Sum() + normalization.ExternalPercent == 100m &&
            ContributionCanonicalJson.Hash(JsonSerializer.Serialize(normalization, JsonOptions)) == ContributionCanonicalJson.Hash(run.NormalizedResultsJson!) &&
            ContributionCanonicalJson.Hash(JsonSerializer.Serialize(scoring.Results.OrderBy(x => x.UserId), JsonOptions)) == ContributionCanonicalJson.Hash(run.ResultsJson),
            "OFFICIAL_RESULT_INTEGRITY", "Results do not match deterministic calculation.", 409);
        Require(!await db.ContributionCycles.AnyAsync(x => x.ProjectId == run.ProjectId &&
            x.Status == UniNet.Domain.Enums.ContributionCycleStatus.Finalized, ct),
            "OFFICIAL_LEGACY_RESULT_CONFLICT", "A finalized legacy result already exists.", 409);
    }
}
