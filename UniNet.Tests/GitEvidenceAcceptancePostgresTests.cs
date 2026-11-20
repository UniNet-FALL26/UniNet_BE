using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class EvidenceAcceptanceFactAttribute : FactAttribute
{
    public EvidenceAcceptanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UNINET_EVIDENCE_ACCEPTANCE_PORT") is null)
            Skip = "Requires a verified disposable PostgreSQL acceptance target.";
    }
}

public sealed class GitEvidenceAcceptancePostgresTests
{
    [EvidenceAcceptanceFact]
    public async Task CompositeKeysAndReplayAreEnforced()
    {
        var portText = Environment.GetEnvironmentVariable("UNINET_EVIDENCE_ACCEPTANCE_PORT");
        if (!int.TryParse(portText, out var port) || port is <= 1024 or 55433 or > 65535)
            throw new InvalidOperationException("An explicitly verified disposable PostgreSQL port is required.");
        var cs = $"Host=127.0.0.1;Port={port};Database=uninet_acceptance_disposable;Username=postgres;SSL Mode=Disable";
        var options = new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(cs).Options;
        await using var db = new UniNetDbContext(options);
        await db.Database.OpenConnectionAsync();
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        Assert.Equal("127.0.0.1", conn.Host); Assert.Equal(port, conn.Port); Assert.Equal("uninet_acceptance_disposable", conn.Database);
        var owner = new Account { Email = $"accept-{Guid.NewGuid():N}@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Acceptance", ProjectField = "IT", Description = "fixture", MemberTarget = 2, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Status = ProjectStatus.Active };
        var seed = Random.Shared.NextInt64(1000000, long.MaxValue / 4);
        var install = new GitHubAppInstallation { ProviderInstallationId = seed, OwnerAccountId = 9, OwnerLogin = "fixture", OwnerType = "User" };
        GitHubRepository Repo(long id) => new() { ProviderRepositoryId = id, ProviderOwnerId = 9, OwnerLogin = "fixture", Name = "repo" + id, FullName = "fixture/repo" + id, HtmlUrl = "https://example.test/repo" + id };
        var r1 = Repo(seed + 1); var r2 = Repo(seed + 2);
        var g1 = new GitHubAppRepositoryGrant { InstallationId = install.Id, ProviderRepositoryId = r1.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var g2 = new GitHubAppRepositoryGrant { InstallationId = install.Id, ProviderRepositoryId = r2.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        ProjectGitRepository Link(GitHubRepository r, GitHubAppRepositoryGrant g) => new() { ProjectId = project.Id, GitHubRepositoryId = r.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = g.Id, SyncPaused = true, AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization };
        var a1 = Link(r1,g1); var a2 = Link(r2,g2);
        GitSyncJob Job(ProjectGitRepository a, GitHubAppRepositoryGrant g) => new() { ProjectGitRepositoryId = a.Id, GitHubRepositoryId = a.GitHubRepositoryId, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = g.Id, SyncType = GitSyncType.Initial };
        var j1 = Job(a1,g1); var j2 = Job(a2,g2);
        GitHubProviderObject Obj(GitHubRepository r) => new() { GitHubRepositoryId = r.Id, Category = "commits", ExternalObjectId = "sha", PayloadJson = "{}", RetrievalMetadataJson = "{}", FirstObservedAt = DateTimeOffset.UtcNow, LastObservedAt = DateTimeOffset.UtcNow };
        var p1 = Obj(r1); var p2 = Obj(r2);
        GitEvidenceVersion Ver(GitHubProviderObject p) => new() { GitHubProviderObjectId = p.Id, GitHubRepositoryId = p.GitHubRepositoryId, EvidenceSchemaVersion = 1, CanonicalizationVersion = 1, ContentHash = new string('a',64), FrozenFactsJson = "{}", EvidenceStatus = "partial" };
        var v1 = Ver(p1); var v2 = Ver(p2);
        db.AddRange(owner, project, install, r1, r2, g1, g2, a1, a2, j1, j2, p1, p2, v1, v2); await db.SaveChangesAsync();
        await using (var invalidHash = new UniNetDbContext(options))
        {
            var invalid = Ver(p1); invalid.HashAlgorithm = "unsupported"; invalid.ContentHash = new string('c', 64);
            invalidHash.Add(invalid);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => invalidHash.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(failure.InnerException);
            Assert.Equal("23514", postgres.SqlState); Assert.Equal("CK_GitEvidenceVersions_Hash", postgres.ConstraintName);
        }
        async Task Insert(Guid association, Guid job, Guid version, Guid repo, string scope)
        {
            await using var c = new UniNetDbContext(options);
            c.Add(new ProjectEvidenceObservation { ProjectGitRepositoryId = association, GitSyncJobId = job, GitEvidenceVersionId = version, GitHubRepositoryId = repo, Category = "commits", ScopeKey = scope, AuthorizationSource = "app", RetrievalCompleteness = "partial" });
            await c.SaveChangesAsync();
        }
        await Insert(a1.Id,j1.Id,v1.Id,r1.Id,"");
        async Task Rejected(Func<Task> action, string constraintPrefix)
        {
            var ex = await Assert.ThrowsAsync<DbUpdateException>(action);
            var pg = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.StartsWith(constraintPrefix, pg.ConstraintName);
        }
        await Rejected(() => Insert(a1.Id,j2.Id,v1.Id,r1.Id,""), "FK_ProjectEvidenceObservations_GitSyncJobs");
        await Rejected(() => Insert(a1.Id,j1.Id,v2.Id,r1.Id,""), "FK_ProjectEvidenceObservations_GitEvidenceVersions");
        await Rejected(() => Insert(a1.Id,j1.Id,v1.Id,r1.Id,""), "IX_ProjectEvidenceObservations_");
        await Insert(a1.Id,j1.Id,v1.Id,r1.Id,"branch");
        await Insert(a2.Id,j2.Id,v2.Id,r2.Id,"");
        var j3 = Job(a1,g1); j3.Status = GitSyncJobStatus.Completed; j3.CompletedAt = DateTimeOffset.UtcNow;
        var v3 = Ver(p1); v3.ContentHash = new string('b', 64);
        db.AddRange(j3, v3); await db.SaveChangesAsync();
        await Insert(a1.Id,j3.Id,v1.Id,r1.Id,"");
        await Insert(a1.Id,j1.Id,v3.Id,r1.Id,"");
        await Insert(a1.Id,j1.Id,v1.Id,r1.Id," "); // Database treats whitespace as a distinct scope; writer must normalize.
        await Rejected(() => Insert(a1.Id,j1.Id,v1.Id,r1.Id,""), "IX_ProjectEvidenceObservations_");
        await using (var txDb = new UniNetDbContext(options))
        {
            await using var tx = await txDb.Database.BeginTransactionAsync();
            txDb.Add(new ProjectEvidenceObservation { ProjectGitRepositoryId = a1.Id, GitSyncJobId = j1.Id, GitEvidenceVersionId = v1.Id, GitHubRepositoryId = r1.Id, Category = "commits", ScopeKey = "rolled-back", AuthorizationSource = "app", RetrievalCompleteness = "partial" });
            await txDb.SaveChangesAsync(); await tx.RollbackAsync();
        }
        Assert.False(await db.Set<ProjectEvidenceObservation>().AnyAsync(x => x.GitSyncJobId == j1.Id && x.ScopeKey == "rolled-back"));
        await Insert(a1.Id,j1.Id,v1.Id,r1.Id,"rolled-back");
        async Task<bool> ConcurrentInsert()
        {
            try { await Insert(a1.Id,j1.Id,v1.Id,r1.Id,"concurrent"); return true; }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { return false; }
        }
        var outcomes = await Task.WhenAll(ConcurrentInsert(), ConcurrentInsert());
        Assert.Single(outcomes.Where(x => x));
        Assert.Equal(8, await db.Set<ProjectEvidenceObservation>().CountAsync(x => x.GitSyncJobId == j1.Id || x.GitSyncJobId == j2.Id || x.GitSyncJobId == j3.Id));
    }
}
