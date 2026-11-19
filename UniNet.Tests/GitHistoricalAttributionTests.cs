using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHistoricalAttributionTests
{
    [Fact]
    public void SnapshotCommitRequiresValidFactsAndDoesNotFallbackToAuthorDate()
    {
        var project = Guid.NewGuid(); var user = Guid.NewGuid(); var time = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var history = new ProjectGitMemberIdentityHistory { ProjectId = project, IdentityId = Guid.NewGuid(), UserProfileId = user,
            Provider = "github", ExternalAccountId = 123, NewStatus = ProjectGitMemberIdentityStatus.Active,
            OccurredAt = time, VerifiedByConnectionId = Guid.NewGuid() };
        var facts = new GitFrozenFacts(1, "commits", "sha", time.AddDays(1), null, null, [new("author", "123")], "Complete");
        var entry = new SnapshotEvidenceEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "commits", "", facts.ExternalObjectId, time, new string('a', 64), SnapshotEvidenceStatus.Valid, "Valid", facts);
        Assert.Null(GitHistoricalAttributionService.ResolveCommit(project, entry, "author", time.AddDays(2), new[] { user }, new[] { history }).UserId);
        var resolved = entry with { Facts = facts with { CommitterDate = time.AddDays(1) } };
        Assert.Equal(user, GitHistoricalAttributionService.ResolveCommit(project, resolved, "author", time.AddDays(2), new[] { user }, new[] { history }).UserId);
        Assert.Null(GitHistoricalAttributionService.ResolveCommit(project, resolved with { Status = SnapshotEvidenceStatus.Incomplete }, "author", time.AddDays(2), new[] { user }, new[] { history }).UserId);
    }
    [Fact]
    public void RejectsAttributionWhenVerifiedOwnershipWasTransferredBetweenMembers()
    {
        var project = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var verified = Guid.NewGuid();
        var t = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        ProjectGitMemberIdentityHistory H(Guid id, Guid user, DateTimeOffset at, ProjectGitMemberIdentityStatus status, Guid? connection) => new()
        { IdentityId = id, ProjectId = project, UserProfileId = user, Provider = "github", ExternalAccountId = 123,
          NewStatus = status, OccurredAt = at, VerifiedByConnectionId = connection };
        var history = new[] { H(a, first, t, ProjectGitMemberIdentityStatus.Active, verified),
            H(a, first, t.AddDays(2), ProjectGitMemberIdentityStatus.Inactive, verified),
            H(b, second, t.AddDays(3), ProjectGitMemberIdentityStatus.Active, verified) };
        var roster = new[] { first, second };
            var beforeTransfer = GitHistoricalAttributionService.Resolve(project, 123, t.AddDays(1), t.AddDays(5), roster, history);
            var afterTransfer = GitHistoricalAttributionService.Resolve(project, 123, t.AddDays(4), t.AddDays(5), roster, history);
            Assert.Null(beforeTransfer.UserId);
            Assert.Equal("HistoricalOwnershipConflict", beforeTransfer.Reason);
            Assert.Null(afterTransfer.UserId);
            Assert.Equal("HistoricalOwnershipConflict", afterTransfer.Reason);
            Assert.Null(GitHistoricalAttributionService.Resolve(project, 999, t.AddDays(1), t.AddDays(5), roster, history).UserId);
    }

    [Fact]
    public void AttributesPastActivityToTheOnlyVerifiedOwnerAtSnapshotCutoff()
    {
        var project = Guid.NewGuid();
        var member = Guid.NewGuid();
        var identity = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var activityAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var linkedAt = activityAt.AddDays(5);
        var history = new[]
        {
            new ProjectGitMemberIdentityHistory
            {
                IdentityId = identity,
                ProjectId = project,
                UserProfileId = member,
                Provider = "github",
                ExternalAccountId = 123,
                Action = ProjectGitMemberIdentityHistoryAction.Linked,
                NewStatus = ProjectGitMemberIdentityStatus.Active,
                VerifiedByConnectionId = connection,
                OccurredAt = linkedAt
            }
        };

        var result = GitHistoricalAttributionService.Resolve(
            project, 123, activityAt, linkedAt.AddDays(1), new[] { member }, history);

        Assert.Equal(member, result.UserId);
        Assert.Equal("Resolved", result.Reason);
    }

    [Fact]
    public void RejectsHistoricalAttributionWhenGitHubAccountHasMultipleUniNetOwners()
    {
        var project = Guid.NewGuid();
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var firstIdentity = Guid.NewGuid();
        var secondIdentity = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var linkedAt = DateTimeOffset.Parse("2026-01-05T00:00:00Z");
        ProjectGitMemberIdentityHistory History(
            Guid identityId, Guid userId, DateTimeOffset occurredAt, ProjectGitMemberIdentityStatus status) => new()
        {
            IdentityId = identityId,
            ProjectId = project,
            UserProfileId = userId,
            Provider = "github",
            ExternalAccountId = 123,
            NewStatus = status,
            VerifiedByConnectionId = connection,
            OccurredAt = occurredAt
        };
        var history = new[]
        {
            History(firstIdentity, firstOwner, linkedAt, ProjectGitMemberIdentityStatus.Active),
            History(firstIdentity, firstOwner, linkedAt.AddDays(1), ProjectGitMemberIdentityStatus.Inactive),
            History(secondIdentity, secondOwner, linkedAt.AddDays(2), ProjectGitMemberIdentityStatus.Active)
        };

        var result = GitHistoricalAttributionService.Resolve(
            project, 123, linkedAt.AddDays(-1), linkedAt.AddDays(3), new[] { firstOwner, secondOwner }, history);

        Assert.Null(result.UserId);
        Assert.Equal("HistoricalOwnershipConflict", result.Reason);
    }

    [Fact]
    public void AllowsHistoricalReactivationByTheSameUniNetOwner()
    {
        var project = Guid.NewGuid();
        var member = Guid.NewGuid();
        var identity = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var linkedAt = DateTimeOffset.Parse("2026-01-05T00:00:00Z");
        var history = new[]
        {
            new ProjectGitMemberIdentityHistory
            {
                IdentityId = identity,
                ProjectId = project,
                UserProfileId = member,
                Provider = "github",
                ExternalAccountId = 123,
                NewStatus = ProjectGitMemberIdentityStatus.Active,
                VerifiedByConnectionId = connection,
                OccurredAt = linkedAt
            },
            new ProjectGitMemberIdentityHistory
            {
                IdentityId = identity,
                ProjectId = project,
                UserProfileId = member,
                Provider = "github",
                ExternalAccountId = 123,
                NewStatus = ProjectGitMemberIdentityStatus.Inactive,
                VerifiedByConnectionId = connection,
                OccurredAt = linkedAt.AddDays(1)
            },
            new ProjectGitMemberIdentityHistory
            {
                IdentityId = identity,
                ProjectId = project,
                UserProfileId = member,
                Provider = "github",
                ExternalAccountId = 123,
                NewStatus = ProjectGitMemberIdentityStatus.Active,
                VerifiedByConnectionId = connection,
                OccurredAt = linkedAt.AddDays(2)
            }
        };

        var result = GitHistoricalAttributionService.Resolve(
            project, 123, linkedAt.AddDays(-1), linkedAt.AddDays(3), new[] { member }, history);

        Assert.Equal(member, result.UserId);
        Assert.Equal("Resolved", result.Reason);
    }

    [Fact]
    public void RejectsUnverifiedAmbiguousAndOutsideRoster()
    {
        var p = Guid.NewGuid(); var u = Guid.NewGuid(); var t = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        ProjectGitMemberIdentityHistory H(Guid owner, Guid? c) => new() { IdentityId = Guid.NewGuid(), ProjectId = p, UserProfileId = owner,
            Provider = "github", ExternalAccountId = 1, NewStatus = ProjectGitMemberIdentityStatus.Active, OccurredAt = t, VerifiedByConnectionId = c };
        Assert.Null(GitHistoricalAttributionService.Resolve(p, 1, t, t.AddDays(1), new[] { u }, new[] { H(u, null) }).UserId);
        Assert.Null(GitHistoricalAttributionService.Resolve(p, 1, t, t.AddDays(1), Array.Empty<Guid>(), new[] { H(u, Guid.NewGuid()) }).UserId);
        var other = Guid.NewGuid();
        Assert.Null(GitHistoricalAttributionService.Resolve(p, 1, t, t.AddDays(1), new[] { u, other }, new[] { H(u, Guid.NewGuid()), H(other, Guid.NewGuid()) }).UserId);
        Assert.Null(GitHistoricalAttributionService.Resolve(p, 1, null, t.AddDays(1), new[] { u }, new[] { H(u, Guid.NewGuid()) }).UserId);
        var foreignProjectHistory = H(u, Guid.NewGuid());
        foreignProjectHistory.ProjectId = Guid.NewGuid();
        var foreignProject = GitHistoricalAttributionService.Resolve(
            p, 1, t, t.AddDays(1), new[] { u }, new[] { foreignProjectHistory });
        Assert.Null(foreignProject.UserId);
        Assert.Equal("IdentityHistoryMissing", foreignProject.Reason);
        var afterCutoffHistory = H(u, Guid.NewGuid());
        afterCutoffHistory.OccurredAt = t.AddDays(2);
        var afterCutoff = GitHistoricalAttributionService.Resolve(
            p, 1, t, t.AddDays(1), new[] { u }, new[] { afterCutoffHistory });
        Assert.Null(afterCutoff.UserId);
        Assert.Equal("IdentityHistoryMissing", afterCutoff.Reason);
    }
}
