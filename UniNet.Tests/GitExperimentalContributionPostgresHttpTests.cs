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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.Services;
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

public sealed class GitExperimentalContributionPostgresHttpTests
{
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
