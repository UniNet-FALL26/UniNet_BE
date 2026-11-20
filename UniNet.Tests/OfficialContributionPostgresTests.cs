using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UniNet.Application;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class OfficialPostgresFactAttribute : FactAttribute
{
    public OfficialPostgresFactAttribute() { if (Environment.GetEnvironmentVariable("UNINET_OFFICIAL_TEST_PORT") is null) Skip = "Requires verified Official V2 disposable PostgreSQL."; }
}

public sealed partial class GitExperimentalContributionPostgresHttpTests
{
    private static UniNetDbContext OfficialDb(string suffix = "")
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("UNINET_OFFICIAL_TEST_PORT"), out var port) || port is <= 1024 or 55433 or > 65535)
            throw new Exception("Unsafe official PostgreSQL fixture target");
        return new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql($"Host=127.0.0.1;Port={port};Database=uninet_official_v2{suffix}_disposable;Username=postgres;SSL Mode=Disable").Options);
    }
    private static OfficialContributionService OfficialService(UniNetDbContext db)
    {
        var capture = new ProjectGitEvidenceSnapshotService(db); var reader = new ProjectGitEvidenceSnapshotReader(db);
        var engine = new GitExperimentalContributionService(db, new ProjectGitContributionSnapshotAccess(capture, reader), TimeProvider.System);
        return new(db, engine, capture, reader, new(db, TimeProvider.System));
    }
    private static async Task<T> OfficialCall<T>(Func<OfficialContributionService, Task<T>> action)
    { await using var db = OfficialDb(); return await action(OfficialService(db)); }

    private static async Task<(Guid Project, Guid[] Accounts, Guid[] Profiles, Guid Snapshot, Guid Peer)> OfficialFixture()
    {
        await using var db = OfficialDb(); await db.Database.MigrateAsync();
        var seed = await SeedProjectAsync(db);
        var original = await db.Projects.SingleAsync(x => x.Id == seed.ProjectId);
        // Different project, same repository and verified accounts; no legacy cycle.
        var project = new Project { CreatorId = seed.ProfileIds[0], Title = "Official no legacy cycle", ProjectField = "Software",
            Description = "Disposable official fixture", MemberTarget = 3, MinimumQuantity = 1,
            RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(-2), CreatedAt = original.CreatedAt, Status = ProjectStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        db.Add(project); db.AddRange(seed.ProfileIds.Take(3).Select(x => new ProjectMember { ProjectId = project.Id, UserId = x,
            Role = x == seed.ProfileIds[0] ? "Creator" : "Member", Status = ProjectMemberStatus.Active, JoinedAt = original.CreatedAt }));
        var identity = await db.ProjectGitMemberIdentities.AsNoTracking().Where(x => x.ProjectId == seed.ProjectId).ToArrayAsync();
        foreach (var old in identity)
        {
            var clone = new ProjectGitMemberIdentity { ProjectId = project.Id, UserProfileId = old.UserProfileId, ExternalAccountId = old.ExternalAccountId,
                ExternalUsername = old.ExternalUsername, VerifiedByConnectionId = old.VerifiedByConnectionId, LinkedAt = old.LinkedAt, LastActivatedAt = old.LastActivatedAt };
            db.Add(clone); db.Add(new ProjectGitMemberIdentityHistory { IdentityId = clone.Id, ProjectId = project.Id, UserProfileId = clone.UserProfileId,
                ExternalAccountId = clone.ExternalAccountId, ExternalUsername = clone.ExternalUsername, NewStatus = ProjectGitMemberIdentityStatus.Active,
                VerifiedByConnectionId = clone.VerifiedByConnectionId, PerformedByProfileId = seed.ProfileIds[0], OccurredAt = old.LinkedAt });
        }
        var link = await db.ProjectGitRepositories.AsNoTracking().SingleAsync(x => x.ProjectId == seed.ProjectId);
        var association = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = link.GitHubRepositoryId, LinkedByProfileId = seed.ProfileIds[0],
            CredentialSource = link.CredentialSource, GitHubAppRepositoryGrantId = link.GitHubAppRepositoryGrantId, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var job = new GitSyncJob { ProjectGitRepositoryId = association.Id, GitHubRepositoryId = association.GitHubRepositoryId,
            CredentialSource = association.CredentialSource, GitHubAppRepositoryGrantId = association.GitHubAppRepositoryGrantId,
            Status = GitSyncJobStatus.Completed, CompletedAt = DateTimeOffset.UtcNow, SyncType = GitSyncType.Manual };
        db.AddRange(association, job); await db.SaveChangesAsync();
        var prior = await db.ProjectEvidenceObservations.AsNoTracking().Where(x => x.ProjectGitRepositoryId == link.Id).ToArrayAsync();
        foreach (var o in prior)
        {
            var v = await db.GitEvidenceVersions.AsNoTracking().SingleAsync(x => x.Id == o.GitEvidenceVersionId);
            var f = JsonSerializer.Deserialize<GitFrozenFacts>(v.FrozenFactsJson)!;
            var facts = f with { SchemaVersion = 4, Completeness = "Complete", Details = f.Details! with { MissingFacts = [], ParentShas = o.Category == "commits" ? [new string('d', 40)] : null } };
            var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
            var version = new GitEvidenceVersion { GitHubProviderObjectId = v.GitHubProviderObjectId, GitHubRepositoryId = v.GitHubRepositoryId,
                EvidenceSchemaVersion = 4, CanonicalizationVersion = 1, ContentHash = canonical.Sha256, FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes), EvidenceStatus = "Complete" };
            db.Add(version); db.Add(new ProjectEvidenceObservation { ProjectGitRepositoryId = association.Id, GitSyncJobId = job.Id,
                GitHubRepositoryId = association.GitHubRepositoryId, GitEvidenceVersionId = version.Id, Category = o.Category, ScopeKey = o.ScopeKey,
                ObservedAt = DateTimeOffset.UtcNow, AuthorizationSource = "app", RetrievalCompleteness = "Complete" });
        }
        await db.SaveChangesAsync();
        var grant = await db.Set<GitHubAppRepositoryGrant>().AsNoTracking().SingleAsync(x => x.Id == association.GitHubAppRepositoryGrantId);
        var secondRepo = new GitHubRepository { ProviderRepositoryId = Random.Shared.NextInt64(1000000, long.MaxValue / 4),
            ProviderOwnerId = 9001, OwnerLogin = "disposable-owner", Name = "second", FullName = "disposable-owner/" + Guid.NewGuid().ToString("N"), HtmlUrl = "https://example.test/second" };
        var secondGrant = new GitHubAppRepositoryGrant { InstallationId = grant.InstallationId, ProviderRepositoryId = secondRepo.ProviderRepositoryId, VerifiedAt = DateTimeOffset.UtcNow };
        var secondLink = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = secondRepo.Id, LinkedByProfileId = seed.ProfileIds[0],
            CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = secondGrant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var completed = DateTimeOffset.UtcNow;
        var secondJob = new GitSyncJob { ProjectGitRepositoryId = secondLink.Id, GitHubRepositoryId = secondRepo.Id, CredentialSource = secondLink.CredentialSource,
            GitHubAppRepositoryGrantId = secondGrant.Id, Status = GitSyncJobStatus.Completed, CompletedAt = completed, SyncType = GitSyncType.Manual };
        var firstCommitVersionId = prior.First(o => o.Category == "commits").GitEvidenceVersionId;
        var firstFacts = JsonSerializer.Deserialize<GitFrozenFacts>((await db.GitEvidenceVersions.SingleAsync(x => x.Id == firstCommitVersionId)).FrozenFactsJson)!;
        var facts2 = firstFacts with { SchemaVersion = 4, Category = "commits", ExternalObjectId = new string('c', 40), Completeness = "Complete",
            Details = firstFacts.Details! with { MissingFacts = [], ParentShas = [new string('d', 40)] } };
        var extra = Evidence(secondRepo.Id, secondLink.Id, secondJob.Id, "commits", "", facts2.ExternalObjectId, facts2, completed);
        extra.Version.EvidenceStatus = "Complete"; extra.Observation.RetrievalCompleteness = "Complete";
        db.AddRange(secondRepo, secondGrant, secondLink, secondJob, extra.ProviderObject, extra.Version, extra.Observation,
            new GitSyncCheckpoint { SyncJobId = secondJob.Id, Category = "commits", ScopeKey = "", Status = GitSyncCheckpointStatus.Completed,
                ItemsProcessed = 1, LastProcessedAt = completed });
        await db.SaveChangesAsync();
        var service = OfficialService(db);
        var peer = await service.OpenPeerAsync(project.Id, seed.AccountIds[0], new(seed.ProfileIds.Take(3).ToArray()), default);
        for (var from = 0; from < 3; from++) for (var to = 0; to < 3; to++) if (from != to)
            await service.SubmitPeerAsync(project.Id, peer.Id, seed.ProfileIds[to], seed.AccountIds[from], new(4, null, null), default);
        var peerId = await service.ClosePeerAsync(project.Id, peer.Id, seed.AccountIds[0], default);
        var snapshot = await service.CaptureAsync(project.Id, seed.AccountIds[0], new(DateTimeOffset.UtcNow), default);
        Assert.Equal(2, await db.Set<ProjectGitEvidenceSnapshotEntry>().Where(x => x.SnapshotId == snapshot).Select(x => x.ProjectGitRepositoryId).Distinct().CountAsync());
        Assert.False(await db.ContributionCycles.AnyAsync(x => x.ProjectId == project.Id));
        return (project.Id, seed.AccountIds, seed.ProfileIds, snapshot, peerId);
    }

    [OfficialPostgresFact]
    public async Task OfficialV2UsesFrozenRosterWithoutLegacyCycleAndPublishesOnlyAfterApproval()
    {
        var f = await OfficialFixture();
        await using (var schema = OfficialDb())
        {
            Assert.Equal(0, await schema.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM information_schema.tables
                WHERE table_schema='public' AND table_name IN
                  ('ProjectTasks','ProjectTaskAssignments','TaskEvidence','TaskReports','TaskReportReviews')
                """).SingleAsync());
        }
        Assert.Equal("OfficialV2", (await OfficialCall(s => s.StatusAsync(f.Project, f.Accounts[0], default))).Mode);
        var request = new OfficialRunRequest(Guid.NewGuid(), f.Snapshot, f.Peer);
        var run = await OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], request, default));
        var workflow = await OfficialCall(s => s.StatusAsync(f.Project, f.Accounts[0], default));
        Assert.Equal(f.Peer, workflow.LatestPeerSnapshotId); Assert.Equal(run.Id, workflow.LatestRunId); Assert.Equal(f.Snapshot, workflow.LatestGitSnapshotId);
        Assert.Equal("Closed", workflow.PeerState); Assert.Equal(6, workflow.PeerSubmittedCount);
        Assert.Equal("Ready", run.Calculation.CalculationStatus); Assert.Equal(100m, run.Calculation.Results.Sum(x => x.ContributionPercent));
        await using (var direct = OfficialDb())
        {
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => direct.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"OfficialContributionRuns\" SET \"State\"='Approved', \"Revision\"=1 WHERE \"Id\"={run.Id}"))).SqlState);
            Assert.Equal("Draft", (await direct.Set<OfficialContributionRun>().AsNoTracking().SingleAsync(x => x.Id == run.Id)).State);
        }
        Assert.Equal(run.Id, (await OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], request, default))).Id);
        await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.GetAsync(f.Project, run.Id, f.Accounts[1], default)));
        Assert.Equal("OFFICIAL_TRANSITION_INVALID", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "finalize", new(0), default)))).Code);
        Assert.Equal("OFFICIAL_NOT_FINALIZED", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "publish", new(0), default)))).Code);
        await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.MyResultAsync(f.Project, f.Accounts[1], default)));
        run = await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "submit", new(0), default));
        Assert.Equal("Review", run.State);
        Assert.Equal("OFFICIAL_FEEDBACK_REQUIRED", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "reject", new(1), default)))).Code);
        var approve = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => {
            try { return (await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "approve", new(1), default))).State; }
            catch (ProjectException ex) { return ex.Code; }
        }));
        Assert.Contains("Approved", approve); Assert.All(approve, x => Assert.Contains(x, new[] { "Approved", "OFFICIAL_CONCURRENCY_CONFLICT", "OFFICIAL_REVISION_CONFLICT" }));
        run = await OfficialCall(s => s.GetAsync(f.Project, run.Id, f.Accounts[0], default));
        var finalizations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => {
            try { return (await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "finalize", new(run.Revision), default))).State; }
            catch (ProjectException ex) { return ex.Code; }
        }));
        Assert.Contains("Finalized", finalizations);
        Assert.All(finalizations, x => Assert.Contains(x, new[] { "Finalized", "OFFICIAL_CONCURRENCY_CONFLICT" }));
        run = await OfficialCall(s => s.GetAsync(f.Project, run.Id, f.Accounts[0], default));
        Assert.Equal("Finalized", run.State);
        Assert.Equal(run.Id, (await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "finalize", new(0), default))).Id);
        var published = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => {
            try { return (await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "publish", new(run.Revision), default))).State; }
            catch (ProjectException ex) { return ex.Code; }
        }));
        Assert.Contains("Published", published); Assert.All(published, x => Assert.Contains(x, new[] { "Published", "OFFICIAL_CONCURRENCY_CONFLICT" }));
        Assert.Equal("Published", (await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "publish", new(0), default))).State);
        var own = await OfficialCall(s => s.MyResultAsync(f.Project, f.Accounts[1], default));
        Assert.Equal(f.Profiles[1], own.UserId); Assert.Equal(run.Id, own.RunId);
        await using var verify = OfficialDb();
        Assert.Equal(5, await verify.Set<OfficialContributionAudit>().CountAsync(x => x.RunId == run.Id));
        Assert.Single(await verify.Set<OfficialContributionPublication>().Where(x => x.ProjectId == f.Project).ToArrayAsync());
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitContributionRuns\" SET \"ResultsJson\"='[]'::jsonb WHERE \"Id\"={run.Id}"))).SqlState);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"OfficialContributionRuns\" WHERE \"Id\"={run.Id}"))).SqlState);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"OfficialContributionPublications\" SET \"PublishedAt\"=now() WHERE \"RunId\"={run.Id}"))).SqlState);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"OfficialContributionAudits\" WHERE \"RunId\"={run.Id}"))).SqlState);
        var legacyEngine = new GitExperimentalContributionService(verify, new ProjectGitContributionSnapshotAccess(new(verify), new(verify)), TimeProvider.System);
        Assert.Equal("OFFICIAL_LIFECYCLE_REQUIRED", (await Assert.ThrowsAsync<ProjectException>(() => legacyEngine.FinalizeRunAsync(f.Project, run.Id, f.Accounts[0], default))).Code);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => verify.GetService<IMigrator>().MigrateAsync("20261119164008_AddProjectGitSnapshotCoverageReceipts"))).SqlState);
        Assert.Contains("20261119190820_AddOfficialContributionV2Workflow", await verify.Database.GetAppliedMigrationsAsync());
        Assert.False(verify.Database.HasPendingModelChanges());
    }

    [OfficialPostgresFact]
    public async Task OfficialRejectRevokedAccessAndIndependentProjectIsolation()
    {
        var f = await OfficialFixture(); var request = new OfficialRunRequest(Guid.NewGuid(), f.Snapshot, f.Peer);
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[3], request, default)));
        var run = await OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], request, default));
        run = await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "submit", new(0), default));
        run = await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "reject", new(1, "missing verification"), default));
        Assert.Equal("Rejected", run.State);
        await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "finalize", new(2), default)));
        await using var db = OfficialDb(); var link = await db.ProjectGitRepositories.AsNoTracking().OrderBy(x => x.Id).FirstAsync(x => x.ProjectId == f.Project);
        await db.ProjectGitRepositories.Where(x => x.Id == link.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.AccessStatus, ProjectGitRepositoryAccessStatus.AccessLost));
        // A new context cannot reuse revoked private/project evidence as a Ready official run.
        await Assert.ThrowsAnyAsync<Exception>(() => OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], request with { RequestId = Guid.NewGuid() }, default)));
        Assert.Equal(1, await db.Set<OfficialContributionRun>().CountAsync(x => x.ProjectId == f.Project));
        Assert.Equal(1, await db.GitContributionRuns.CountAsync(x => x.ProjectId == f.Project));
        Assert.Equal(0, await db.Set<OfficialContributionPublication>().CountAsync(x => x.ProjectId == f.Project));
    }

    [OfficialPostgresFact]
    public async Task OfficialHttpRoutesEnforceCreatorReviewAndMemberPublicationPrivacy()
    {
        var f = await OfficialFixture();
        await using var source = OfficialDb();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<UniNetDbContext>(o => o.UseNpgsql(source.Database.GetConnectionString()));
        builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddGitExperimentalContributionServices();
        builder.Services.AddControllers().AddApplicationPart(typeof(OfficialContributionController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o => {
            o.MapInboundClaims = false; o.TokenValidationParameters = new() { ValidateIssuer = true, ValidIssuer = Issuer,
                ValidateAudience = true, ValidAudience = Issuer, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), RoleClaimType = ClaimTypes.Role };
        }); builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseExceptionHandler(e => e.Run(async context => {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            context.Response.StatusCode = error switch { ProjectException p => p.Status, GitHubIntegrationException g => g.Status, _ => 500 };
            await context.Response.WriteAsJsonAsync(new { code = error switch { ProjectException p => p.Code, GitHubIntegrationException g => g.Code, _ => "INTERNAL" } });
        })); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        try
        {
            using var leader = Client(app, f.Accounts[0]); using var member = Client(app, f.Accounts[1]);
            var path = $"/api/projects/{f.Project}/contribution/v2";
            Assert.False((await member.GetFromJsonAsync<OfficialWorkflowResponse>($"{path}/workflow"))!.CanManage);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"{path}/snapshots/{f.Snapshot}")).StatusCode);
            var metadata = await leader.GetStringAsync($"{path}/snapshots/{f.Snapshot}"); Assert.DoesNotContain("frozenFacts", metadata, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Ready", (await leader.GetFromJsonAsync<OfficialReadinessResponse>($"{path}/snapshots/{f.Snapshot}/readiness"))!.GitState);
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"{path}/results/me")).StatusCode);
            var request = new OfficialRunRequest(Guid.NewGuid(), f.Snapshot, f.Peer);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"{path}/runs", request)).StatusCode);
            var response = await leader.PostAsJsonAsync($"{path}/runs", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var run = (await response.Content.ReadFromJsonAsync<OfficialRunResponse>())!;
            Assert.Equal(run.Id, (await (await leader.PostAsJsonAsync($"{path}/runs", request)).Content.ReadFromJsonAsync<OfficialRunResponse>())!.Id);
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync($"{path}/runs/{run.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"{path}/runs/{run.Id}/finalize", new OfficialTransitionRequest(0))).StatusCode);
            foreach (var action in new[] { "submit", "approve", "finalize", "publish" })
            {
                var next = await leader.PostAsJsonAsync($"{path}/runs/{run.Id}/{action}", new OfficialTransitionRequest(run.Revision));
                Assert.True(next.IsSuccessStatusCode, await next.Content.ReadAsStringAsync());
                run = (await next.Content.ReadFromJsonAsync<OfficialRunResponse>())!;
            }
            Assert.Equal("Published", run.State);
            var own = await member.GetStringAsync($"{path}/results/me");
            Assert.DoesNotContain("ratings", own, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("comment", own, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("frozenFacts", own, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(f.Profiles[1], JsonSerializer.Deserialize<OfficialMemberResultResponse>(own, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.UserId);
            Assert.Single((await leader.GetFromJsonAsync<OfficialPage<OfficialRunListItem>>($"{path}/runs?state=Published"))!.Items);
            Assert.Equal(5, (await leader.GetFromJsonAsync<OfficialPage<OfficialAuditResponse>>($"{path}/runs/{run.Id}/audit"))!.Total);
            var activities = await leader.GetAsync($"{path}/runs/{run.Id}/activities?pageSize=2");
            Assert.True(activities.IsSuccessStatusCode, await activities.Content.ReadAsStringAsync());
            var scoredPage = (await activities.Content.ReadFromJsonAsync<OfficialPage<OfficialScoredActivity>>())!;
            Assert.Equal(run.Calculation.Categories.Sum(x => x.EligibleActivityCount), scoredPage.Total);
            Assert.Equal(2, scoredPage.Items.Count);
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync($"{path}/runs?pageSize=1000")).StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [OfficialPostgresFact]
    public async Task OfficialRejectsHashCorruptionMissingInputsAndCrossProjectPeer()
    {
        var f = await OfficialFixture();
        await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], new(Guid.NewGuid(), f.Snapshot, Guid.NewGuid()), default)));
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], new(Guid.NewGuid(), Guid.NewGuid(), f.Peer), default)));
        var request = new OfficialRunRequest(Guid.NewGuid(), f.Snapshot, f.Peer);
        var run = await OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], request, default));
        Assert.Equal("OFFICIAL_IDEMPOTENCY_CONFLICT", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], request with { PeerSnapshotId = Guid.NewGuid() }, default)))).Code);
        await using var db = OfficialDb();
        var engine = new GitExperimentalContributionService(db, new ProjectGitContributionSnapshotAccess(new(db), new(db)), TimeProvider.System);
        var calc = await db.GitContributionRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id);
        calc.InputHash = new string('0', 64);
        Assert.Equal("OFFICIAL_INPUT_INTEGRITY", (await Assert.ThrowsAsync<ProjectException>(() => engine.ValidateOfficialAsync(calc, f.Accounts[0], default))).Code);
        calc = await db.GitContributionRuns.AsNoTracking().SingleAsync(x => x.Id == run.Id); calc.ResultsJson = "[]";
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            Assert.Equal("OFFICIAL_RESULT_INTEGRITY", (await Assert.ThrowsAsync<ProjectException>(() => engine.ValidateOfficialAsync(calc, f.Accounts[0], default))).Code);
            await tx.RollbackAsync();
        }
        Assert.Equal(run.Calculation.InputHash, (await OfficialCall(s => s.GetAsync(f.Project, run.Id, f.Accounts[0], default))).Calculation.InputHash);
        var p = new Project { CreatorId = f.Profiles[0], Title = "Other official project", Description = "fixture", ProjectField = "IT",
            Status = ProjectStatus.Completed, MemberTarget = 1, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(-1) };
        db.Add(p); await db.SaveChangesAsync();
        Assert.Equal("PEER_SNAPSHOT_NOT_FOUND", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.CreateAsync(p.Id, f.Accounts[0], request with { RequestId = Guid.NewGuid() }, default)))).Code);
        Assert.Equal("OFFICIAL_ROSTER_MISMATCH", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.OpenPeerAsync(f.Project, f.Accounts[0], new(f.Profiles.Take(2).ToArray()), default)))).Code);
    }

    [OfficialPostgresFact]
    public async Task NewOfficialProjectCompletionDoesNotCreateLegacyCycle()
    {
        await using var db = OfficialDb(); await db.Database.MigrateAsync();
        var owner = MakeAccount("official-completion");
        var project = new Project { CreatorId = owner.Profile!.Id, Title = "Official completion", Description = "fixture", ProjectField = "IT",
            Status = ProjectStatus.Active, MemberTarget = 1, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(-1) };
        db.AddRange(owner, project); await db.SaveChangesAsync();
        Assert.Equal("OfficialV2", (await OfficialService(db).StatusAsync(project.Id, owner.Id, default)).Mode);
        Assert.Contains("AuthorizedRepositoryRequired", (await OfficialService(db).StatusAsync(project.Id, owner.Id, default)).Blockers);
        Assert.Contains("VerifiedGitIdentityRequired", (await OfficialService(db).StatusAsync(project.Id, owner.Id, default)).Blockers);
        var service = new ProjectService(db, null!, new ConfigurationBuilder().Build());
        await service.UpdateStatusAsync(owner.Id, new(project.Id, ProjectStatus.Completed), default);
        Assert.False(await db.ContributionCycles.AnyAsync(x => x.ProjectId == project.Id));
        Assert.Equal("CONTRIBUTION_USE_OFFICIAL_V2", (await Assert.ThrowsAsync<ProjectException>(() => new ContributionService(db).GetCycleAsync(project.Id, owner.Id, default))).Code);
        var peer = await OfficialService(db).OpenPeerAsync(project.Id, owner.Id, new([owner.Profile.Id]), default);
        Assert.Equal(ExperimentalPeerCohortStatus.ReadyToClose, peer.Status);
        var snapshot = await OfficialService(db).CaptureAsync(project.Id, owner.Id, new(DateTimeOffset.UtcNow), default);
        Assert.Equal("Incomplete", (await OfficialService(db).ReadinessAsync(project.Id, snapshot, owner.Id, default)).GitState);
        var peerSnapshot = await OfficialService(db).ClosePeerAsync(project.Id, peer.Id, owner.Id, default);
        await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.CreateAsync(project.Id, owner.Id,
            new(Guid.NewGuid(), snapshot, peerSnapshot), default)));
        Assert.False(await db.Set<OfficialContributionRun>().AnyAsync(x => x.ProjectId == project.Id));
        var pending = new Project { CreatorId = owner.Profile.Id, Title = "Pending deletable project", Description = "fixture", ProjectField = "IT",
            Status = ProjectStatus.Pending, MemberTarget = 1, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1) };
        db.Add(pending); await db.SaveChangesAsync();
        await service.DeleteProjectAsync(pending.Id, owner.Id, default);
        Assert.False(await db.Set<ProjectContributionWorkflow>().AnyAsync(x => x.ProjectId == pending.Id));
    }

    [OfficialPostgresFact]
    public async Task OfficialApplicableZeroPeerMassCannotCreateAReadyResult()
    {
        var f = await OfficialFixture();
        var cohort = await OfficialCall(s => s.OpenPeerAsync(f.Project, f.Accounts[0], new(f.Profiles.Take(3).ToArray()), default));
        for (var from = 0; from < 3; from++) for (var to = 0; to < 3; to++) if (from != to)
            await OfficialCall(s => s.SubmitPeerAsync(f.Project, cohort.Id, f.Profiles[to], f.Accounts[from], new(1, null, null), default));
        var peer = await OfficialCall(s => s.ClosePeerAsync(f.Project, cohort.Id, f.Accounts[0], default));
        Assert.Equal("InsufficientData", (await OfficialCall(s => s.PeerSnapshotAsync(f.Project, peer, f.Accounts[0], default))).State);
        Assert.Equal("OFFICIAL_CALCULATION_NOT_READY", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.CreateAsync(f.Project,
            f.Accounts[0], new(Guid.NewGuid(), f.Snapshot, peer), default)))).Code);
        await using var db = OfficialDb();
        Assert.Equal(0, await db.GitContributionRuns.CountAsync(x => x.ProjectId == f.Project));
        Assert.Equal(0, await db.Set<OfficialContributionRun>().CountAsync(x => x.ProjectId == f.Project));
    }

    [OfficialPostgresFact]
    public async Task OfficialFinalizeSerializesAccountRevocationUntilCommit()
    {
        var f = await OfficialFixture();
        var run = await OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], new(Guid.NewGuid(), f.Snapshot, f.Peer), default));
        run = await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "submit", new(0), default));
        run = await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "approve", new(1), default));
        var probe = new OfficialRevocationProbe(f.Accounts[0]);
        await using (var source = OfficialDb())
        await using (var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql(source.Database.GetConnectionString()).AddInterceptors(probe).Options))
        {
            run = await OfficialService(db).TransitionAsync(f.Project, run.Id, f.Accounts[0], "finalize", new(2), default);
        }
        Assert.Equal("55P03", probe.ObservedSqlState);
        Assert.Equal("Finalized", run.State);
        await using var revoke = OfficialDb();
        await revoke.Accounts.Where(x => x.Id == f.Accounts[0]).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AccountStatus.Suspended));
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => OfficialCall(s => s.TransitionAsync(f.Project, run.Id,
            f.Accounts[0], "publish", new(run.Revision), default)));
        Assert.False(await revoke.Set<OfficialContributionPublication>().AnyAsync(x => x.ProjectId == f.Project));
        Assert.Equal(4, await revoke.Set<OfficialContributionAudit>().CountAsync(x => x.RunId == run.Id));
    }

    private sealed class OfficialRevocationProbe(Guid account) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public string? ObservedSqlState { get; private set; }
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await using var competing = OfficialDb();
            await using var tx = await competing.Database.BeginTransactionAsync(cancellationToken);
            await competing.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='150ms'; SET LOCAL statement_timeout='2s'", cancellationToken);
            var error = await Assert.ThrowsAsync<PostgresException>(() => competing.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Accounts\" SET \"Status\"=2 WHERE \"Id\"={account}", cancellationToken));
            ObservedSqlState = error.SqlState;
            await tx.RollbackAsync(cancellationToken);
            return result;
        }
    }

    [OfficialPostgresFact]
    public async Task OfficialStaleRevisionMissingPeerAndChangedRosterCannotFinalize()
    {
        var f = await OfficialFixture();
        var open = await OfficialCall(s => s.OpenPeerAsync(f.Project, f.Accounts[0], new(f.Profiles.Take(3).ToArray()), default));
        Assert.Equal("CONTRIBUTION_PEER_EVALUATIONS_INCOMPLETE", (await Assert.ThrowsAsync<ProjectException>(() =>
            OfficialCall(s => s.ClosePeerAsync(f.Project, open.Id, f.Accounts[0], default)))).Code);
        var run = await OfficialCall(s => s.CreateAsync(f.Project, f.Accounts[0], new(Guid.NewGuid(), f.Snapshot, f.Peer), default));
        run = await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "submit", new(0), default));
        Assert.Equal("OFFICIAL_REVISION_CONFLICT", (await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s =>
            s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "approve", new(0), default)))).Code);
        run = await OfficialCall(s => s.TransitionAsync(f.Project, run.Id, f.Accounts[0], "approve", new(1), default));
        await using var db = OfficialDb();
        await db.Accounts.Where(x => x.Id == f.Accounts[1]).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AccountStatus.Suspended));
        await Assert.ThrowsAsync<ProjectException>(() => OfficialCall(s => s.TransitionAsync(f.Project, run.Id,
            f.Accounts[0], "finalize", new(2), default)));
        Assert.Equal("Approved", (await db.Set<OfficialContributionRun>().AsNoTracking().SingleAsync(x => x.Id == run.Id)).State);
        Assert.False(await db.Set<OfficialContributionPublication>().AnyAsync(x => x.ProjectId == f.Project));
    }

    [OfficialPostgresFact]
    public async Task OfficialMigrationPreservesLegacyAndRequiresExplicitHistoricalSelection()
    {
        await using var db = OfficialDb("_upgrade"); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261119164008_AddProjectGitSnapshotCoverageReceipts");
        var old = await SeedProjectAsync(db); var finalized = await SeedProjectAsync(db);
        await db.ContributionCycles.Where(x => x.ProjectId == finalized.ProjectId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, ContributionCycleStatus.Finalized).SetProperty(x => x.FinalizedAt, DateTimeOffset.UtcNow));
        var snapshot = await new ProjectGitEvidenceSnapshotService(db).CaptureAsync(old.ProjectId, old.AccountIds[0], DateTimeOffset.UtcNow, default);
        var engine = new GitExperimentalContributionService(db, new ProjectGitContributionSnapshotAccess(new(db), new(db)), TimeProvider.System);
        var legacy = await engine.CreateRunAsync(old.ProjectId, old.AccountIds[0], new(snapshot, true), default);
        var missingSchema = await Assert.ThrowsAsync<ProjectException>(() => OfficialService(db).StatusAsync(old.ProjectId, old.AccountIds[0], default));
        Assert.Equal("OFFICIAL_SCHEMA_REQUIRED", missingSchema.Code);
        Assert.NotNull(await new ContributionService(db).GetCycleAsync(old.ProjectId, old.AccountIds[0], default));
        var before = await db.GitContributionRuns.AsNoTracking().SingleAsync(x => x.Id == legacy.RunId);
        await migrator.MigrateAsync();
        Assert.Equal("RequiresOptIn", (await OfficialService(db).StatusAsync(old.ProjectId, old.AccountIds[0], default)).Mode);
        Assert.Equal("LegacyV1", (await OfficialService(db).StatusAsync(finalized.ProjectId, finalized.AccountIds[0], default)).Mode);
        Assert.Equal("OFFICIAL_WORKFLOW_NOT_ENABLED", (await Assert.ThrowsAsync<ProjectException>(() =>
            OfficialService(db).CaptureAsync(finalized.ProjectId, finalized.AccountIds[0], new(DateTimeOffset.UtcNow), default))).Code);
        Assert.Equal(before.InputHash, (await engine.GetRunAsync(old.ProjectId, legacy.RunId, old.AccountIds[0], default)).InputHash);
        await Assert.ThrowsAsync<ProjectException>(() => OfficialService(db).CaptureAsync(old.ProjectId, old.AccountIds[0], new(DateTimeOffset.UtcNow), default));
        await migrator.MigrateAsync("20261119164008_AddProjectGitSnapshotCoverageReceipts");
        Assert.Null(await db.Database.SqlQueryRaw<string?>("SELECT to_regclass('public.\"OfficialContributionRuns\"')::text AS \"Value\"").SingleAsync());
        Assert.Equal(before.InputHash, (await db.GitContributionRuns.AsNoTracking().SingleAsync(x => x.Id == legacy.RunId)).InputHash);
        await migrator.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
    }
}
