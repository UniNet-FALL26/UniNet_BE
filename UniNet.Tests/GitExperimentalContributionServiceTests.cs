using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Text.Json;
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
    public async Task GitOnlyDiagnosticUsesFrozenInputsWithoutPeerValuesOrWritingARun()
    {
        await using var f = await Fixture.CreateAsync(completeV2: true);
        await f.AddProjectMemberAsync(f.Member);
        var service = (GitExperimentalContributionService)f.CreateService();
        var json = await service.DiagnoseGitAsync(f.Project.Id, f.SnapshotId, f.LeaderAccount.Id, [f.Leader.Id, f.Member.Id], default);
        var again = await service.DiagnoseGitAsync(f.Project.Id, f.SnapshotId, f.LeaderAccount.Id, [f.Leader.Id, f.Member.Id], default);
        using var result = JsonDocument.Parse(json);
        using var repeated = JsonDocument.Parse(again);
        Assert.True(result.RootElement.GetProperty("diagnosticOnly").GetBoolean());
        Assert.Equal(result.RootElement.GetProperty("inputHash").GetString(), repeated.RootElement.GetProperty("inputHash").GetString());
        Assert.Equal(64, result.RootElement.GetProperty("snapshotMembershipHash").GetString()!.Length);
        Assert.NotEmpty(result.RootElement.GetProperty("scoredActivities").EnumerateArray());
        Assert.All(result.RootElement.GetProperty("results").EnumerateArray(), row =>
        {
            Assert.False(row.TryGetProperty("peerScore", out _));
            Assert.False(row.TryGetProperty("totalScore", out _));
            Assert.False(row.TryGetProperty("contributionPercent", out _));
        });
        Assert.Empty(await f.Db.GitContributionRuns.ToListAsync());
        var denied = await Assert.ThrowsAsync<ProjectException>(() => service.DiagnoseGitAsync(
            f.Project.Id, f.SnapshotId, f.MemberAccount.Id, [f.Leader.Id, f.Member.Id], default));
        Assert.Equal(403, denied.Status);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V2FreezesSeparateGitPeerNormalizationAndPreservesExternalShare(bool external)
    {
        await using var f = await Fixture.CreateAsync(completeV2: true, includeExternalV2: external);
        await f.AddProjectMemberAsync(f.Member);
        var peers = new ExperimentalPeerCohortService(f.Db, TimeProvider.System);
        var cohort = await peers.OpenAsync(f.Project.Id, f.LeaderAccount.Id, new([f.Member.Id]), default);
        await peers.SubmitAsync(f.Project.Id, cohort.Id, f.LeaderAccount.Id, f.Member.Id, new(4, null, null), default);
        await peers.SubmitAsync(f.Project.Id, cohort.Id, f.MemberAccount.Id, f.Leader.Id, new(3, null, null), default);
        var peerId = await peers.CloseAsync(f.Project.Id, cohort.Id, f.LeaderAccount.Id, default);
        var request = new GitExperimentalRunRequest(f.SnapshotId, true, null, "ExperimentalGitScoringV2", peerId);
        var run = await f.CreateService().CreateRunAsync(f.Project.Id, f.LeaderAccount.Id, request, default);
        Assert.Equal("Ready",run.CalculationStatus);
        Assert.Equal(GitPeerNormalization.Version,run.NormalizationPolicyVersion);
        Assert.Equal(external ? 4.5m : 0m,run.ExternalContributionPercent);
        Assert.Equal(external ? 71.5m : 76m,Assert.Single(run.Results,x=>x.UserId==f.Leader.Id).ContributionPercent);
        Assert.Equal(24m,Assert.Single(run.Results,x=>x.UserId==f.Member.Id).ContributionPercent);
        Assert.Equal(100m,run.Results.Sum(x=>x.ContributionPercent)+run.ExternalContributionPercent);
        Assert.All(run.Results,x=>Assert.Equal(x.TotalScore,x.RawTotalScore));
        var retry = await f.CreateService().CreateRunAsync(f.Project.Id,f.LeaderAccount.Id,request,default);
        Assert.Equal(run.RunId,retry.RunId);
        var stored=await f.Db.GitContributionRuns.SingleAsync();
        Assert.Contains(GitPeerNormalization.Version,stored.InputSnapshotJson);
        Assert.NotNull(stored.NormalizedResultsJson);
    }
    [Fact]
    public async Task V2DoesNotCreateAZeroPeerResultWhenEvaluationsAreMissing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<ProjectException>(() => fixture.CreateService().CreateRunAsync(fixture.Project.Id,
            fixture.LeaderAccount.Id, new(fixture.SnapshotId, true, null, "ExperimentalGitScoringV2"), default));
        Assert.Equal("PEER_SNAPSHOT_REQUIRED", error.Code); Assert.Empty(await fixture.Db.GitContributionRuns.ToListAsync());
    }
    [Fact]
    public async Task PeerCohortUsesRealAuthenticatedSubmissionsEditsPrivacyAndLeaderClosure()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddProjectMemberAsync(fixture.Member);
        var peers = new ExperimentalPeerCohortService(fixture.Db, TimeProvider.System);
        var cohort = await peers.OpenAsync(fixture.Project.Id, fixture.LeaderAccount.Id, new([fixture.Member.Id]), default);
        Assert.Equal(2, cohort.ParticipantIds.Count);
        await Assert.ThrowsAsync<ProjectException>(() => peers.SubmitAsync(fixture.Project.Id, cohort.Id, fixture.LeaderAccount.Id, fixture.Leader.Id, new(5, null, null), default));
        await peers.SubmitAsync(fixture.Project.Id, cohort.Id, fixture.LeaderAccount.Id, fixture.Member.Id, new(5, "first", null), default);
        await peers.SubmitAsync(fixture.Project.Id, cohort.Id, fixture.LeaderAccount.Id, fixture.Member.Id, new(4, "edited", 1), default);
        await peers.SubmitAsync(fixture.Project.Id, cohort.Id, fixture.MemberAccount.Id, fixture.Leader.Id, new(3, "member", null), default);
        var memberView = await peers.ReadAsync(fixture.Project.Id, cohort.Id, fixture.MemberAccount.Id, default);
        Assert.Single(memberView.Ratings); Assert.Empty(memberView.History);
        Assert.Equal(fixture.Member.Id, memberView.Ratings[0].EvaluatorId);
        await Assert.ThrowsAsync<ProjectException>(() => peers.CloseAsync(fixture.Project.Id, cohort.Id, fixture.MemberAccount.Id, default));
        var leaderView = await peers.ReadAsync(fixture.Project.Id, cohort.Id, fixture.LeaderAccount.Id, default);
        Assert.Equal(2, leaderView.Ratings.Count); Assert.Equal(3, leaderView.History.Count);
        var snapshot = await peers.CloseAsync(fixture.Project.Id, cohort.Id, fixture.LeaderAccount.Id, default);
        Assert.NotEqual(Guid.Empty, snapshot);
        var v2Request = new GitExperimentalRunRequest(fixture.SnapshotId, true, null, "ExperimentalGitScoringV2", snapshot);
        var v2 = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id, v2Request, default);
        var repeated = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id, v2Request, default);
        Assert.Equal(v2.RunId, repeated.RunId); Assert.Equal(snapshot, v2.PeerSnapshotId);
        Assert.Equal(50m, Assert.Single(v2.Results, x => x.UserId == fixture.Leader.Id).PeerScore);
        Assert.Equal(75m, Assert.Single(v2.Results, x => x.UserId == fixture.Member.Id).PeerScore);
        var another = await peers.OpenAsync(fixture.Project.Id, fixture.LeaderAccount.Id, new([fixture.Member.Id]), default);
        await peers.SubmitAsync(fixture.Project.Id, another.Id, fixture.LeaderAccount.Id, fixture.Member.Id, new(5, null, null), default);
        await peers.SubmitAsync(fixture.Project.Id, another.Id, fixture.MemberAccount.Id, fixture.Leader.Id, new(5, null, null), default);
        var anotherSnapshot = await peers.CloseAsync(fixture.Project.Id, another.Id, fixture.LeaderAccount.Id, default);
        var anotherRun = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            v2Request with { PeerSnapshotId = anotherSnapshot }, default);
        Assert.NotEqual(v2.RunId, anotherRun.RunId);
        Assert.Equal(v2.InputHash, (await fixture.CreateService().GetRunAsync(fixture.Project.Id, v2.RunId, fixture.LeaderAccount.Id, default)).InputHash);
        await Assert.ThrowsAsync<ProjectException>(() => peers.SubmitAsync(fixture.Project.Id, cohort.Id, fixture.MemberAccount.Id, fixture.Leader.Id, new(5, null, 1), default));
        Assert.Equal(2, await fixture.Db.ContributionParticipants.CountAsync());
        Assert.Equal(2, await fixture.Db.ContributionPeerEvaluations.CountAsync());
    }

    [Fact]
    public async Task IdempotentRetryIgnoresJsonbPropertyOrdering()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();
        var first = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);
        var row = await fixture.Db.GitContributionRuns.SingleAsync();
        using var document = JsonDocument.Parse(row.InputSnapshotJson);
        row.InputSnapshotJson = JsonSerializer.Serialize(document.RootElement.EnumerateObject()
            .OrderBy(x => x.Name, StringComparer.Ordinal).ToDictionary(x => x.Name, x => x.Value.Clone()));
        await fixture.Db.SaveChangesAsync();
        var retry = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);
        Assert.Equal(first.RunId, retry.RunId);
        Assert.Single(await fixture.Db.GitContributionRuns.ToListAsync());
    }

    [Fact]
    public async Task PullRequestCannotBorrowCommitRelationshipsFromAnotherRepositoryWithTheSameNumber()
    {
        await using var fixture = await Fixture.CreateAsync(prRelationInOtherRepository: true);
        var run = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);
        Assert.Equal(0m, Assert.Single(run.Results, x => x.UserId == fixture.Leader.Id).PullRequestScore);
        Assert.Contains(run.ExcludedActivities, x => x.ObjectId == "pr-id-10" && x.Reason == "PullRequestCommitEvidenceMissing");
    }

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
    public async Task CreatesRunWithoutLoadingTheOptionalProjectSpecializationCatalog()
    {
        await using var fixture = await Fixture.CreateAsync(rejectProjectSpecializationColumn: true);
        await fixture.Db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await fixture.Db.Database.ExecuteSqlRawAsync("DROP TABLE \"Specializations\"");

        var run = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);

        Assert.Equal(GitContributionRunStatus.Draft, run.Status);
    }

    [Fact]
    public async Task AttributesSnapshotActivityWhenVerifiedIdentityWasLinkedAfterTheActivity()
    {
        await using var fixture = await Fixture.CreateAsync(linkLeaderIdentityAfterActivity: true);

        var run = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);

        var leaderResult = Assert.Single(run.Results, x => x.UserId == fixture.Leader.Id);
        Assert.Equal(100m, leaderResult.CommitScore);
        Assert.DoesNotContain(run.ExcludedActivities, x =>
            x.ObjectId == fixture.LeaderCommitSha && x.Reason == "IdentityHistoryMissing");
    }

    [Fact]
    public async Task ExcludesActivityAfterTheFrozenSnapshotCutoff()
    {
        await using var fixture = await Fixture.CreateAsync(leaderActivityAfterSnapshotCutoff: true);

        var run = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);

        Assert.Contains(run.ExcludedActivities, x =>
            x.ObjectId == fixture.LeaderCommitSha && x.Reason == "ActivityAfterSnapshotCutoff");
    }

    [Fact]
    public async Task RejectsAnExistingRunWhenItsFrozenScoringInputsDoNotMatchTheRequestedRoster()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService();
        var original = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true), CancellationToken.None);
        await fixture.AddExternalAsMemberAsync();

        var error = await Assert.ThrowsAsync<ProjectException>(() => service.CreateRunAsync(
            fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true, [fixture.External.Id]), CancellationToken.None));

        Assert.Equal("GIT_CONTRIBUTION_INPUT_MISMATCH", error.Code);
        Assert.Equal(409, error.Status);
        Assert.Equal(original.RunId, (await fixture.Db.GitContributionRuns.SingleAsync()).Id);
    }

    [Fact]
    public async Task CreatesDraftForEffectiveRosterWithoutReusingLegacyPeerEvaluations()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddExternalAsMemberAsync();

        var run = await fixture.CreateService().CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true, [fixture.External.Id, fixture.External.Id]), CancellationToken.None);
        using var input = JsonDocument.Parse((await fixture.Db.GitContributionRuns.SingleAsync()).InputSnapshotJson);
        var rosterIds = input.RootElement.GetProperty("roster").EnumerateArray()
            .Select(x => x.GetProperty("userId").GetGuid()).ToArray();

        Assert.Equal(GitContributionRunStatus.Draft, run.Status);
        Assert.Equal(2, run.Results.Count);
        Assert.Equal(2, rosterIds.Distinct().Count());
        Assert.Contains(fixture.Leader.Id, rosterIds);
        Assert.Contains(fixture.External.Id, rosterIds);
        Assert.Equal("effectiveProjectMembership", input.RootElement.GetProperty("rosterSource").GetString());
        Assert.Empty(input.RootElement.GetProperty("peerEvaluations").EnumerateArray());
        Assert.All(run.Results, result => Assert.Equal(0m, result.PeerScore));
    }

    [Fact]
    public async Task RejectsAlternateRosterThatWasNotActiveByTheSnapshotCutoff()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddExternalAsMemberAsync(fixture.Project.CompletedAt!.Value.AddMinutes(3));

        var error = await Assert.ThrowsAsync<ProjectException>(() => fixture.CreateService().CreateRunAsync(
            fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true, [fixture.External.Id]), CancellationToken.None));

        Assert.Equal("GIT_CONTRIBUTION_ROSTER_INVALID", error.Code);
    }

    [Fact]
    public async Task AlternateRosterCannotBeReviewedOrFinalized()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddExternalAsMemberAsync();
        var service = fixture.CreateService();
        var run = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true, [fixture.External.Id]), CancellationToken.None);

        var reviewError = await Assert.ThrowsAsync<ProjectException>(() => service.ReviewRunAsync(
            fixture.Project.Id, run.RunId, fixture.LeaderAccount.Id, new("approve", null), CancellationToken.None));
        Assert.Equal("GIT_CONTRIBUTION_ALTERNATE_ROSTER_NOT_OFFICIAL", reviewError.Code);

        var tracked = await fixture.Db.GitContributionRuns.SingleAsync(x => x.Id == run.RunId);
        tracked.Status = GitContributionRunStatus.Review;
        tracked.ReviewedById = fixture.Leader.Id;
        tracked.ReviewedAt = DateTimeOffset.UtcNow;
        await fixture.Db.SaveChangesAsync();

        var finalizeError = await Assert.ThrowsAsync<ProjectException>(() => service.FinalizeRunAsync(
            fixture.Project.Id, run.RunId, fixture.LeaderAccount.Id, CancellationToken.None));
        Assert.Equal("GIT_CONTRIBUTION_ALTERNATE_ROSTER_NOT_OFFICIAL", finalizeError.Code);
        Assert.Equal(GitContributionRunStatus.Review,
            (await fixture.Db.GitContributionRuns.SingleAsync(x => x.Id == run.RunId)).Status);
    }

    [Fact]
    public async Task ExplicitDuplicatesOfOfficialRosterUseTheExistingCycleRosterAndRemainIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddProjectMemberAsync(fixture.Member);
        var service = fixture.CreateService();

        var run = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true, [fixture.Leader.Id, fixture.Member.Id, fixture.Member.Id]), CancellationToken.None);
        var retry = await service.CreateRunAsync(fixture.Project.Id, fixture.LeaderAccount.Id,
            new(fixture.SnapshotId, true, [fixture.Member.Id, fixture.Leader.Id]), CancellationToken.None);
        using var input = JsonDocument.Parse((await fixture.Db.GitContributionRuns.SingleAsync()).InputSnapshotJson);

        Assert.Equal(run.RunId, retry.RunId);
        Assert.Equal("contributionCycle", input.RootElement.GetProperty("rosterSource").GetString());
        Assert.Equal(2, run.Results.Count);
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
        public string LeaderCommitSha => new('a', 40);

        public IGitExperimentalContributionService CreateService() =>
            new GitExperimentalContributionService(Db, _snapshots, TimeProvider.System);

        public Task AddExternalAsMemberAsync(DateTimeOffset? joinedAt = null) =>
            AddProjectMemberAsync(External, joinedAt: joinedAt);

        public async Task AddProjectMemberAsync(
            UserProfile member, DateTimeOffset? joinedAt = null)
        {
            var membershipDate = joinedAt ?? Project.CreatedAt.AddDays(10);
            Db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = Project.Id,
                UserId = member.Id,
                Role = "Member",
                Status = ProjectMemberStatus.Active,
                JoinedAt = membershipDate,
                CreatedAt = membershipDate,
                UpdatedAt = membershipDate
            });
            await Db.SaveChangesAsync();
        }

        public static async Task<Fixture> CreateAsync(
            bool linkLeaderIdentityAfterActivity = false,
            bool leaderActivityAfterSnapshotCutoff = false,
            bool rejectProjectSpecializationColumn = false,
            bool prRelationInOtherRepository = false,
            bool completeV2 = false,
            bool includeExternalV2 = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection);
            if (rejectProjectSpecializationColumn)
                options.AddInterceptors(new RejectProjectSpecializationColumnInterceptor());
            var db = new UniNetDbContext(options.Options);
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
            if (linkLeaderIdentityAfterActivity)
            {
                var linkAt = now.AddMinutes(1);
                identities[0].Identity.LinkedAt = linkAt;
                identities[0].Identity.LastActivatedAt = linkAt;
                identities[0].History.OccurredAt = linkAt;
            }
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
                Entry(prRelationInOtherRepository ? Guid.NewGuid() : repository, "pull-request-commits", "10", prCommitSha, now,
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
            if (completeV2)
            {
                evidence = evidence.Where(e => e.Facts is not null && e.ExternalObjectId is not ("review-unattributed" or "event-unsupported" or "review-corrupt") &&
                    !(e.Category == "commits" && e.Facts.CommitterDate is null) && (includeExternalV2 || e.ExternalObjectId != "issue-2"))
                    .Select(e => {
                        var facts = e.Facts! with { SchemaVersion = 4, Completeness = "Complete", Details = e.Facts.Details! with {
                            MissingFacts = [], ParentShas = e.Category == "commits" ? [new string('d',40)] : null } };
                        return e with { Facts = facts, Status = SnapshotEvidenceStatus.Valid, Reason = "Valid", ContentHash = GitFrozenFactsCanonicalizer.Canonicalize(facts).Sha256 };
                    }).ToArray();
                // A valid PR/commit retrieval shares one association and job in PostgreSQL.
                var sourceJob = Guid.NewGuid();
                var sourceAssociation = Guid.NewGuid();
                evidence = evidence.Select(e => e with { JobId = sourceJob, AssociationId = sourceAssociation }).ToArray();
            }
            if (leaderActivityAfterSnapshotCutoff)
            {
                var index = Array.FindIndex(evidence, x => x.ExternalObjectId == new string('a', 40));
                var entry = evidence[index];
                var facts = entry.Facts! with { CommitterDate = now.AddMinutes(3) };
                evidence[index] = entry with
                {
                    Facts = facts,
                    ContentHash = GitFrozenFactsCanonicalizer.Canonicalize(facts).Sha256
                };
            }
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

    private sealed class RejectProjectSpecializationColumnInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("\"Projects\"", StringComparison.Ordinal) &&
                command.CommandText.Contains("\"SpecializationId\"", StringComparison.Ordinal))
                throw new InvalidOperationException("The contribution project query must not load optional specialization data.");

            return ValueTask.FromResult(result);
        }
    }
}
