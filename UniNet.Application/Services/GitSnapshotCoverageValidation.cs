using System.Globalization;

namespace UniNet.Application.Services;

internal static class GitSnapshotCoverageValidation
{
    // A mutable live checkpoint is never consulted here. These receipts were
    // frozen with membership in the same policy-2 Repeatable Read capture.
    public static bool HasVerifiedEmptyReviewCoverage(ProjectGitEvidenceSnapshotRead snapshot)
    {
        if (snapshot.PolicyVersion < 2 || snapshot.RestrictedCount != 0 || snapshot.Coverage is not { Count: > 0 } receipts ||
            snapshot.Entries.Any(x => x.Category == "pull-request-reviews")) return false;
        if (receipts.Any(x => x.ProjectId != snapshot.ProjectId || x.SnapshotId != snapshot.SnapshotId ||
            x.ReceiptPolicyVersion != 1 || x.ItemsProcessed < 0 || x.ScopeCompletedAt > x.JobCompletedAt ||
            x.JobCompletedAt > snapshot.ObservationCutoff)) return false;
        var associations = snapshot.Entries.Select(x => x.AssociationId)
            .Concat(receipts.Select(x => x.ProjectGitRepositoryId)).Distinct().ToArray();
        foreach (var association in associations)
        {
            var enumerations = receipts.Where(x => x.ProjectGitRepositoryId == association && x.Category == "pull-requests" && x.ScopeKey == "").ToArray();
            if (enumerations.Length == 0) return false;
            var latest = enumerations.Where(x => x.JobCompletedAt == enumerations.Max(y => y.JobCompletedAt)).ToArray();
            if (latest.Length != 1) return false;
            var enumeration = latest[0];
            if (snapshot.Entries.Any(x => x.AssociationId == association && x.ObservedAt > enumeration.JobCompletedAt)) return false;
            var groups = snapshot.Entries.Where(x => x.AssociationId == association && x.Category == "pull-requests")
                .GroupBy(x => x.ExternalObjectId).Select(x => x.Where(y => y.ObservedAt == x.Max(z => z.ObservedAt)).ToArray()).ToArray();
            if (groups.Any(x => x.Select(y => (y.JobId, y.VersionId)).Distinct().Count() != 1)) return false;
            var prs = groups.Select(x => x[0]).ToArray();
            if (prs.Any(x => x.JobId != enumeration.GitSyncJobId)) return false;
            if (prs.LongLength != enumeration.ItemsProcessed || prs.Any(x => x.Status != SnapshotEvidenceStatus.Valid || x.Facts?.Details?.RelatedPullRequestNumber is null or <= 0)) return false;
            var numbers = prs.Select(x => x.Facts!.Details!.RelatedPullRequestNumber!.Value).ToArray();
            if (numbers.Distinct().Count() != numbers.Length) return false;
            foreach (var number in numbers)
            {
                var reviews = receipts.Where(x => x.ProjectGitRepositoryId == association && x.GitSyncJobId == enumeration.GitSyncJobId &&
                    x.Category == "pull-request-reviews" && x.ScopeKey == number.ToString(CultureInfo.InvariantCulture)).ToArray();
                if (reviews.Length != 1 || reviews[0].ItemsProcessed != 0) return false;
            }
        }
        return associations.Length != 0;
    }
}
