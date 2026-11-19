using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Data.Common;
using System.Text;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class SnapshotPostgresTheoryAttribute : TheoryAttribute
{
    public SnapshotPostgresTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("UNINET_SNAPSHOT_TEST_PORT") is null)
            Skip = "Requires explicitly verified snapshot disposable PostgreSQL.";
    }
}

public sealed class ProjectGitEvidenceSnapshotPostgresTests
{
    [SnapshotPostgresTheory]
    [InlineData(true)]
    public async Task LegacyCaptureWithoutCoverageSchemaIsExplicitlyPolicyOne(bool legacy)
    {
        Assert.True(legacy);
        if (!int.TryParse(Environment.GetEnvironmentVariable("UNINET_SNAPSHOT_TEST_PORT"), out var port) || port is <= 1024 or 55433 or > 65535)
            throw new InvalidOperationException("Unsafe legacy snapshot target.");
        const string name = "uninet_snapshot_legacy_coverage_disposable";
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql($"Host=127.0.0.1;Port={port};Database={name};Username=postgres;SSL Mode=Disable").Options);
        await db.Database.OpenConnectionAsync(); var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        Assert.Equal(name, connection.Database); Assert.Equal(port, connection.Port); Assert.Equal("postgres", connection.UserName);
        await db.GetService<IMigrator>().MigrateAsync("20261118152617_GuardGitV2FinalNormalizationReadiness");
        var owner = new Account { Email = $"legacy-coverage-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
            Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Legacy snapshot", ProjectField = "IT", Description = "fixture",
            MemberTarget = 2, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Status = ProjectStatus.Active };
        db.AddRange(owner, project); await db.SaveChangesAsync();
        var id = await new ProjectGitEvidenceSnapshotService(db).CaptureAsync(project.Id, owner.Id, DateTimeOffset.UtcNow, default);
        var read = await new ProjectGitEvidenceSnapshotReader(db).ReadAsync(project.Id, id, owner.Id, default);
        Assert.Equal(1, read.PolicyVersion); Assert.Empty(read.Entries); Assert.Empty(read.Coverage!);
        Assert.False(GitSnapshotCoverageValidation.HasVerifiedEmptyReviewCoverage(read));
    }
    private sealed class FailAfterEntries : SaveChangesInterceptor
    {
        public Guid SnapshotId { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            var entry = data.Context!.ChangeTracker.Entries<ProjectGitEvidenceSnapshotEntry>().FirstOrDefault();
            if (entry != null) { SnapshotId = entry.Entity.SnapshotId; throw new InvalidOperationException("injected after entries"); }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class CaptureGate : DbCommandInterceptor
    {
        public TaskCompletionSource Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FROM \"ProjectEvidenceObservations\"", StringComparison.Ordinal))
            { Seen.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            return result;
        }
    }
    [SnapshotPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureFreezesMembershipAndRejectsLaterMutations(bool upgrade)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("UNINET_SNAPSHOT_TEST_PORT"), out var port) || port is <= 1024 or 55433 or > 65535)
            throw new InvalidOperationException("Unsafe snapshot test target.");
        var database = upgrade ? "uninet_snapshot_upgrade_disposable" : "uninet_snapshot_disposable";
        var cs = $"Host=127.0.0.1;Port={port};Database={database};Username=postgres;SSL Mode=Disable";
        var options = new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(cs).Options;
        await using var db = new UniNetDbContext(options);
        await db.Database.OpenConnectionAsync();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        Assert.Equal("127.0.0.1", conn.Host); Assert.Equal(port, conn.Port);
        Assert.Equal(database, conn.Database); Assert.Equal("postgres", conn.UserName);
        var owner = new Account { Email = $"snapshot-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Snapshot", ProjectField = "IT", Description = "fixture", MemberTarget = 2, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Status = ProjectStatus.Active };
        var seed = Random.Shared.NextInt64(1000000, long.MaxValue / 4);
        var install = new GitHubAppInstallation { ProviderInstallationId = seed, OwnerAccountId = 9, OwnerLogin = "fixture", OwnerType = "User" };
        var repo = new GitHubRepository { ProviderRepositoryId = seed + 1, ProviderOwnerId = 9, OwnerLogin = "fixture", Name = "snapshot", FullName = "fixture/snapshot", HtmlUrl = "https://example.test/repo" };
        var grant = new GitHubAppRepositoryGrant { InstallationId = install.Id, ProviderRepositoryId = repo.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = true, AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization };
        var job = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repo.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial };
        var obj = new GitHubProviderObject { GitHubRepositoryId = repo.Id, Category = "commits", ExternalObjectId = "sha", PayloadJson = "{}", RetrievalMetadataJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        var version = new GitEvidenceVersion { GitHubProviderObjectId = obj.Id, GitHubRepositoryId = repo.Id, EvidenceSchemaVersion = 1, CanonicalizationVersion = 1, ContentHash = new string('a', 64), FrozenFactsJson = "{}", EvidenceStatus = "Incomplete" };
        ProjectEvidenceObservation Observation(string scope) => new() { ProjectGitRepositoryId = link.Id, GitSyncJobId = job.Id, GitEvidenceVersionId = version.Id, GitHubRepositoryId = repo.Id, Category = "commits", ScopeKey = scope, AuthorizationSource = "app", RetrievalCompleteness = "Incomplete", ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var first = Observation("first");
        var otherOwner = new Account { Email = $"other-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Other", Nickname = "Other" } };
        var otherProject = new Project { Creator = otherOwner.Profile!, Title = "Other", ProjectField = "IT", Description = "fixture", MemberTarget = 2, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Status = ProjectStatus.Active };
        var otherLink = new ProjectGitRepository { ProjectId = otherProject.Id, GitHubRepositoryId = repo.Id, LinkedByProfileId = otherOwner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncPaused = true, AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization };
        var otherJob = new GitSyncJob { ProjectGitRepositoryId = otherLink.Id, GitHubRepositoryId = repo.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial };
        var otherObservation = new ProjectEvidenceObservation { ProjectGitRepositoryId = otherLink.Id, GitSyncJobId = otherJob.Id, GitEvidenceVersionId = version.Id, GitHubRepositoryId = repo.Id, Category = "commits", ScopeKey = "other", AuthorizationSource = "app", RetrievalCompleteness = "Incomplete", ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        db.AddRange(owner, project, otherOwner, otherProject, install, repo, grant, link, otherLink, job, otherJob, obj, version, first, otherObservation); await db.SaveChangesAsync();
        if (upgrade)
        {
            Assert.DoesNotContain("20261114151603_AddProjectGitEvidenceSnapshots", await db.Database.GetAppliedMigrationsAsync());
            await db.Database.MigrateAsync();
            Assert.True(await db.GitEvidenceVersions.AnyAsync(x => x.Id == version.Id && x.FrozenFactsJson == "{}"));
            Assert.True(await db.ProjectEvidenceObservations.AnyAsync(x => x.Id == first.Id));
        }
        job.Status = GitSyncJobStatus.Completed; job.CompletedAt = DateTimeOffset.UtcNow;
        var scopeReceipt = new GitSyncCheckpoint { SyncJobId = job.Id, Category = "pull-requests", ScopeKey = "",
            Status = GitSyncCheckpointStatus.Completed, ItemsProcessed = 0, LastProcessedAt = job.CompletedAt.Value.AddSeconds(-1) };
        db.Add(scopeReceipt); await db.SaveChangesAsync();
        await using var writer = new UniNetDbContext(options);
        await using var tx = await writer.Database.BeginTransactionAsync();
        var late = Observation("late"); writer.Add(late); await writer.SaveChangesAsync();
        var cutoff = DateTimeOffset.UtcNow;
        await using var capture = new UniNetDbContext(options);
        var id = await new ProjectGitEvidenceSnapshotService(capture).CaptureAsync(project.Id, owner.Id, cutoff, default);
        await tx.CommitAsync();
        await using var verify = new UniNetDbContext(options);
        var members = await verify.Set<ProjectGitEvidenceSnapshotEntry>().Where(x => x.SnapshotId == id).ToListAsync();
        Assert.Single(members); Assert.Equal(first.Id, members[0].ProjectEvidenceObservationId);
        var frozenCoverage = await verify.Set<ProjectGitEvidenceSnapshotCoverage>().SingleAsync(x => x.SnapshotId == id);
        Assert.Equal(scopeReceipt.Id, frozenCoverage.GitSyncCheckpointId);
        Assert.Equal(0, frozenCoverage.ItemsProcessed);
        Assert.Equal(2, (await verify.Set<ProjectGitEvidenceSnapshot>().SingleAsync(x => x.Id == id)).CapturePolicyVersion);
        await using (var bad = new UniNetDbContext(options))
        await using (var badTx = await bad.Database.BeginTransactionAsync())
        {
            var invalidReceiptSnapshot = new ProjectGitEvidenceSnapshot { ProjectId = project.Id, ObservationCutoff = cutoff, CapturePolicyVersion = 2 };
            bad.Add(invalidReceiptSnapshot); await bad.SaveChangesAsync();
            bad.Add(new ProjectGitEvidenceSnapshotCoverage { SnapshotId = invalidReceiptSnapshot.Id, ProjectId = project.Id,
                ProjectGitRepositoryId = link.Id, GitSyncJobId = job.Id, GitSyncCheckpointId = scopeReceipt.Id,
                Category = scopeReceipt.Category, ScopeKey = scopeReceipt.ScopeKey, ItemsProcessed = 99,
                ScopeCompletedAt = scopeReceipt.LastProcessedAt!.Value, JobCompletedAt = job.CompletedAt!.Value });
            var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => bad.SaveChangesAsync());
            Assert.Equal("23514", Assert.IsType<PostgresException>(rejected.InnerException).SqlState);
            await badTx.RollbackAsync();
            Assert.False(await verify.Set<ProjectGitEvidenceSnapshot>().AnyAsync(x => x.Id == invalidReceiptSnapshot.Id));
        }
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"ProjectGitEvidenceSnapshotCoverage\" WHERE \"SnapshotId\"={id}"))).SqlState);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ProjectGitEvidenceSnapshotCoverage\" SET \"ItemsProcessed\"=99 WHERE \"SnapshotId\"={id}"))).SqlState);
        await verify.GitSyncCheckpoints.Where(x => x.Id == scopeReceipt.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemsProcessed, 1));
        Assert.Equal(0, (await verify.Set<ProjectGitEvidenceSnapshotCoverage>().AsNoTracking().SingleAsync(x => x.SnapshotId == id)).ItemsProcessed);
        await using var later = new UniNetDbContext(options);
        var laterId = await new ProjectGitEvidenceSnapshotService(later).CaptureAsync(project.Id, owner.Id, cutoff, default);
        Assert.Equal(2, await verify.Set<ProjectGitEvidenceSnapshotEntry>().CountAsync(x => x.SnapshotId == laterId));
        Assert.Equal(1, await verify.Set<ProjectGitEvidenceSnapshotEntry>().CountAsync(x => x.SnapshotId == id));
        var ex = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ProjectGitEvidenceSnapshotEntries\" WHERE \"SnapshotId\" = {id}"));
        Assert.Equal("55000", ex.SqlState);
        var ex2 = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectGitEvidenceSnapshots\" SET \"Finalized\" = false WHERE \"Id\" = {id}"));
        Assert.Equal("55000", ex2.SqlState);
        var ex3 = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ProjectGitEvidenceSnapshotEntries\" (\"SnapshotId\", \"ProjectId\", \"ProjectEvidenceObservationId\", \"GitEvidenceVersionId\", \"ProjectGitRepositoryId\") VALUES ({id}, {project.Id}, {late.Id}, {version.Id}, {link.Id})"));
        Assert.Equal("55000", ex3.SqlState);
        await using var unauthorized = new UniNetDbContext(options);
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => new ProjectGitEvidenceSnapshotService(unauthorized).CaptureAsync(project.Id, Guid.NewGuid(), cutoff, default));
        Assert.Equal(2, await verify.Set<ProjectGitEvidenceSnapshot>().CountAsync(x => x.ProjectId == project.Id));
        Assert.False(await verify.Set<ProjectGitEvidenceSnapshotEntry>().AnyAsync(x => x.SnapshotId == id && x.ProjectEvidenceObservationId == otherObservation.Id));
        await using var invalid = new UniNetDbContext(options);
        await using var invalidTx = await invalid.Database.BeginTransactionAsync();
        var draft = new ProjectGitEvidenceSnapshot { ProjectId = project.Id, ObservationCutoff = cutoff };
        invalid.Add(draft); await invalid.SaveChangesAsync();
        invalid.Add(new ProjectGitEvidenceSnapshotEntry { SnapshotId = draft.Id, ProjectId = project.Id, ProjectGitRepositoryId = otherLink.Id, ProjectEvidenceObservationId = otherObservation.Id, GitEvidenceVersionId = version.Id });
        var mismatch = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
        Assert.Equal("23503", Assert.IsType<PostgresException>(mismatch.InnerException).SqlState);
        await invalidTx.RollbackAsync();
        Assert.False(await verify.Set<ProjectGitEvidenceSnapshot>().AnyAsync(x => x.Id == draft.Id));
        async Task<Guid> ConcurrentCapture()
        {
            await using var concurrent = new UniNetDbContext(options);
            return await new ProjectGitEvidenceSnapshotService(concurrent).CaptureAsync(project.Id, owner.Id, cutoff, default);
        }
        var concurrentIds = await Task.WhenAll(ConcurrentCapture(), ConcurrentCapture());
        foreach (var captureId in concurrentIds)
        {
            var captured = await verify.Set<ProjectGitEvidenceSnapshotEntry>().Where(x => x.SnapshotId == captureId)
                .Select(x => x.ProjectEvidenceObservationId).ToListAsync();
            Assert.Equal(new[] { first.Id, late.Id }.Order(), captured.Order());
        }

        var repo2 = new GitHubRepository { ProviderRepositoryId = seed + 2, ProviderOwnerId = 9, OwnerLogin = "fixture", Name = "snapshot2", FullName = "fixture/snapshot2", HtmlUrl = "https://example.test/repo2" };
        var grant2 = new GitHubAppRepositoryGrant { InstallationId = install.Id, ProviderRepositoryId = repo2.ProviderRepositoryId, VerifiedAt = cutoff };
        var historical = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repo2.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant2.Id, SyncPaused = true, AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization, LinkedAt = cutoff.AddMinutes(-2), UnlinkedAt = cutoff };
        var job2 = new GitSyncJob { ProjectGitRepositoryId = historical.Id, GitHubRepositoryId = repo2.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant2.Id, SyncType = GitSyncType.Initial };
        var object2 = new GitHubProviderObject { GitHubRepositoryId = repo2.Id, Category = "commits", ExternalObjectId = "sha2", PayloadJson = "{}", RetrievalMetadataJson = "{}", FirstObservedAt = cutoff, LastObservedAt = cutoff };
        var version2 = new GitEvidenceVersion { GitHubProviderObjectId = object2.Id, GitHubRepositoryId = repo2.Id, EvidenceSchemaVersion = 1, CanonicalizationVersion = 1, ContentHash = new string('b', 64), FrozenFactsJson = "{}", EvidenceStatus = "Incomplete" };
        var historicalObs = new ProjectEvidenceObservation { ProjectGitRepositoryId = historical.Id, GitSyncJobId = job2.Id, GitEvidenceVersionId = version2.Id, GitHubRepositoryId = repo2.Id, Category = "commits", ScopeKey = "", AuthorizationSource = "app", RetrievalCompleteness = "Incomplete", ObservedAt = cutoff.AddSeconds(-1) };
        var secondVersion = new GitEvidenceVersion { GitHubProviderObjectId = obj.Id, GitHubRepositoryId = repo.Id, EvidenceSchemaVersion = 1, CanonicalizationVersion = 1, ContentHash = new string('c', 64), FrozenFactsJson = "{}", EvidenceStatus = "Incomplete" };
        var nextJob = new GitSyncJob { ProjectGitRepositoryId = link.Id, GitHubRepositoryId = repo.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial, Status = GitSyncJobStatus.Completed, CompletedAt = cutoff };
        var again = Observation("again"); again.GitSyncJobId = nextJob.Id;
        var changed = Observation("changed"); changed.GitEvidenceVersionId = secondVersion.Id;
        var cycle = new ContributionCycle { ProjectId = project.Id, OpenedById = owner.Profile.Id, Status = ContributionCycleStatus.Finalized, FinalizedAt = cutoff };
        var run = new ContributionCalculationRun { CycleId = cycle.Id, RunNumber = 1, FormulaVersion = "1", InputSnapshot = "{\"legacy\":true}", InputHash = "legacy-hash", CalculatedById = owner.Profile.Id, Status = ContributionCalculationRunStatus.Confirmed };
        var participant = new ContributionParticipant { CycleId = cycle.Id, UserId = owner.Profile.Id, RoleAtSnapshot = "Leader", MembershipStatusAtSnapshot = ProjectMemberStatus.Active, JoinedAt = cutoff.AddDays(-1), IsLeader = true };
        var result = new ContributionResult { CycleId = cycle.Id, RunId = run.Id, UserId = owner.Profile.Id, TotalScore = 42 };
        verify.AddRange(repo2, grant2, historical, job2, object2, version2, historicalObs, secondVersion, nextJob, again, changed, cycle, participant, run, result);
        await verify.SaveChangesAsync();
        await using var legacyBeforeDb = new UniNetDbContext(options);
        var legacyBefore = await legacyBeforeDb.ContributionCalculationRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
        var duplicate = Observation("first");
        await using (var dupDb = new UniNetDbContext(options))
        {
            dupDb.Add(duplicate);
            var duplicateError = await Assert.ThrowsAsync<DbUpdateException>(() => dupDb.SaveChangesAsync());
            Assert.Equal("23505", Assert.IsType<PostgresException>(duplicateError.InnerException).SqlState);
        }
        await verify.GitHubProviderObjects.Where(x => x.Id == obj.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.PayloadJson, "{\"changed\":true}"));
        await using (var captureMulti = new UniNetDbContext(options))
        {
            var multiId = await new ProjectGitEvidenceSnapshotService(captureMulti).CaptureAsync(project.Id, owner.Id, cutoff, default);
            var actual = await verify.Set<ProjectGitEvidenceSnapshotEntry>().Where(x => x.SnapshotId == multiId).ToListAsync();
            Assert.Equal(new[] { first.Id, late.Id, historicalObs.Id, again.Id, changed.Id }.Order(), actual.Select(x => x.ProjectEvidenceObservationId).Order());
            Assert.Contains(actual, x => x.ProjectEvidenceObservationId == historicalObs.Id && x.ProjectGitRepositoryId == historical.Id && x.GitEvidenceVersionId == version2.Id);
            Assert.Contains(actual, x => x.ProjectEvidenceObservationId == changed.Id && x.GitEvidenceVersionId == secondVersion.Id);
        }
        await using (var checkLegacy = new UniNetDbContext(options))
        {
            var existing = await checkLegacy.ContributionCalculationRuns.SingleAsync(x => x.Id == run.Id);
            Assert.Equal(legacyBefore.InputHash, existing.InputHash); Assert.Equal(legacyBefore.InputSnapshot, existing.InputSnapshot);
            Assert.Equal(ContributionCalculationRunStatus.Confirmed, existing.Status);
            Assert.Equal(ContributionCycleStatus.Finalized, (await checkLegacy.ContributionCycles.SingleAsync(x => x.Id == cycle.Id)).Status);
            Assert.Equal(42, (await checkLegacy.ContributionResults.SingleAsync(x => x.Id == result.Id)).TotalScore);
            Assert.Equal(1, await checkLegacy.ContributionResults.CountAsync(x => x.CycleId == cycle.Id));
            Assert.Equal(1, await checkLegacy.ContributionCalculationRuns.CountAsync(x => x.CycleId == cycle.Id));
            Assert.Equal(1, await checkLegacy.ContributionCycles.CountAsync(x => x.ProjectId == project.Id));
        }
        var failure = new FailAfterEntries();
        var injectedOptions = new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(cs).AddInterceptors(failure).Options;
        await using (var failing = new UniNetDbContext(injectedOptions))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new ProjectGitEvidenceSnapshotService(failing).CaptureAsync(project.Id, owner.Id, cutoff, default));
        Assert.NotEqual(Guid.Empty, failure.SnapshotId);
        Assert.False(await verify.Set<ProjectGitEvidenceSnapshot>().AnyAsync(x => x.Id == failure.SnapshotId));
        Assert.False(await verify.Set<ProjectGitEvidenceSnapshotEntry>().AnyAsync(x => x.SnapshotId == failure.SnapshotId));
        Assert.True(await verify.GitEvidenceVersions.AnyAsync(x => x.Id == version.Id));
        Assert.True(await verify.ProjectEvidenceObservations.AnyAsync(x => x.Id == first.Id));
        await using (var retry = new UniNetDbContext(options))
            Assert.NotEqual(Guid.Empty, await new ProjectGitEvidenceSnapshotService(retry).CaptureAsync(project.Id, owner.Id, cutoff, default));
        await using (var crossUser = new UniNetDbContext(options))
            await Assert.ThrowsAsync<GitHubIntegrationException>(() => new ProjectGitEvidenceSnapshotService(crossUser).CaptureAsync(project.Id, otherOwner.Id, cutoff, default));
        var manager = new Account { Email = $"manager-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Manager", Nickname = "Manager" } };
        var member = new ProjectMember { Id = Guid.NewGuid(), ProjectId = project.Id, UserId = manager.Profile!.Id, Role = "Member", Status = ProjectMemberStatus.Active, JoinedAt = cutoff.AddDays(-1), CreatedAt = cutoff, UpdatedAt = cutoff };
        var managerGrant = new ProjectGitManagerGrant { ProjectId = project.Id, UserProfileId = manager.Profile.Id, GrantedByProfileId = owner.Profile.Id, IsActive = true };
        verify.AddRange(manager, member, managerGrant); await verify.SaveChangesAsync();
        await using (var managerDb = new UniNetDbContext(options))
            Assert.NotEqual(Guid.Empty, await new ProjectGitEvidenceSnapshotService(managerDb).CaptureAsync(project.Id, manager.Id, cutoff, default));

        await using var pendingWriter = new UniNetDbContext(options);
        await using var pendingTx = await pendingWriter.Database.BeginTransactionAsync();
        var pending = Observation("mvcc"); pendingWriter.Add(pending); await pendingWriter.SaveChangesAsync();
        var gate = new CaptureGate();
        var gateOptions = new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(cs).AddInterceptors(gate).Options;
        await using var captureA = new UniNetDbContext(gateOptions);
        var taskA = new ProjectGitEvidenceSnapshotService(captureA).CaptureAsync(project.Id, owner.Id, cutoff, default);
        try
        {
            await gate.Seen.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await pendingTx.CommitAsync();
            await using var captureB = new UniNetDbContext(options);
            var b = await new ProjectGitEvidenceSnapshotService(captureB).CaptureAsync(project.Id, owner.Id, cutoff, default);
            gate.Release.TrySetResult(); var a = await taskA;
            var aIds = await verify.Set<ProjectGitEvidenceSnapshotEntry>().Where(x => x.SnapshotId == a).Select(x => x.ProjectEvidenceObservationId).ToListAsync();
            var bIds = await verify.Set<ProjectGitEvidenceSnapshotEntry>().Where(x => x.SnapshotId == b).Select(x => x.ProjectEvidenceObservationId).ToListAsync();
            Assert.DoesNotContain(pending.Id, aIds); Assert.Contains(pending.Id, bIds);
            Assert.Equal(aIds.Order(), bIds.Where(x => x != pending.Id).Order());
        }
        finally { gate.Release.TrySetResult(); }

        // Reader validation uses stored membership and never returns facts for unauthorized associations.
        var validFacts = new GitFrozenFacts(1, "commits", "sha", null, null, null, [], "Complete");
        var incompleteFacts = validFacts with { Completeness = "Incomplete" };
        GitEvidenceVersion FactVersion(GitFrozenFacts facts, string status)
        {
            var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
            return new GitEvidenceVersion { GitHubProviderObjectId = obj.Id, GitHubRepositoryId = repo.Id,
                EvidenceSchemaVersion = 1, CanonicalizationVersion = 1, HashAlgorithm = "SHA-256", ContentHash = canonical.Sha256,
                FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes), EvidenceStatus = status };
        }
        var validVersion = FactVersion(validFacts, "Complete");
        var incompleteVersion = FactVersion(incompleteFacts, "Incomplete");
        var unsupportedVersion = new GitEvidenceVersion { GitHubProviderObjectId = obj.Id, GitHubRepositoryId = repo.Id,
            EvidenceSchemaVersion = 99, CanonicalizationVersion = 1, ContentHash = new string('f', 64), FrozenFactsJson = "{}", EvidenceStatus = "Incomplete" };
        var validObservation = Observation("reader-valid"); validObservation.GitEvidenceVersionId = validVersion.Id; validObservation.RetrievalCompleteness = "Complete";
        var incompleteObservation = Observation("reader-incomplete"); incompleteObservation.GitEvidenceVersionId = incompleteVersion.Id;
        var unsupportedObservation = Observation("reader-unsupported"); unsupportedObservation.GitEvidenceVersionId = unsupportedVersion.Id;
        verify.AddRange(validVersion, incompleteVersion, unsupportedVersion, validObservation, incompleteObservation, unsupportedObservation);
        await verify.SaveChangesAsync();
        await verify.GitHubRepositories.Where(x => x.Id == repo.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPrivate, true));
        await verify.ProjectGitRepositories.Where(x => x.Id == link.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AccessStatus, ProjectGitRepositoryAccessStatus.Active));
        var readerCutoff = DateTimeOffset.UtcNow;
        await using var captureRead = new UniNetDbContext(options);
        var readerSnapshot = await new ProjectGitEvidenceSnapshotService(captureRead).CaptureAsync(project.Id, owner.Id, readerCutoff, default);
        await using var readDb = new UniNetDbContext(options);
        var reader = new ProjectGitEvidenceSnapshotReader(readDb);
        var read = await reader.ReadAsync(project.Id, readerSnapshot, owner.Id, default);
        var readiness = await new GitContributionReadinessService(reader).AssessAsync(project.Id, readerSnapshot, owner.Id, default);
        Assert.Equal(4, readiness.Categories.Count);
        Assert.All(readiness.Categories, x => Assert.Equal("NotReady", x.Status));
        Assert.Equal(SnapshotEvidenceStatus.Valid, Assert.Single(read.Entries, x => x.ObservationId == validObservation.Id).Status);
        Assert.Equal(SnapshotEvidenceStatus.Incomplete, Assert.Single(read.Entries, x => x.ObservationId == incompleteObservation.Id).Status);
        Assert.Equal(SnapshotEvidenceStatus.Unsupported, Assert.Single(read.Entries, x => x.ObservationId == unsupportedObservation.Id).Status);
        Assert.Equal(SnapshotEvidenceStatus.Corrupt, Assert.Single(read.Entries, x => x.ObservationId == first.Id).Status);
        Assert.Null(Assert.Single(read.Entries, x => x.ObservationId == first.Id).Facts);
        Assert.True(read.RestrictedCount > 0); // The historical, unlinked association is not disclosed.
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => reader.ReadAsync(project.Id, readerSnapshot, otherOwner.Id, default));
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => reader.ReadAsync(otherProject.Id, readerSnapshot, otherOwner.Id, default));
        var afterSnapshot = Observation("reader-later"); afterSnapshot.GitEvidenceVersionId = incompleteVersion.Id;
        verify.Add(afterSnapshot); await verify.SaveChangesAsync();
        var unchanged = await reader.ReadAsync(project.Id, readerSnapshot, owner.Id, default);
        Assert.Equal(read.Entries.Select(x => x.ObservationId), unchanged.Entries.Select(x => x.ObservationId));
        await verify.ProjectGitRepositories.Where(x => x.Id == link.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.AccessStatus, ProjectGitRepositoryAccessStatus.AccessLost));
        var restricted = await reader.ReadAsync(project.Id, readerSnapshot, owner.Id, default);
        Assert.Empty(restricted.Entries); Assert.True(restricted.RestrictedCount >= read.Entries.Count);
    }
}
