using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitExperimentalContributionServiceTests
{
    [Fact]
    public async Task CalculatesFromVerifiedSnapshotFactsAndFreezesAttributionInputs()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();

        var run = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);
        var retry = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);

        Assert.Equal(run.RunId, retry.RunId);
        Assert.Equal("ExperimentalGitScoringV1", run.ScoringPolicyVersion);
        Assert.Equal(64, run.InputHash.Length);
        Assert.Equal(GitContributionRunStatus.Draft, run.Status);
        Assert.All(run.Categories, category => Assert.Equal("EligibleForExperimentalMvp", category.Status));
        Assert.All(run.Categories, category => Assert.Contains("EvidenceCoverageIncomplete", category.Limitations));
        var leaderResult = Assert.Single(run.Results, x => x.UserId == fixture.Leader.Id);
        Assert.Equal(100m, leaderResult.CommitScore);
        Assert.Equal(100m, leaderResult.PullRequestScore);
        Assert.Equal(100m, leaderResult.ReviewScore);
        Assert.Equal(50m, leaderResult.IssueScore);
        Assert.Equal(92.5m, leaderResult.GitScore);
        Assert.Equal(100m, leaderResult.PeerScore);
        Assert.Equal(95.5m, leaderResult.TotalScore);
        Assert.Equal(1, Assert.Single(run.Categories, x => x.Category == "Issue").ExternalActivityCount);
        Assert.Contains("PartialPatchOverlapNotSupportedInExperimentalV1",
            Assert.Single(run.Categories, x => x.Category == "PR").Limitations);
        Assert.Contains(run.ExcludedActivities, x => x.Classification == "Corrupt" && x.Reason == "CorruptEvidence");
        Assert.Contains(run.ExcludedActivities, x => x.Classification == "Incomplete" && x.Reason == "MissingActivityTimestamp");
        Assert.Contains(run.ExcludedActivities, x => x.Classification == "Unsupported" && x.Reason == "UnsupportedIssueEvent");
        Assert.Contains(run.ExcludedActivities, x => x.Classification == "Unattributed" && x.Reason == "IdentityHistoryMissing");
        Assert.DoesNotContain(run.Results, x => x.UserId == fixture.External.Id);
        Assert.Contains(run.ExcludedActivities, x => x.Reason == "CountedUnderMergedPullRequest");
        var persisted = await fixture.Db.GitContributionRuns.SingleAsync(x => x.Id == run.RunId);
        Assert.Contains(fixture.Leader.Id.ToString(), persisted.InputSnapshotJson);
        Assert.Contains(fixture.External.Id.ToString(), persisted.InputSnapshotJson);
    }

    [Fact]
    public async Task RequiresPolicyAcknowledgmentAndRestrictsReviewFinalizationAndEvidenceDetails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();
        await Assert.ThrowsAsync<ProjectException>(() => service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, false), CancellationToken.None));

        var run = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);
        var memberCannotReview = await Assert.ThrowsAsync<ProjectException>(() => service.ReviewRunAsync(
            fixture.Project.Id, run.RunId, fixture.MemberAccount.Id,
            new("approve", null), CancellationToken.None));
        Assert.Equal(403, memberCannotReview.Status);
        var memberCannotFinalize = await Assert.ThrowsAsync<ProjectException>(() => service.FinalizeRunAsync(
            fixture.Project.Id, run.RunId, fixture.MemberAccount.Id, CancellationToken.None));
        Assert.Equal(403, memberCannotFinalize.Status);

        await service.ReviewRunAsync(fixture.Project.Id, run.RunId, fixture.LeaderAccount.Id,
            new("approve", null), CancellationToken.None);
        var finalized = await service.FinalizeRunAsync(fixture.Project.Id, run.RunId,
            fixture.LeaderAccount.Id, CancellationToken.None);
        Assert.Equal(GitContributionRunStatus.Finalized, finalized.Status);

        var memberResults = await service.GetFinalResultsAsync(fixture.Project.Id, fixture.MemberAccount.Id, CancellationToken.None);
        Assert.Equal(run.RunId, memberResults.RunId);
        Assert.Equal(2, memberResults.Results.Count);
        var memberCannotInspect = await Assert.ThrowsAsync<ProjectException>(() => service.GetRunAsync(
            fixture.Project.Id, run.RunId, fixture.MemberAccount.Id, CancellationToken.None));
        Assert.Equal(403, memberCannotInspect.Status);

        var tracked = await fixture.Db.GitContributionRuns.SingleAsync(x => x.Id == run.RunId);
        tracked.ReviewFeedback = "attempted mutation";
        Assert.Throws<InvalidOperationException>(() => fixture.Db.SaveChanges());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly FakeSnapshotAccess _snapshots;

        private Fixture(SqliteConnection connection, UniNetDbContext db, FakeSnapshotAccess snapshots,
            Account leaderAccount, UserProfile leader, Account memberAccount, UserProfile member,
            UserProfile external, Project project, Guid snapshotId)
        {
            _connection = connection;
            Db = db;
            _snapshots = snapshots;
            LeaderAccount = leaderAccount;
            Leader = leader;
            MemberAccount = memberAccount;
            Member = member;
            External = external;
            Project = project;
            SnapshotId = snapshotId;
        }

        public UniNetDbContext Db { get; }
        public Account LeaderAccount { get; }
        public UserProfile Leader { get; }
        public Account MemberAccount { get; }
        public UserProfile Member { get; }
        public UserProfile External { get; }
        public Project Project { get; }
        public Guid SnapshotId { get; }

        public IGitExperimentalContributionService CreateService() =>
            new GitExperimentalContributionService(Db, _snapshots, TimeProvider.System);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var leaderAccount = MakeAccount("git-leader");
            var memberAccount = MakeAccount("git-member");
            var externalAccount = MakeAccount("git-external");
            var leader = leaderAccount.Profile!;
            var member = memberAccount.Profile!;
            var external = externalAccount.Profile!;
            var connectionLeader = MakeConnection(leaderAccount.Id, 101);
            var connectionMember = MakeConnection(memberAccount.Id, 102);
            var connectionExternal = MakeConnection(externalAccount.Id, 999);
            var now = DateTimeOffset.UtcNow;
            var project = new Project
            {
                CreatorId = leader.Id,
                Title = "Experimental Git project",
                ProjectField = "IT",
                Description = "Experimental contribution scoring",
                MemberTarget = 2,
                Status = ProjectStatus.Completed,
                CreatedAt = now.AddDays(-20),
                UpdatedAt = now,
                CompletedAt = now
            };
            var cycle = new ContributionCycle
            {
                ProjectId = project.Id,
                OpenedById = leader.Id,
                OpenedAt = now.AddDays(-1),
                FormulaVersion = "1",
                Participants =
                [
                    new ContributionParticipant
                    {
                        UserId = leader.Id, RoleAtSnapshot = "Creator",
                        MembershipStatusAtSnapshot = ProjectMemberStatus.Active, JoinedAt = project.CreatedAt, IsLeader = true
                    },
                    new ContributionParticipant
                    {
                        UserId = member.Id, RoleAtSnapshot = "Member",
                        MembershipStatusAtSnapshot = ProjectMemberStatus.Active, JoinedAt = now.AddDays(-10)
                    }
                ]
            };
            var snapshotId = Guid.NewGuid();
            var snapshot = new ProjectGitEvidenceSnapshot
            {
                Id = snapshotId, ProjectId = project.Id, CreatedAt = now, ObservationCutoff = now.AddMinutes(2), Finalized = true
            };

            db.Accounts.AddRange(leaderAccount, memberAccount, externalAccount);
            db.GitHubOAuthConnections.AddRange(connectionLeader, connectionMember, connectionExternal);
            db.Projects.Add(project);
            db.ContributionCycles.Add(cycle);
            db.Set<ProjectGitEvidenceSnapshot>().Add(snapshot);
            await db.SaveChangesAsync();

            var identities = new[]
            {
                MakeIdentity(project.Id, leader, connectionLeader.Id, 101, now),
                MakeIdentity(project.Id, member, connectionMember.Id, 102, now),
                MakeIdentity(project.Id, external, connectionExternal.Id, 999, now)
            };
            db.ProjectGitMemberIdentities.AddRange(identities.Select(x => x.Identity));
            db.ProjectGitMemberIdentityHistory.AddRange(identities.Select(x => x.History));
            db.ContributionPeerEvaluations.AddRange(
                new ContributionPeerEvaluation { CycleId = cycle.Id, EvaluatorUserId = leader.Id, EvaluateeUserId = member.Id, Score = 1 },
                new ContributionPeerEvaluation { CycleId = cycle.Id, EvaluatorUserId = member.Id, EvaluateeUserId = leader.Id, Score = 5 });
            await db.SaveChangesAsync();

            var repository = Guid.NewGuid();
            var commitSha = new string('a', 40);
            var prCommitSha = new string('b', 40);
            var evidence = new[]
            {
                Entry(repository, "commits", "", commitSha, now,
                    Facts("commits", commitSha, "author", 101, now,
                        new GitFrozenDetails(null, null, null, null, null, null, null, null, null, null, null, null,
                            ["PrAndPatchRelationships"]))),
                Entry(repository, "commits", "", prCommitSha, now,
                    Facts("commits", prCommitSha, "author", 101, now)),
                Entry(repository, "pull-requests", "", "pr-id-10", now,
                    Facts("pull-requests", "pr-id-10", "actor", 101, now,
                        new GitFrozenDetails(null, null, null, now, null, "closed", true, null, null, null, 10, null,
                            ["PatchAndAllocationEvidence"]))),
                Entry(repository, "pull-request-commits", "10", prCommitSha, now,
                    Facts("pull-request-commits", prCommitSha, "author", 101, now,
                        new GitFrozenDetails(null, null, null, null, null, null, null, null, null, null, 10, null, []))),
                Entry(repository, "pull-request-reviews", "10", "review-1", now,
                    Facts("pull-request-reviews", "review-1", "actor", 101, now,
                        new GitFrozenDetails(null, null, null, null, now, "APPROVED", null, null, null, null, 10, null, []))),
                Entry(repository, "issues", "", "issue-1", now,
                    Facts("issues", "issue-1", "actor", 101, now,
                        new GitFrozenDetails(now, null, null, null, null, "open", null, null, null, null, null, false, []))),
                Entry(repository, "issues", "", "issue-2", now,
                    Facts("issues", "issue-2", "actor", 999, now,
                        new GitFrozenDetails(now, null, null, null, null, "open", null, null, null, null, null, false, []))),
                Entry(repository, "commits", "", new string('c', 40), now,
                    Facts("commits", new string('c', 40), "author", 101, now) with { CommitterDate = null }),
                Entry(repository, "pull-request-reviews", "10", "review-unattributed", now,
                    Facts("pull-request-reviews", "review-unattributed", "actor", 777, now,
                        new GitFrozenDetails(null, null, null, null, now, "APPROVED", null, null, null, null, 10, null, []))),
                Entry(repository, "issue-events", "1", "event-unsupported", now,
                    Facts("issue-events", "event-unsupported", "actor", 101, now,
                        new GitFrozenDetails(now, null, null, null, null, null, null, null, null, null, null, null, [],
                            EventKind: "mentioned"))),
                Entry(repository, "pull-request-reviews", "10", "review-corrupt", now, null, SnapshotEvidenceStatus.Corrupt)
            };
            var read = new ProjectGitEvidenceSnapshotRead(snapshotId, project.Id, now, now.AddMinutes(2), 1, 0, evidence);
            var snapshots = new FakeSnapshotAccess(read);
            return new Fixture(connection, db, snapshots, leaderAccount, leader, memberAccount, member, external, project, snapshotId);
        }

        private static (ProjectGitMemberIdentity Identity, ProjectGitMemberIdentityHistory History) MakeIdentity(
            Guid projectId, UserProfile user, Guid connectionId, long providerId, DateTimeOffset now)
        {
            var identity = new ProjectGitMemberIdentity
            {
                ProjectId = projectId, UserProfileId = user.Id, Provider = "github", ExternalAccountId = providerId,
                ExternalUsername = $"user-{providerId}", VerifiedByConnectionId = connectionId,
                LinkedAt = now.AddDays(-5), LastActivatedAt = now.AddDays(-5)
            };
            return (identity, new ProjectGitMemberIdentityHistory
            {
                IdentityId = identity.Id, ProjectId = projectId, UserProfileId = user.Id, Provider = "github",
                ExternalAccountId = providerId, ExternalUsername = identity.ExternalUsername,
                Action = ProjectGitMemberIdentityHistoryAction.Linked, NewStatus = ProjectGitMemberIdentityStatus.Active,
                PerformedByProfileId = user.Id, VerifiedByConnectionId = connectionId, OccurredAt = now.AddDays(-5)
            });
        }

        private static SnapshotEvidenceEntry Entry(
            Guid repository, string category, string scope, string externalId, DateTimeOffset observedAt,
            GitFrozenFacts? facts, SnapshotEvidenceStatus status = SnapshotEvidenceStatus.Incomplete)
        {
            var contentHash = facts is null
                ? new string('f', 64)
                : GitFrozenFactsCanonicalizer.Canonicalize(facts).Sha256;
            return new(Guid.NewGuid(), repository, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), category, scope,
                externalId, observedAt, contentHash, status, status == SnapshotEvidenceStatus.Incomplete ? "IncompleteSource" : status.ToString(), facts);
        }

        private static GitFrozenFacts Facts(
            string category, string externalId, string actorRole, long providerId, DateTimeOffset at, GitFrozenDetails? details = null) =>
            new(2, category, externalId, at, at, null, [new(actorRole, providerId.ToString())], "Incomplete",
                details ?? new GitFrozenDetails(null, null, null, null, null, null, null, null, null, null, null, null, []));

        private static GitHubOAuthConnection MakeConnection(Guid accountId, long providerId) => new()
        {
            AccountId = accountId, ProviderAccountId = providerId.ToString(), Login = $"user-{providerId}",
            AccessTokenEncrypted = "encrypted-test-token", GrantedScopes = "read:user"
        };

        private static Account MakeAccount(string name)
        {
            var account = new Account
            {
                Email = $"{name}@experimental.test", Role = AccountRole.Student, Status = AccountStatus.Active,
                Profile = new UserProfile
                {
                    FullName = name, Nickname = name, IsVerified = true,
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                }
            };
            account.Profile.AccountId = account.Id;
            return account;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeSnapshotAccess(ProjectGitEvidenceSnapshotRead read) : IGitContributionSnapshotAccess
    {
        public Task<Guid> CaptureAsync(Guid projectId, Guid accountId, DateTimeOffset cutoff, CancellationToken ct) =>
            Task.FromResult(read.SnapshotId);

        public Task<ProjectGitEvidenceSnapshotRead> ReadAsync(
            Guid projectId, Guid snapshotId, Guid accountId, CancellationToken ct) =>
            Task.FromResult(read);
    }
}
