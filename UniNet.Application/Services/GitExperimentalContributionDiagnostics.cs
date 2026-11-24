using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

internal sealed partial class GitExperimentalContributionService
{
    // Internal audit only: no run, peer values, final percentage, or database write.
    // Reuses the same V2 selection, attribution, overlap arithmetic and readiness as run creation.
    internal async Task<string> DiagnoseGitAsync(Guid projectId, Guid snapshotId, Guid accountId,
        IReadOnlyList<Guid> participantIds, CancellationToken ct)
    {
        var actor = await GetActiveStudentAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        RequireLeader(project, actor.Id);
        Require(project.Status == ProjectStatus.Completed, "GIT_CONTRIBUTION_PROJECT_NOT_COMPLETED",
            "Git diagnostics require a completed project.", 409);
        var snapshot = await snapshots.ReadAsync(projectId, snapshotId, accountId, ct);
        Require(snapshot.ProjectId == projectId, "GIT_CONTRIBUTION_SNAPSHOT_PROJECT_MISMATCH", "Snapshot project mismatch.", 409);
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        var participants = await ResolveRequestedRosterAsync(project, participantIds, snapshot.ObservationCutoff, ct);
        var roster = participants.Select(x => x.UserId).ToArray();
        var history = await db.ProjectGitMemberIdentityHistory.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OccurredAt <= snapshot.ObservationCutoff)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.IdentityId).ThenBy(x => x.Id).ToArrayAsync(ct);
        var exclusions = new List<GitExperimentalExcludedActivityResponse>();
        var latest = SelectLatestEvidence(snapshot.Entries, exclusions);
        var selected = ExperimentalGitScoringV2.Filter(latest, exclusions);
        var work = BuildActivities(projectId, snapshot, selected, roster, history, exclusions);
        var inTimeline = ExperimentalGitScoringV2.ApplyCommitTimeline(work.Select(x => x.Activity).ToArray(),
            selected, snapshot.ObservationCutoff, exclusions);
        work = work.Where(x => inTimeline.Contains(x.Activity)).ToArray();
        var effective = await ProjectMembership.EffectiveMemberIds(db, projectId).ToArrayAsync(ct);
        work = work.Where(x =>
        {
            if (x.Activity.UserId is not null || !history.Any(h => h.ExternalAccountId == x.Input.ProviderActorId && effective.Contains(h.UserProfileId)))
                return true;
            exclusions.Add(new(CategoryName(x.Activity.Category), x.Activity.RepositoryId, x.Activity.ObjectId,
                "Unattributed", "VerifiedMemberOutsideCohort"));
            return false;
        }).ToArray();
        var git = ExperimentalGitScoringV1.CalculateGit(roster, work.Select(x => x.Activity).ToArray());
        exclusions.AddRange(git.ExcludedActivities.Select(x => new GitExperimentalExcludedActivityResponse(
            CategoryName(x.Category), x.RepositoryId, x.ObjectId, PolicyExclusionClassification(x.Reason),
            x.Reason == "CountedUnderMergedPullRequest" ? "AlreadyRepresentedByPullRequest" : x.Reason)));
        var categories = ExperimentalGitScoringV2.ApplyReadiness(BuildCategoryStatuses(selected, snapshot.RestrictedCount,
            git.EligibleActivityCounts, git.ExternalActivityCounts, exclusions), selected, snapshot.RestrictedCount, exclusions,
            GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(snapshot));
        var gap = ExperimentalGitScoringV2.HasEvidenceGap(categories, selected, snapshot.RestrictedCount, exclusions) ||
            git.EligibleActivityCounts.Values.Sum() == 0;
        var manifest = new
        {
            snapshot.ProjectId, snapshot.SnapshotId, snapshot.ObservationCutoff, snapshot.PolicyVersion,
            snapshot.RestrictedCount,
            snapshot.Coverage,
            Evidence = snapshot.Entries.OrderBy(x => x.ObservationId).Select(x => new
            { x.ObservationId, x.AssociationId, x.JobId, x.RepositoryId, x.VersionId, x.ContentHash, x.Category, x.ScopeKey, x.ExternalObjectId, x.Status, x.Reason }).ToArray()
        };
        // Hash exact frozen membership, not mutable authorization/validation results.
        // A restricted read cannot attest the complete membership's content hash.
        var snapshotMembershipHash = snapshot.RestrictedCount == 0 ? ContributionCanonicalJson.Hash(JsonSerializer.Serialize(new
        {
            snapshot.ProjectId, snapshot.SnapshotId, snapshot.ObservationCutoff, snapshot.PolicyVersion,
            Entries = snapshot.Entries.OrderBy(x => x.ObservationId).Select(x => new
            { x.ObservationId, x.AssociationId, x.VersionId, x.ContentHash }).ToArray()
        }, JsonOptions)) : null;
        if (snapshotMembershipHash is not null && snapshot.PolicyVersion >= 2)
            snapshotMembershipHash = ContributionCanonicalJson.Hash(JsonSerializer.Serialize(new { EvidenceHash = snapshotMembershipHash, snapshot.Coverage }, JsonOptions));
        var inputs = new
        {
            Policy = ExperimentalGitScoringV2.PolicyVersion, SnapshotMembershipHash = snapshotMembershipHash, Manifest = manifest,
            Roster = participants.Select(x => new { x.UserId, x.RoleAtSnapshot, x.MembershipStatusAtSnapshot, x.JoinedAt, x.LeftAt }).ToArray(),
            IdentityHistory = history,
            Candidates = work.Select(x => x.Input).OrderBy(x => x.Category).ThenBy(x => x.RepositoryId).ThenBy(x => x.ObjectId).ToArray()
        };
        var inputHash = ContributionCanonicalJson.Hash(JsonSerializer.Serialize(inputs, JsonOptions));
        var scored = work.Where(x => git.ScoredActivities.Contains(x.Activity)).Select(x => x.Input).ToArray();
        var orderedExclusions = exclusions.OrderBy(x => x.Category).ThenBy(x => x.RepositoryId).ThenBy(x => x.ObjectId).ThenBy(x => x.Reason).ToArray();
        var latestIds = latest.Select(x => x.ObservationId).ToHashSet();
        var scoredIds = scored.Select(x => x.ObservationId).ToHashSet();
        var relationIds = selected.Where(x => x.Category == "pull-request-commits").Select(x => x.ObservationId).ToHashSet();
        var observationAudit = snapshot.Entries.OrderBy(x => x.ObservationId).Select(x => new
        {
            x.ObservationId, x.AssociationId, x.JobId, x.VersionId, x.Category, x.ScopeKey, x.ExternalObjectId,
            Reason = !IsScoredEvidenceCategory(x.Category) && x.Category != "pull-request-commits" ? "AuditOnlyNonScoringCategory" :
                !latestIds.Contains(x.ObservationId) ? "DuplicateObservation" :
                scoredIds.Contains(x.ObservationId) ? "Scored" : relationIds.Contains(x.ObservationId) ? "PullRequestRelationshipEvidence" :
                orderedExclusions.FirstOrDefault(e => e.RepositoryId == x.RepositoryId && e.ObjectId == x.ExternalObjectId && e.Reason != "DuplicateObservation")?.Reason ?? "NotScored"
        }).ToArray();
        var output = new
        {
            DiagnosticOnly = true, inputs.Policy, snapshot.SnapshotId, snapshot.ObservationCutoff,
            SnapshotMembershipHash = snapshotMembershipHash, InputHash = inputHash, Inputs = inputs,
            GitState = gap ? "Incomplete" : "Ready",
            Results = git.Results.Select(x => new { x.UserId, RawCategoryScores = new { x.CommitScore, x.PullRequestScore, x.ReviewScore, x.IssueScore },
                ProvisionalRawGitScore = x.GitScore, ReadyGitScore = gap ? (decimal?)null : x.GitScore }).ToArray(),
            GitShare = gap ? null : ContributionPercentAllocator.Allocate(git.UnroundedGitWeights, git.ExternalGitWeight, true),
            Categories = categories, ScoredActivities = scored, ExcludedActivities = orderedExclusions,
            ObservationAudit = observationAudit,
            AuditOnlyEntries = snapshot.Entries.Where(x => !IsScoredEvidenceCategory(x.Category) && x.Category != "pull-request-commits")
                .Select(x => new { x.ObservationId, x.VersionId, x.Category, Reason = "AuditOnlyNonScoringCategory" }).ToArray(),
            ExternalActivityCounts = git.ExternalActivityCounts.ToDictionary(x => x.Key.ToString(), x => x.Value),
            git.UnroundedGitWeights, git.ExternalGitWeight,
            CommitTimeline = selected.Where(x => x.Category == "commits" && x.Facts?.CommitterDate <= snapshot.ObservationCutoff)
                .Select(x => x.Facts!.CommitterDate).Order().ToArray(),
            Limitations = new[] { "DiagnosticNotContributionRun", "RosterAndHistoryFrozenInDiagnosticArtifactOnly",
                "PartialPatchOverlapNotSupportedInExperimentalV1", "HistoricalForcePushNotProven" }
        };
        if (tx is not null) await tx.CommitAsync(ct);
        return JsonSerializer.Serialize(output, JsonOptions);
    }
}
