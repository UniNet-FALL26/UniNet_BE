using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

internal sealed record HistoricalAttributionResult(Guid? UserId, string Reason);

// Pure resolver only: caller must supply verified, frozen evidence, roster and complete
// project identity history. This method does not infer identities from logins or score work.
internal static class GitHistoricalAttributionService
{
    // The caller selects the actor role explicitly; this helper never turns incomplete
    // snapshot evidence into a scoring attribution or infers an event time from observation time.
    public static HistoricalAttributionResult ResolveCommit(Guid projectId, SnapshotEvidenceEntry entry,
        string actorRole, DateTimeOffset historyCutoff, IReadOnlyCollection<Guid> roster,
        IReadOnlyCollection<ProjectGitMemberIdentityHistory> history)
    {
        if (entry.Status != SnapshotEvidenceStatus.Valid || entry.Facts is null)
            return new(null, "EvidenceNotEligible");
        if (entry.Facts.Category != "commits") return new(null, "UnsupportedCategory");
        var actors = entry.Facts.Actors.Where(x => x.Role == actorRole).ToArray();
        if (actors.Length != 1 || !long.TryParse(actors[0].ProviderAccountId,
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var actorId))
            return new(null, "ActorUnresolved");
        return Resolve(projectId, actorId, entry.Facts.CommitterDate, historyCutoff, roster, history);
    }

    public static HistoricalAttributionResult Resolve(Guid projectId, long? providerAccountId,
        DateTimeOffset? activityAt, DateTimeOffset historyCutoff, IReadOnlyCollection<Guid> roster,
        IReadOnlyCollection<ProjectGitMemberIdentityHistory> history)
    {
        if (providerAccountId is null or <= 0) return new(null, "MissingProviderAccountId");
        if (activityAt is null) return new(null, "MissingActivityTimestamp");
        var records = history.Where(x => x.ProjectId == projectId && x.Provider == "github" &&
            x.ExternalAccountId == providerAccountId && x.OccurredAt <= historyCutoff).ToArray();
        if (records.Length == 0) return new(null, "IdentityHistoryMissing");

        if (records.Select(x => x.UserProfileId).Distinct().Skip(1).Any())
            return new(null, "HistoricalOwnershipConflict");

        var owners = new HashSet<Guid>();
        foreach (var identity in records.GroupBy(x => x.IdentityId))
        {
            if (identity.Select(x => x.UserProfileId).Distinct().Skip(1).Any())
                return new(null, "AmbiguousIdentityHistory");
            var latestAt = identity.Max(x => x.OccurredAt);
            var latest = identity.Where(x => x.OccurredAt == latestAt).ToArray();
            if (latest.Length != 1) return new(null, "AmbiguousIdentityHistory");
            var state = latest[0];
            if (identity.Any(x => x.UserProfileId != state.UserProfileId)) return new(null, "AmbiguousIdentityHistory");
            if (state.NewStatus != ProjectGitMemberIdentityStatus.Active) continue;
            if (state.VerifiedByConnectionId is null) return new(null, "UnverifiedIdentity");
            owners.Add(state.UserProfileId);
        }
        if (owners.Count == 0) return new(null, "NoActiveOwnerAtSnapshotTime");
        if (owners.Count != 1) return new(null, "AmbiguousOwnership");
        var owner = owners.Single();
        return roster.Contains(owner) ? new(owner, "Resolved") : new(null, "OwnerOutsideRoster");
    }
}
