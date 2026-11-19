using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitExperimentalContributionPostgresFactAttribute : FactAttribute
{
    public GitExperimentalContributionPostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("UNINET_GIT_MVP_TEST_PORT") is null)
            Skip = "Requires the explicitly verified Git Contribution disposable PostgreSQL.";
    }
}

public sealed partial class GitExperimentalContributionPostgresHttpTests
{
    [GitExperimentalContributionPostgresFact]
    public async Task CoverageMigrationPreservesLegacyRunSnapshotAndFacts()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("UNINET_GIT_MVP_TEST_PORT"), out var port) || port is <= 1024 or 55433 or > 65535)
            throw new InvalidOperationException("Unsafe coverage upgrade target.");
        const string name = "uninet_phase2_upgrade_disposable";
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql($"Host=127.0.0.1;Port={port};Database={name};Username=postgres;SSL Mode=Disable").Options);
        await db.Database.OpenConnectionAsync(); var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        Assert.Equal(name, connection.Database); Assert.Equal(port, connection.Port); Assert.Equal("postgres", connection.UserName);
        await db.GetService<IMigrator>().MigrateAsync("20261118152617_GuardGitV2FinalNormalizationReadiness");
        var seed = await SeedProjectAsync(db);
        var capture = new ProjectGitEvidenceSnapshotService(db); var reader = new ProjectGitEvidenceSnapshotReader(db);
        var snapshotId = await capture.CaptureAsync(seed.ProjectId, seed.AccountIds[0], DateTimeOffset.UtcNow, default);
        Assert.Equal(1, (await reader.ReadAsync(seed.ProjectId, snapshotId, seed.AccountIds[0], default)).PolicyVersion);
        var service = new GitExperimentalContributionService(db, new ProjectGitContributionSnapshotAccess(capture, reader), TimeProvider.System);
        var run = await service.CreateRunAsync(seed.ProjectId, seed.AccountIds[0], new(snapshotId, true), default);
        var before = await db.GitEvidenceVersions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ContentHash, x.FrozenFactsJson }).ToArrayAsync();
        var memberships = await db.Set<ProjectGitEvidenceSnapshotEntry>().CountAsync(x => x.SnapshotId == snapshotId);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Set<ProjectGitEvidenceSnapshotCoverage>().ToArrayAsync()); // No invented/backfilled receipts.
        Assert.Equal(before, await db.GitEvidenceVersions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.ContentHash, x.FrozenFactsJson }).ToArrayAsync());
        Assert.Equal(memberships, await db.Set<ProjectGitEvidenceSnapshotEntry>().CountAsync(x => x.SnapshotId == snapshotId));
        Assert.Equal(run.InputHash, (await service.GetRunAsync(seed.ProjectId, run.RunId, seed.AccountIds[0], default)).InputHash);
        Assert.Equal(1, (await reader.ReadAsync(seed.ProjectId, snapshotId, seed.AccountIds[0], default)).PolicyVersion);
        Assert.False(db.Database.HasPendingModelChanges());
    }
    private const string Issuer = "git-contribution-postgres-tests";
    private const string SigningKey = "git-contribution-postgres-tests-signing-key-32-bytes-minimum";

    [GitExperimentalContributionPostgresFact]
    public async Task RunsSnapshotScoringReviewAndFinalizationThroughHttpOnPostgres()
    {
        var portText = Environment.GetEnvironmentVariable("UNINET_GIT_MVP_TEST_PORT");
        if (!int.TryParse(portText, out var port) || port is <= 1024 or 55433 or > 65535)
            throw new InvalidOperationException("The Git Contribution disposable PostgreSQL port is invalid.");

        const string database = "uninet_git_mvp_disposable";
        var connectionString = $"Host=127.0.0.1;Port={port};Database={database};Username=postgres;SSL Mode=Disable";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<UniNetDbContext>(options => options.UseNpgsql(connectionString));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddGitExperimentalContributionServices();
        builder.Services.AddControllers().AddApplicationPart(typeof(GitExperimentalContributionController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Issuer,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = true,
                RoleClaimType = ClaimTypes.Role
            };
        });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            if (error is ProjectException projectError)
            {
                context.Response.StatusCode = projectError.Status;
                await context.Response.WriteAsJsonAsync(new { code = projectError.Code, message = projectError.Message });
                return;
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = error?.GetBaseException().Message });
        }));
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        Guid projectId;
        Guid snapshotId;
        Guid[] accountIds;
        Guid[] profileIds;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            await db.Database.OpenConnectionAsync();
            var connection = Assert.IsType<NpgsqlConnection>(db.Database.GetDbConnection());
            Assert.Equal("127.0.0.1", connection.Host);
            Assert.Equal(port, connection.Port);
            Assert.Equal(database, connection.Database);
            Assert.Equal("postgres", connection.UserName);
            await db.Database.CloseConnectionAsync();

            await db.Database.MigrateAsync();
            var seeded = await SeedProjectAsync(db);
            projectId = seeded.ProjectId;
            accountIds = seeded.AccountIds;
            profileIds = seeded.ProfileIds;
            snapshotId = await new ProjectGitEvidenceSnapshotService(db)
                .CaptureAsync(projectId, accountIds[0], DateTimeOffset.UtcNow, CancellationToken.None);
            var diagnostics = (GitExperimentalContributionService)scope.ServiceProvider.GetRequiredService<IGitExperimentalContributionService>();
            var firstDiagnostic = await diagnostics.DiagnoseGitAsync(projectId, snapshotId, accountIds[0], profileIds.Take(3).ToArray(), default);
            var secondDiagnostic = await diagnostics.DiagnoseGitAsync(projectId, snapshotId, accountIds[0], profileIds.Take(3).ToArray(), default);
            using var firstRead = System.Text.Json.JsonDocument.Parse(firstDiagnostic);
            using var secondRead = System.Text.Json.JsonDocument.Parse(secondDiagnostic);
            Assert.Equal(firstRead.RootElement.GetProperty("inputHash").GetString(), secondRead.RootElement.GetProperty("inputHash").GetString());
            Assert.Equal(await db.Set<ProjectGitEvidenceSnapshotEntry>().CountAsync(x => x.SnapshotId == snapshotId),
                firstRead.RootElement.GetProperty("observationAudit").GetArrayLength());
            Assert.Empty(await db.GitContributionRuns.Where(x => x.ProjectId == projectId).ToArrayAsync());
            Assert.Empty(await db.Set<ExperimentalPeerCohort>().Where(x => x.ProjectId == projectId).ToArrayAsync());
        }

        await app.StartAsync();
        try
        {
            using var leader = Client(app, accountIds[0]);
            using var memberA = Client(app, accountIds[1]);
            using var memberB = Client(app, accountIds[2]);
            using var delegatedLeader = Client(app, accountIds[1]);
            using var outsider = Client(app, accountIds[3]);
            using var anonymous = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var route = $"/api/projects/{projectId}/contribution/git-experimental";

            var anonymousCreate = await anonymous.PostAsJsonAsync($"{route}/runs",
                new GitExperimentalRunRequest(snapshotId, true));
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousCreate.StatusCode);

            var delegatedCreate = await delegatedLeader.PostAsJsonAsync($"{route}/runs",
                new GitExperimentalRunRequest(snapshotId, true));
            Assert.Equal(HttpStatusCode.Forbidden, delegatedCreate.StatusCode);

            var memberCreate = await memberB.PostAsJsonAsync($"{route}/runs",
                new GitExperimentalRunRequest(snapshotId, true));
            Assert.Equal(HttpStatusCode.Forbidden, memberCreate.StatusCode);

            var create = await leader.PostAsJsonAsync($"{route}/runs",
                new GitExperimentalRunRequest(snapshotId, true));
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            var run = await create.Content.ReadFromJsonAsync<GitExperimentalRunResponse>();
            Assert.NotNull(run);
            Assert.Equal(snapshotId, run.GitEvidenceSnapshotId);
            Assert.Equal("ExperimentalGitScoringV1", run.ScoringPolicyVersion);
            Assert.Equal(GitContributionRunStatus.Draft, run.Status);
            Assert.Equal(64, run.InputHash.Length);
            Assert.Equal(1, Assert.Single(run.Categories, x => x.Category == "Commit").EligibleActivityCount);
            Assert.Equal(1, Assert.Single(run.Categories, x => x.Category == "PR").EligibleActivityCount);
            Assert.Equal(1, Assert.Single(run.Categories, x => x.Category == "Review").EligibleActivityCount);
            Assert.Equal(1, Assert.Single(run.Categories, x => x.Category == "Issue").EligibleActivityCount);
            Assert.Contains(run.ExcludedActivities, x => x.Reason == "CountedUnderMergedPullRequest");

            var creatorScore = Assert.Single(run.Results, x => x.UserId == profileIds[0]);
            var memberAScore = Assert.Single(run.Results, x => x.UserId == profileIds[1]);
            var memberBScore = Assert.Single(run.Results, x => x.UserId == profileIds[2]);
            Assert.Equal(30m, creatorScore.GitScore);
            Assert.Equal(50m, memberAScore.GitScore);
            Assert.Equal(20m, memberBScore.GitScore);
            Assert.Equal(50m, creatorScore.PeerScore);
            Assert.Equal(50m, memberAScore.PeerScore);
            Assert.Equal(50m, memberBScore.PeerScore);
            Assert.Equal(38m, creatorScore.TotalScore);
            Assert.Equal(50m, memberAScore.TotalScore);
            Assert.Equal(32m, memberBScore.TotalScore);

            var retry = await leader.PostAsJsonAsync($"{route}/runs",
                new GitExperimentalRunRequest(snapshotId, true));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var retriedRun = await retry.Content.ReadFromJsonAsync<GitExperimentalRunResponse>();
            Assert.Equal(run.RunId, retriedRun!.RunId);

            var creatorBreakdown = await leader.GetAsync($"{route}/runs/{run.RunId}");
            Assert.Equal(HttpStatusCode.OK, creatorBreakdown.StatusCode);
            var delegatedBreakdown = await delegatedLeader.GetAsync($"{route}/runs/{run.RunId}");
            Assert.Equal(HttpStatusCode.Forbidden, delegatedBreakdown.StatusCode);
            var memberBreakdown = await memberA.GetAsync($"{route}/runs/{run.RunId}");
            Assert.Equal(HttpStatusCode.Forbidden, memberBreakdown.StatusCode);

            var memberReview = await memberA.PostAsJsonAsync($"{route}/runs/{run.RunId}/review",
                new GitExperimentalReviewRequest("approve", null));
            Assert.Equal(HttpStatusCode.Forbidden, memberReview.StatusCode);
            var memberFinalize = await memberA.PostAsync($"{route}/runs/{run.RunId}/finalize", null);
            Assert.Equal(HttpStatusCode.Forbidden, memberFinalize.StatusCode);
            var outsiderResultsBeforeFinalize = await outsider.GetAsync($"{route}/results");
            Assert.Equal(HttpStatusCode.Forbidden, outsiderResultsBeforeFinalize.StatusCode);

            var review = await leader.PostAsJsonAsync($"{route}/runs/{run.RunId}/review",
                new GitExperimentalReviewRequest("approve", null));
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
            var reviewedRun = await review.Content.ReadFromJsonAsync<GitExperimentalRunResponse>();
            Assert.Equal(GitContributionRunStatus.Review, reviewedRun!.Status);

            var finalize = await leader.PostAsync($"{route}/runs/{run.RunId}/finalize", null);
            Assert.Equal(HttpStatusCode.OK, finalize.StatusCode);
            var finalizedRun = await finalize.Content.ReadFromJsonAsync<GitExperimentalRunResponse>();
            Assert.Equal(GitContributionRunStatus.Finalized, finalizedRun!.Status);

            var memberResultsResponse = await memberA.GetAsync($"{route}/results");
            Assert.Equal(HttpStatusCode.OK, memberResultsResponse.StatusCode);
            var memberResults = await memberResultsResponse.Content.ReadFromJsonAsync<GitExperimentalFinalResultsResponse>();
            Assert.NotNull(memberResults);
            Assert.Equal(run.RunId, memberResults.RunId);
            Assert.Equal(3, memberResults.Results.Count);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            var finalizedRow = await verifyDb.GitContributionRuns.SingleAsync(x => x.Id == run.RunId);
            Assert.Equal(run.InputHash, finalizedRow.InputHash);
            var immutableFailure = await Assert.ThrowsAsync<PostgresException>(() =>
                verifyDb.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"GitContributionRuns\" SET \"ReviewFeedback\" = 'tampered' WHERE \"Id\" = {run.RunId}"));
            Assert.Equal("55000", immutableFailure.SqlState);

            var openPeer = await leader.PostAsJsonAsync($"{route}/peer-cohorts", new ExperimentalPeerOpenRequest([profileIds[1]]));
            Assert.Equal(HttpStatusCode.OK, openPeer.StatusCode);
            var cohort = (await openPeer.Content.ReadFromJsonAsync<ExperimentalPeerCohortResponse>())!;
            Assert.Equal(2, cohort.ParticipantIds.Count);
            var ratingA = await leader.PutAsJsonAsync($"{route}/peer-cohorts/{cohort.Id}/evaluations/{profileIds[1]}", new ExperimentalPeerSubmitRequest(5, "test-only A", null));
            Assert.Equal(HttpStatusCode.OK, ratingA.StatusCode);
            var ratingB = await memberA.PutAsJsonAsync($"{route}/peer-cohorts/{cohort.Id}/evaluations/{profileIds[0]}", new ExperimentalPeerSubmitRequest(3, "test-only B", null));
            Assert.Equal(HttpStatusCode.OK, ratingB.StatusCode);
            var edit = await leader.PutAsJsonAsync($"{route}/peer-cohorts/{cohort.Id}/evaluations/{profileIds[1]}", new ExperimentalPeerSubmitRequest(4, "test-only edit", 1));
            Assert.True(edit.StatusCode == HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
            var peerView = await memberA.GetFromJsonAsync<ExperimentalPeerCohortResponse>($"{route}/peer-cohorts/{cohort.Id}");
            Assert.Single(peerView!.Ratings); Assert.Empty(peerView.History); Assert.Equal(profileIds[1], peerView.Ratings[0].EvaluatorId);
            Assert.Equal(HttpStatusCode.Forbidden, (await memberA.PostAsync($"{route}/peer-cohorts/{cohort.Id}/close", null)).StatusCode);
            var close = await leader.PostAsync($"{route}/peer-cohorts/{cohort.Id}/close", null);
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
            var closedView = (await leader.GetFromJsonAsync<ExperimentalPeerCohortResponse>($"{route}/peer-cohorts/{cohort.Id}"))!;
            Assert.Equal(3, closedView.History.Count); Assert.NotNull(closedView.SnapshotId);
            Assert.Equal(HttpStatusCode.Conflict, (await memberA.PutAsJsonAsync($"{route}/peer-cohorts/{cohort.Id}/evaluations/{profileIds[0]}", new ExperimentalPeerSubmitRequest(5, null, 1))).StatusCode);
            var immutablePeer = await Assert.ThrowsAsync<PostgresException>(() => verifyDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ExperimentalPeerSnapshots\" SET \"InputHash\" = repeat('a',64) WHERE \"Id\" = {closedView.SnapshotId}"));
            Assert.Equal("55000", immutablePeer.SqlState);
            var closedRatingFailure = await Assert.ThrowsAsync<PostgresException>(() => verifyDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ExperimentalPeerSubmissions\" SET \"Score\"=5,\"Revision\"=\"Revision\"+1 WHERE \"CohortId\"={cohort.Id}"));
            Assert.Equal("55000", closedRatingFailure.SqlState);
            var historyFailure = await Assert.ThrowsAsync<PostgresException>(() => verifyDb.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ExperimentalPeerRevisions\" WHERE \"SubmissionId\" IN (SELECT \"Id\" FROM \"ExperimentalPeerSubmissions\" WHERE \"CohortId\"={cohort.Id})"));
            Assert.Equal("55000", historyFailure.SqlState);
            var createV2 = await leader.PostAsJsonAsync($"{route}/runs", new GitExperimentalRunRequest(snapshotId, true, null, "ExperimentalGitScoringV2", closedView.SnapshotId));
            Assert.Equal(HttpStatusCode.OK, createV2.StatusCode);
            var v2Run = (await createV2.Content.ReadFromJsonAsync<GitExperimentalRunResponse>())!;
            Assert.Equal(closedView.SnapshotId, v2Run.PeerSnapshotId); Assert.All(v2Run.Results, x => Assert.Null(x.ContributionPercent));
            var retryV2 = (await (await leader.PostAsJsonAsync($"{route}/runs", new GitExperimentalRunRequest(snapshotId, true, null, "ExperimentalGitScoringV2", closedView.SnapshotId))).Content.ReadFromJsonAsync<GitExperimentalRunResponse>())!;
            Assert.Equal(v2Run.RunId, retryV2.RunId);
            Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"{route}/runs/{v2Run.RunId}/review", new GitExperimentalReviewRequest("approve", null))).StatusCode);
            var v2Immutable = await Assert.ThrowsAsync<PostgresException>(() => verifyDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitContributionRuns\" SET \"ResultsJson\"='[]' WHERE \"Id\"={v2Run.RunId}"));
            Assert.Equal("55000", v2Immutable.SqlState);
            var notReadyFinalization = await Assert.ThrowsAsync<PostgresException>(() => verifyDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitContributionRuns\" SET \"Status\"=3,\"FinalizedAt\"=now(),\"FinalizedById\"={profileIds[0]} WHERE \"Id\"={v2Run.RunId}"));
            Assert.Equal("23514", notReadyFinalization.SqlState);
            Assert.Equal("CK_GitContributionRuns_V2FinalReady", notReadyFinalization.ConstraintName);

            // Both requests must wait on the same real PostgreSQL cohort row before racing.
            var raceCohort = (await (await leader.PostAsJsonAsync($"{route}/peer-cohorts", new ExperimentalPeerOpenRequest([profileIds[1]]))).Content.ReadFromJsonAsync<ExperimentalPeerCohortResponse>())!;
            Assert.Equal(HttpStatusCode.OK, (await leader.PutAsJsonAsync($"{route}/peer-cohorts/{raceCohort.Id}/evaluations/{profileIds[1]}", new ExperimentalPeerSubmitRequest(4, null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await memberA.PutAsJsonAsync($"{route}/peer-cohorts/{raceCohort.Id}/evaluations/{profileIds[0]}", new ExperimentalPeerSubmitRequest(3, null, null))).StatusCode);
            await using var lockDb = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(connectionString).Options);
            await using var lockTx = await lockDb.Database.BeginTransactionAsync();
            await lockDb.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"ExperimentalPeerCohorts\" WHERE \"Id\"={raceCohort.Id} FOR UPDATE").ToListAsync();
            var racingClose = leader.PostAsync($"{route}/peer-cohorts/{raceCohort.Id}/close", null);
            var racingEdit = memberA.PutAsJsonAsync($"{route}/peer-cohorts/{raceCohort.Id}/evaluations/{profileIds[0]}", new ExperimentalPeerSubmitRequest(2, null, 1));
            var lockDeadline = DateTimeOffset.UtcNow.AddSeconds(10); var blocked = false;
            try
            {
                while (DateTimeOffset.UtcNow < lockDeadline)
                {
                    var waiting = await verifyDb.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname={database} AND wait_event_type='Lock' AND query LIKE '%ExperimentalPeerCohorts%'").SingleAsync();
                    if (waiting >= 2) { blocked = true; break; }
                    await Task.Delay(20);
                }
            }
            finally { await lockTx.CommitAsync(); }
            var raceResponses = await Task.WhenAll(racingClose, racingEdit); Assert.True(blocked);
            foreach (var response in raceResponses)
                Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await leader.PostAsync($"{route}/peer-cohorts/{raceCohort.Id}/close", null)).StatusCode);
            var frozenPeer = await verifyDb.Set<ExperimentalPeerSnapshot>().AsNoTracking().SingleAsync(x => x.CohortId == raceCohort.Id);
            using var peerJson = System.Text.Json.JsonDocument.Parse(frozenPeer.InputJson);
            var frozenIncoming = peerJson.RootElement.GetProperty("ratings").EnumerateArray().Single(x => x.GetProperty("evaluatorId").GetGuid() == profileIds[1]);
            var currentIncoming = await verifyDb.Set<ExperimentalPeerSubmission>().AsNoTracking().SingleAsync(x => x.CohortId == raceCohort.Id && x.EvaluatorId == profileIds[1]);
            Assert.Equal(currentIncoming.Score, frozenIncoming.GetProperty("score").GetInt32());
            Assert.Equal(currentIncoming.Revision, frozenIncoming.GetProperty("revision").GetInt32());

            // Append new complete evidence; never alter the earlier immutable versions/snapshot.
            var association = await verifyDb.ProjectGitRepositories.AsNoTracking().SingleAsync(x=>x.ProjectId==projectId);
            var priorObservations = await verifyDb.ProjectEvidenceObservations.AsNoTracking().Where(x=>x.ProjectGitRepositoryId==association.Id).ToArrayAsync();
            var observedAt = DateTimeOffset.UtcNow;
            foreach(var oldObservation in priorObservations)
            {
                var oldVersion = await verifyDb.GitEvidenceVersions.AsNoTracking().SingleAsync(x=>x.Id==oldObservation.GitEvidenceVersionId);
                var oldFacts = System.Text.Json.JsonSerializer.Deserialize<GitFrozenFacts>(oldVersion.FrozenFactsJson)!;
                var facts = oldFacts with { SchemaVersion=4,Completeness="Complete",Details=oldFacts.Details! with {
                    MissingFacts=[],ParentShas=oldObservation.Category=="commits" ? [new string('d',40)] : null } };
                var canonical=GitFrozenFactsCanonicalizer.Canonicalize(facts);
                var version=new GitEvidenceVersion { GitHubProviderObjectId=oldVersion.GitHubProviderObjectId,GitHubRepositoryId=oldVersion.GitHubRepositoryId,
                    EvidenceSchemaVersion=4,CanonicalizationVersion=1,HashAlgorithm="SHA-256",ContentHash=canonical.Sha256,
                    FrozenFactsJson=Encoding.UTF8.GetString(canonical.Bytes),EvidenceStatus="Complete",CreatedAt=observedAt };
                verifyDb.Add(version);
                verifyDb.Add(new ProjectEvidenceObservation { ProjectGitRepositoryId=association.Id,GitHubRepositoryId=association.GitHubRepositoryId,
                    GitSyncJobId=oldObservation.GitSyncJobId,GitEvidenceVersionId=version.Id,Category=oldObservation.Category,ScopeKey=oldObservation.ScopeKey,
                    ObservedAt=observedAt,AuthorizationSource=oldObservation.AuthorizationSource,RetrievalCompleteness="Complete" });
            }
            await verifyDb.SaveChangesAsync();
            var readySnapshot=await new ProjectGitEvidenceSnapshotService(verifyDb).CaptureAsync(projectId,accountIds[0],DateTimeOffset.UtcNow,default);
            var completeCohort=(await (await leader.PostAsJsonAsync($"{route}/peer-cohorts",new ExperimentalPeerOpenRequest(profileIds.Take(3).ToArray()))).Content.ReadFromJsonAsync<ExperimentalPeerCohortResponse>())!;
            var clients=new[]{leader,memberA,memberB};
            for(var evaluator=0;evaluator<3;evaluator++) for(var evaluatee=0;evaluatee<3;evaluatee++)
                if(evaluator!=evaluatee) Assert.Equal(HttpStatusCode.OK,(await clients[evaluator].PutAsJsonAsync($"{route}/peer-cohorts/{completeCohort.Id}/evaluations/{profileIds[evaluatee]}",new ExperimentalPeerSubmitRequest(3,"disposable fixture",null))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await leader.PostAsync($"{route}/peer-cohorts/{completeCohort.Id}/close",null)).StatusCode);
            var readyPeer=(await leader.GetFromJsonAsync<ExperimentalPeerCohortResponse>($"{route}/peer-cohorts/{completeCohort.Id}"))!;
            var readyRequest=new GitExperimentalRunRequest(readySnapshot,true,null,"ExperimentalGitScoringV2",readyPeer.SnapshotId);
            var readyResponse=await leader.PostAsJsonAsync($"{route}/runs",readyRequest);
            Assert.True(readyResponse.IsSuccessStatusCode,await readyResponse.Content.ReadAsStringAsync());
            var readyRun=(await readyResponse.Content.ReadFromJsonAsync<GitExperimentalRunResponse>())!;
            Assert.Equal("Ready",readyRun.CalculationStatus);Assert.Equal(GitPeerNormalization.Version,readyRun.NormalizationPolicyVersion);
            Assert.Equal(0m,readyRun.ExternalContributionPercent);Assert.Equal(100m,readyRun.Results.Sum(x=>x.ContributionPercent));
            var readyRetry=(await (await leader.PostAsJsonAsync($"{route}/runs",readyRequest)).Content.ReadFromJsonAsync<GitExperimentalRunResponse>())!;
            Assert.Equal(readyRun.RunId,readyRetry.RunId);
            var persistedReady=await verifyDb.GitContributionRuns.AsNoTracking().SingleAsync(x=>x.Id==readyRun.RunId);
            Assert.NotNull(persistedReady.NormalizedResultsJson);Assert.Contains(GitPeerNormalization.Version,persistedReady.InputSnapshotJson);
            var stillV1=await verifyDb.GitContributionRuns.AsNoTracking().SingleAsync(x=>x.Id==run.RunId);
            Assert.Equal(run.InputHash,stillV1.InputHash);Assert.Null(stillV1.NormalizedResultsJson);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task<(Guid ProjectId, Guid[] AccountIds, Guid[] ProfileIds)> SeedProjectAsync(UniNetDbContext db)
    {
        var accounts = Enumerable.Range(0, 4).Select(index => MakeAccount($"git-mvp-{index}")).ToArray();
        var profiles = accounts.Select(x => x.Profile!).ToArray();
        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            CreatorId = profiles[0].Id,
            Title = "Experimental Git Contribution PostgreSQL run",
            ProjectField = "Software",
            Description = "Disposable integration fixture",
            MemberTarget = 3,
            MinimumQuantity = 1,
            RecruitmentDeadline = now.AddDays(-1),
            ExpectedOutput = now.AddDays(-1),
            Visibility = ProjectVisibility.Private,
            Status = ProjectStatus.Completed,
            RecruitmentStatus = RecruitmentStatus.Closed,
            CreatedAt = now.AddDays(-30),
            UpdatedAt = now,
            CompletedAt = now.AddDays(-1)
        };
        var members = profiles.Take(3).Select((profile, index) => new ProjectMember
        {
            ProjectId = project.Id,
            UserId = profile.Id,
            Role = index == 0 ? "Creator" : index == 1 ? "Leader" : "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = now.AddDays(-20),
            CreatedAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-20)
        }).ToArray();
        var cycle = new ContributionCycle
        {
            ProjectId = project.Id,
            OpenedById = profiles[0].Id,
            OpenedAt = now.AddDays(-1),
            FormulaVersion = "1",
            Participants = profiles.Take(3).Select((profile, index) => new ContributionParticipant
            {
                UserId = profile.Id,
                RoleAtSnapshot = index == 0 ? "Creator" : index == 1 ? "Leader" : "Member",
                MembershipStatusAtSnapshot = ProjectMemberStatus.Active,
                JoinedAt = now.AddDays(-20),
                IsLeader = index == 0
            }).ToList()
        };
        var peerEvaluations = profiles.Take(3).SelectMany((evaluator, evaluatorIndex) =>
            profiles.Take(3).Where((_, evaluateeIndex) => evaluatorIndex != evaluateeIndex).Select(evaluatee =>
                new ContributionPeerEvaluation
                {
                    CycleId = cycle.Id,
                    EvaluatorUserId = evaluator.Id,
                    EvaluateeUserId = evaluatee.Id,
                    Score = 3
                })).ToArray();
        var oauthConnections = profiles.Take(3).Select((profile, index) => new GitHubOAuthConnection
        {
            AccountId = accounts[index].Id,
            ProviderAccountId = (101L + index).ToString(),
            Login = $"verified-{index}",
            AccessTokenEncrypted = "fixture-token",
            GrantedScopes = "read:user"
        }).ToArray();
        var identities = profiles.Take(3).Select((profile, index) => new ProjectGitMemberIdentity
        {
            ProjectId = project.Id,
            UserProfileId = profile.Id,
            Provider = "github",
            ExternalAccountId = 101 + index,
            ExternalUsername = $"verified-{index}",
            VerifiedByConnectionId = oauthConnections[index].Id,
            LinkedAt = now.AddDays(-5),
            LastActivatedAt = now.AddDays(-5)
        }).ToArray();
        var identityHistory = identities.Select((identity, index) => new ProjectGitMemberIdentityHistory
        {
            IdentityId = identity.Id,
            ProjectId = project.Id,
            UserProfileId = identity.UserProfileId,
            Provider = "github",
            ExternalAccountId = identity.ExternalAccountId,
            ExternalUsername = identity.ExternalUsername,
            Action = ProjectGitMemberIdentityHistoryAction.Linked,
            NewStatus = ProjectGitMemberIdentityStatus.Active,
            PerformedByProfileId = identity.UserProfileId,
            VerifiedByConnectionId = oauthConnections[index].Id,
            OccurredAt = now.AddDays(-5)
        }).ToArray();

        var providerRepositoryId = Random.Shared.NextInt64(1_000_000, long.MaxValue / 4);
        var installation = new GitHubAppInstallation
        {
            ProviderInstallationId = providerRepositoryId,
            OwnerAccountId = 9001,
            OwnerLogin = "disposable-owner",
            OwnerType = "User",
            Status = GitHubAppInstallationStatus.Active
        };
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = providerRepositoryId + 1,
            ProviderOwnerId = 9001,
            OwnerLogin = "disposable-owner",
            Name = "git-mvp",
            FullName = $"disposable-owner/git-mvp-{Guid.NewGuid():N}",
            HtmlUrl = "https://example.test/disposable/git-mvp"
        };
        var grant = new GitHubAppRepositoryGrant
        {
            InstallationId = installation.Id,
            ProviderRepositoryId = repository.ProviderRepositoryId,
            VerifiedAt = now
        };
        var projectRepository = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            LinkedByProfileId = profiles[0].Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp,
            GitHubAppRepositoryGrantId = grant.Id,
            AccessStatus = ProjectGitRepositoryAccessStatus.Active,
            SyncPaused = true
        };
        var syncJob = new GitSyncJob
        {
            ProjectGitRepositoryId = projectRepository.Id,
            GitHubRepositoryId = repository.Id,
            CredentialSource = GitRepositoryCredentialSource.GitHubApp,
            GitHubAppRepositoryGrantId = grant.Id,
            RequestedByProfileId = profiles[0].Id,
            SyncType = GitSyncType.Initial
        };

        var at = now.AddMinutes(-10);
        var commitPrSha = new string('a', 40);
        var commitMemberSha = new string('b', 40);
        var evidence = new[]
        {
            Evidence(repository.Id, projectRepository.Id, syncJob.Id, "commits", "", commitPrSha,
                Facts("commits", commitPrSha, "author", 101, at, Details(missingFacts: ["PrAndPatchRelationships"])), at),
            Evidence(repository.Id, projectRepository.Id, syncJob.Id, "commits", "", commitMemberSha,
                Facts("commits", commitMemberSha, "author", 102, at, Details(missingFacts: ["PrAndPatchRelationships"])), at),
            Evidence(repository.Id, projectRepository.Id, syncJob.Id, "pull-requests", "", "10",
                Facts("pull-requests", "10", "actor", 101, at,
                    Details(mergedAt: at, state: "closed", merged: true, relatedPullRequestNumber: 10,
                        missingFacts: ["PatchAndAllocationEvidence"])), at),
            Evidence(repository.Id, projectRepository.Id, syncJob.Id, "pull-request-commits", "10", commitPrSha,
                Facts("pull-request-commits", commitPrSha, "author", 101, at,
                    Details(relatedPullRequestNumber: 10)), at),
            Evidence(repository.Id, projectRepository.Id, syncJob.Id, "pull-request-reviews", "10", "review-1",
                Facts("pull-request-reviews", "review-1", "actor", 103, at,
                    Details(submittedAt: at, state: "APPROVED", relatedPullRequestNumber: 10)), at),
            Evidence(repository.Id, projectRepository.Id, syncJob.Id, "issues", "", "issue-1",
                Facts("issues", "issue-1", "actor", 102, at,
                    Details(createdAt: at, state: "open", isPullRequest: false, relatedIssueNumber: 20)), at)
        };

        db.Accounts.AddRange(accounts);
        db.Projects.Add(project);
        db.ProjectMembers.AddRange(members);
        db.ContributionCycles.Add(cycle);
        db.ContributionPeerEvaluations.AddRange(peerEvaluations);
        db.GitHubOAuthConnections.AddRange(oauthConnections);
        db.ProjectGitMemberIdentities.AddRange(identities);
        db.ProjectGitMemberIdentityHistory.AddRange(identityHistory);
        db.Set<GitHubAppInstallation>().Add(installation);
        db.GitHubRepositories.Add(repository);
        db.Set<GitHubAppRepositoryGrant>().Add(grant);
        db.ProjectGitRepositories.Add(projectRepository);
        db.GitSyncJobs.Add(syncJob);
        foreach (var item in evidence)
        {
            db.GitHubProviderObjects.Add(item.ProviderObject);
            db.GitEvidenceVersions.Add(item.Version);
            db.ProjectEvidenceObservations.Add(item.Observation);
        }
        await db.SaveChangesAsync();

        return (project.Id, accounts.Select(x => x.Id).ToArray(), profiles.Select(x => x.Id).ToArray());
    }

    private static (GitHubProviderObject ProviderObject, GitEvidenceVersion Version, ProjectEvidenceObservation Observation)
        Evidence(Guid repositoryId, Guid projectRepositoryId, Guid jobId, string category, string scope, string objectId,
            GitFrozenFacts facts, DateTimeOffset observedAt)
    {
        var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
        var providerObject = new GitHubProviderObject
        {
            GitHubRepositoryId = repositoryId,
            Category = category,
            ExternalObjectId = objectId,
            OccurredAt = observedAt,
            FirstObservedAt = observedAt,
            LastObservedAt = observedAt,
            PayloadJson = "{}",
            RetrievalMetadataJson = "{}"
        };
        var version = new GitEvidenceVersion
        {
            GitHubProviderObjectId = providerObject.Id,
            GitHubRepositoryId = repositoryId,
            EvidenceSchemaVersion = facts.SchemaVersion,
            CanonicalizationVersion = GitFrozenFactsCanonicalizer.CanonicalizationVersion,
            ContentHash = canonical.Sha256,
            FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes),
            EvidenceStatus = "Incomplete",
            CreatedAt = observedAt
        };
        var observation = new ProjectEvidenceObservation
        {
            ProjectGitRepositoryId = projectRepositoryId,
            GitSyncJobId = jobId,
            GitEvidenceVersionId = version.Id,
            GitHubRepositoryId = repositoryId,
            Category = category,
            ScopeKey = scope,
            ObservedAt = observedAt,
            AuthorizationSource = "app",
            RetrievalCompleteness = "Incomplete"
        };
        return (providerObject, version, observation);
    }

    private static GitFrozenFacts Facts(
        string category, string externalId, string actorRole, long accountId, DateTimeOffset at, GitFrozenDetails details) =>
        new(2, category, externalId, at, at, null, [new(actorRole, accountId.ToString())], "Incomplete", details);

    private static GitFrozenDetails Details(
        DateTimeOffset? createdAt = null,
        DateTimeOffset? submittedAt = null,
        DateTimeOffset? mergedAt = null,
        string? state = null,
        bool? merged = null,
        long? relatedPullRequestNumber = null,
        bool? isPullRequest = null,
        long? relatedIssueNumber = null,
        IReadOnlyList<string>? missingFacts = null) =>
        new(createdAt, null, null, mergedAt, submittedAt, state, merged, null, null, null,
            relatedPullRequestNumber, isPullRequest, missingFacts ?? [], relatedIssueNumber);

    private static Account MakeAccount(string name)
    {
        var account = new Account
        {
            Email = $"{name}-{Guid.NewGuid():N}@example.test",
            Role = AccountRole.Student,
            Status = AccountStatus.Active,
            Profile = new UserProfile
            {
                FullName = name,
                Nickname = name,
                IsVerified = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };
        account.Profile.AccountId = account.Id;
        return account;
    }

    private static HttpClient Client(WebApplication app, Guid accountId)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
            new Claim(ClaimTypes.Role, "Student")
        };
        var token = new JwtSecurityToken(Issuer, Issuer, claims, expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
}
