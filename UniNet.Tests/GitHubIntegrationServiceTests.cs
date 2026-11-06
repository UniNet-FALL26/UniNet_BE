using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using UniNet.API.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubIntegrationServiceTests
{
    [Fact]
    public async Task ReauthorizationReusesConnectionAndScheduledReconciliationRestoresPendingLinks()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var owner = AddStudent(db, "reauthorization-owner");
        var project = CreateProject(owner.Profile!);
        var connection = new GitHubOAuthConnection
        {
            AccountId = owner.Id,
            ProviderAccountId = "12345",
            Login = "octocat",
            AccessTokenEncrypted = "",
            GrantedScopes = "repo",
            Status = GitHubOAuthConnectionStatus.ReauthorizationRequired
        };
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 3001,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = "repo",
            FullName = "octocat/repo",
            HtmlUrl = "https://github.com/octocat/repo",
            IsPrivate = false
        };
        var link = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = owner.Profile!.Id,
            AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization,
            LastAccessCheckedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        db.AddRange(owner, project, connection, repository, link);
        await db.SaveChangesAsync();

        var provider = new FakeGitHubProvider
        {
            Repository = new(3001, 123, "octocat", "repo", "octocat/repo",
                "https://github.com/octocat/repo", null, "main", false, false, true)
        };
        var oauth = new GitHubOAuthService(db, provider, new FakeTokenProtector(),
            Options.Create(ConfiguredOptions()));
        var start = await oauth.StartAsync(owner.Id, new("expo-web"), default);
        var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl).Query)["state"].ToString();
        var callback = await oauth.ProcessCallbackAsync("authorization-code", state, null, default);
        Assert.Equal("ready", QueryHelpers.ParseQuery(callback.RedirectUri.Query)["githubStatus"].ToString());
        var reauthorized = await oauth.CompleteAsync(owner.Id, new(start.FlowId), default);

        Assert.Equal(connection.Id, reauthorized.Id);
        Assert.Equal(1, await db.GitHubOAuthConnections.CountAsync());
        Assert.Equal(GitHubOAuthConnectionStatus.Active,
            (await db.GitHubOAuthConnections.SingleAsync()).Status);

        var disabledJobs = new GitSyncJobService(db, Options.Create(new GitSyncOptions { Enabled = false }));
        Assert.Equal(0, await disabledJobs.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, default));
        Assert.Equal(0, provider.RepositoryCalls);
        Assert.Empty(await db.GitSyncJobs.ToListAsync());

        var syncOptions = Options.Create(new GitSyncOptions { Enabled = true });
        var jobs = new GitSyncJobService(db, syncOptions);
        Assert.Equal(1, await jobs.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, default));
        var job = await db.GitSyncJobs.SingleAsync();
        Assert.Equal(GitSyncType.Scheduled, job.SyncType);
        var processor = new GitHubSyncJobProcessor(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));
        await processor.ProcessAsync(job, default);

        Assert.Equal(ProjectGitRepositoryAccessStatus.Active,
            (await db.ProjectGitRepositories.SingleAsync()).AccessStatus);
        Assert.Contains(await db.ProjectGitRepositoryHistory.ToListAsync(), x =>
            x.ProjectGitRepositoryId == link.Id && x.Action == ProjectGitRepositoryHistoryAction.AccessRestored);
    }

    [Fact]
    public async Task AccessLostScheduledReconciliationRetries404WithoutChangingAuthorization()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var owner = AddStudent(db, "missing-private-repo");
        var project = CreateProject(owner.Profile!);
        var connection = AddConnection(db, owner.Id, "missing-repo-github");
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 3002,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = "private",
            FullName = "octocat/private",
            HtmlUrl = "https://github.com/octocat/private",
            IsPrivate = true
        };
        var link = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = owner.Profile!.Id,
            AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost,
            LastAccessCheckedAt = DateTimeOffset.UtcNow.AddHours(-8)
        };
        db.AddRange(project, repository, link);
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider
        {
            RepositoryException = new GitHubIntegrationException(
                "GITHUB_RESOURCE_UNAVAILABLE", "GitHub resource unavailable.", 404)
        };
        var options = Options.Create(new GitSyncOptions { Enabled = true });
        var service = new GitSyncJobService(db, options);

        Assert.Equal(1, await service.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, default));
        var job = await db.GitSyncJobs.SingleAsync();
        var processor = new GitHubSyncJobProcessor(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(job, default));

        Assert.Equal("GITHUB_RESOURCE_UNAVAILABLE", error.Code);
        Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost,
            (await db.ProjectGitRepositories.SingleAsync()).AccessStatus);
        Assert.Equal(GitHubOAuthConnectionStatus.Active,
            (await db.GitHubOAuthConnections.SingleAsync()).Status);
        Assert.Empty(await db.ProjectGitRepositoryHistory.Where(x =>
            x.ProjectGitRepositoryId == link.Id && x.Action == ProjectGitRepositoryHistoryAction.AccessLost).ToListAsync());
    }

    [Fact]
    public async Task OAuthInvalidationMarksOnlyLinksUsingTheInvalidConnectionPendingAuthorization()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var owner = AddStudent(db, "oauth-scope-owner");
        var firstProject = CreateProject(owner.Profile!);
        var secondProject = CreateProject(owner.Profile!);
        var unrelatedProject = CreateProject(owner.Profile!);
        var invalidConnection = AddConnection(db, owner.Id, "invalid-oauth");
        var unrelatedConnection = AddConnection(db, owner.Id, "valid-oauth");
        var repositories = Enumerable.Range(0, 3).Select(index => new GitHubRepository
        {
            ProviderRepositoryId = 3100 + index,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = $"repo-{index}",
            FullName = $"octocat/repo-{index}",
            HtmlUrl = $"https://github.com/octocat/repo-{index}"
        }).ToArray();
        var links = new[]
        {
            new ProjectGitRepository
            {
                ProjectId = firstProject.Id, GitHubRepositoryId = repositories[0].Id,
                OAuthConnectionId = invalidConnection.Id, LinkedByProfileId = owner.Profile!.Id
            },
            new ProjectGitRepository
            {
                ProjectId = secondProject.Id, GitHubRepositoryId = repositories[1].Id,
                OAuthConnectionId = invalidConnection.Id, LinkedByProfileId = owner.Profile!.Id
            },
            new ProjectGitRepository
            {
                ProjectId = unrelatedProject.Id, GitHubRepositoryId = repositories[2].Id,
                OAuthConnectionId = unrelatedConnection.Id, LinkedByProfileId = owner.Profile!.Id
            }
        };
        db.AddRange(firstProject, secondProject, unrelatedProject, invalidConnection, unrelatedConnection);
        db.AddRange(repositories);
        db.AddRange(links);
        await db.SaveChangesAsync();

        var credentials = new GitHubCredentialService(db, new FakeGitHubProvider(), new FakeTokenProtector());
        await credentials.MarkReauthorizationRequiredAsync(invalidConnection.Id, owner.Profile!.Id, default);

        Assert.Equal(GitHubOAuthConnectionStatus.ReauthorizationRequired, invalidConnection.Status);
        Assert.Equal(ProjectGitRepositoryAccessStatus.PendingAuthorization, links[0].AccessStatus);
        Assert.Equal(ProjectGitRepositoryAccessStatus.PendingAuthorization, links[1].AccessStatus);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, links[2].AccessStatus);
        Assert.Equal(GitHubOAuthConnectionStatus.Active, unrelatedConnection.Status);
    }

    [Fact]
    public async Task RateLimitDoesNotChangeRepositoryOrOAuthAuthorizationState()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var owner = AddStudent(db, "rate-limit-owner");
        var project = CreateProject(owner.Profile!);
        var connection = AddConnection(db, owner.Id, "rate-limit-github");
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 3200,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = "rate-limited",
            FullName = "octocat/rate-limited",
            HtmlUrl = "https://github.com/octocat/rate-limited"
        };
        var link = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = owner.Profile!.Id
        };
        var job = new GitSyncJob
        {
            ProjectGitRepositoryId = link.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Initial
        };
        db.AddRange(project, repository, link, job);
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider
        {
            SyncException = new GitHubIntegrationException(
                "GITHUB_RATE_LIMITED", "GitHub rate limit reached.", 429, TimeSpan.FromMinutes(4))
        };
        var processor = new GitHubSyncJobProcessor(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));

        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(job, default));

        Assert.Equal("GITHUB_RATE_LIMITED", error.Code);
        Assert.Equal(TimeSpan.FromMinutes(4), error.RetryAfter);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, link.AccessStatus);
        Assert.Equal(GitHubOAuthConnectionStatus.Active, connection.Status);
        Assert.Empty(await db.ProjectGitRepositoryHistory.ToListAsync());
    }

    [Fact]
    public async Task OAuthFlowIsAccountBoundSingleUseAndNeverReturnsTokens()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var owner = AddStudent(db, "owner");
        var other = AddStudent(db, "other");
        await db.SaveChangesAsync();
        var service = new GitHubOAuthService(db, new FakeGitHubProvider(), new FakeTokenProtector(),
            Options.Create(ConfiguredOptions()));

        var start = await service.StartAsync(owner.Id, new("expo-web"), default);
        var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl).Query)["state"].ToString();
        var callback = await service.ProcessCallbackAsync("authorization-code", state, null, default);
        Assert.Equal("ready", QueryHelpers.ParseQuery(callback.RedirectUri.Query)["githubStatus"].ToString());

        var mobileStart = await service.StartAsync(owner.Id, new("expo-mobile"), default);
        var mobileState = QueryHelpers.ParseQuery(new Uri(mobileStart.AuthorizationUrl).Query)["state"].ToString();
        var mobileCallback = await service.ProcessCallbackAsync("mobile-code", mobileState, null, default);
        Assert.Equal("exp", mobileCallback.RedirectUri.Scheme);

        var unauthorized = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.CompleteAsync(other.Id, new(start.FlowId), default));
        Assert.Equal(403, unauthorized.Status);

        var connection = await service.CompleteAsync(owner.Id, new(start.FlowId), default);
        Assert.Equal("12345", connection.ProviderAccountId);
        Assert.Equal("octocat", connection.Login);
        Assert.DoesNotContain("github-access-token", System.Text.Json.JsonSerializer.Serialize(connection));
        Assert.Equal("protected:github-access-token",
            (await db.GitHubOAuthConnections.SingleAsync()).AccessTokenEncrypted);

        var replay = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.ProcessCallbackAsync("authorization-code", state, null, default));
        Assert.Equal("OAUTH_FLOW_ALREADY_USED", replay.Code);
    }

    [Fact]
    public async Task OAuthConnectionsAreUniquePerUniNetAccountButNotAcrossAccounts()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var firstAccount = AddStudent(db, "first");
        var secondAccount = AddStudent(db, "second");
        await db.SaveChangesAsync();
        var service = new GitHubOAuthService(db, new FakeGitHubProvider(), new FakeTokenProtector(),
            Options.Create(ConfiguredOptions()));

        async Task<GitHubOAuthConnectionResponse> ConnectAsync(Guid accountId, string code)
        {
            var start = await service.StartAsync(accountId, new("expo-web"), default);
            var state = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl).Query)["state"].ToString();
            await service.ProcessCallbackAsync(code, state, null, default);
            return await service.CompleteAsync(accountId, new(start.FlowId), default);
        }

        var firstConnection = await ConnectAsync(firstAccount.Id, "first-code");
        var repeatedConnection = await ConnectAsync(firstAccount.Id, "second-code");
        var otherAccountsConnection = await ConnectAsync(secondAccount.Id, "third-code");

        Assert.Equal(firstConnection.Id, repeatedConnection.Id);
        Assert.NotEqual(firstConnection.Id, otherAccountsConnection.Id);
        Assert.Equal(2, await db.GitHubOAuthConnections.CountAsync(x =>
            x.ProviderAccountId == firstConnection.ProviderAccountId &&
            x.Status == GitHubOAuthConnectionStatus.Active));
    }

    [Fact]
    public async Task OnlyCreatorCanGrantManagerAndLeavingMemberLosesGrant()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "creator");
        var manager = AddStudent(db, "manager");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Project = project,
            User = manager.Profile!,
            Role = "Any string is not a permission",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new ProjectGitManagerService(db);

        var grant = await service.GrantAsync(project.Id, creator.Id,
            new(manager.Profile!.Id), default);
        Assert.True(grant.IsActive);
        Assert.Equal(creator.Profile!.Id, grant.GrantedByProfileId);

        var forbidden = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.GrantAsync(project.Id, manager.Id, new(manager.Profile.Id), default));
        Assert.Equal(403, forbidden.Status);
        var revokeForbidden = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.RevokeAsync(project.Id, manager.Profile.Id, manager.Id, default));
        Assert.Equal(403, revokeForbidden.Status);

        await new ProjectMemberService(db).LeaveAsync(project.Id, manager.Id);
        var storedGrant = await db.ProjectGitManagerGrants.AsNoTracking().SingleAsync();
        Assert.False(storedGrant.IsActive);
        Assert.NotNull(storedGrant.RevokedAt);
        Assert.Equal(manager.Profile.Id, storedGrant.RevokedByProfileId);
    }

    [Fact]
    public async Task DuplicateActiveManagerGrantIsRejected()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "creator");
        var manager = AddStudent(db, "manager");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Project = project,
            User = manager.Profile!,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new ProjectGitManagerService(db);

        await service.GrantAsync(project.Id, creator.Id, new(manager.Profile!.Id), default);
        var duplicate = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.GrantAsync(project.Id, creator.Id, new(manager.Profile.Id), default));

        Assert.Equal("GIT_MANAGER_EXISTS", duplicate.Code);
        Assert.Single(await db.ProjectGitManagerGrants.ToListAsync());
    }

    [Fact]
    public async Task ManualSyncIsAuthorizedDeduplicatedAndRespectsPausedAssociations()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "sync-creator");
        var member = AddStudent(db, "sync-member");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Project = project,
            User = member.Profile!,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        var connection = AddConnection(db, creator.Id, "sync-owner");
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 987654,
            ProviderOwnerId = 12345,
            OwnerLogin = "octocat",
            Name = "sync-repository",
            FullName = "octocat/sync-repository",
            HtmlUrl = "https://github.com/octocat/sync-repository",
            LastVerifiedAt = DateTimeOffset.UtcNow
        };
        var link = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile!.Id
        };
        db.GitHubRepositories.Add(repository);
        db.ProjectGitRepositories.Add(link);
        await db.SaveChangesAsync();

        var service = new GitSyncJobService(db, Options.Create(new GitSyncOptions { Enabled = true }));
        var first = await service.RequestManualSyncAsync(creator.Id, project.Id, link.Id, default);
        var repeated = await service.RequestManualSyncAsync(creator.Id, project.Id, link.Id, default);

        Assert.False(first.WasAlreadyQueued);
        Assert.True(repeated.WasAlreadyQueued);
        Assert.Equal(first.Job.Id, repeated.Job.Id);
        Assert.Single(await db.GitSyncJobs.ToListAsync());

        var forbidden = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.RequestManualSyncAsync(member.Id, project.Id, link.Id, default));
        Assert.Equal(403, forbidden.Status);

        link.SyncPaused = true;
        await db.SaveChangesAsync();
        var paused = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.RequestManualSyncAsync(creator.Id, project.Id, link.Id, default));
        Assert.Equal("GITHUB_SYNC_PAUSED", paused.Code);

        link.SyncPaused = false;
        await db.SaveChangesAsync();
        var settings = await service.UpdateSyncSettingsAsync(creator.Id, project.Id, link.Id,
            new(12, true), default);
        Assert.Equal(12, settings.SyncIntervalHours);
        Assert.True(settings.SyncPaused);
        Assert.Equal(link.LinkedAt.AddHours(12), settings.NextScheduledSyncAt);

        var invalidInterval = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.UpdateSyncSettingsAsync(creator.Id, project.Id, link.Id, new(0), default));
        Assert.Equal("INVALID_SYNC_INTERVAL", invalidInterval.Code);
    }

    [Fact]
    public async Task SyncJobLeaseCanCompleteAndRetriesAreBounded()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "worker-creator");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        var connection = AddConnection(db, creator.Id, "worker-owner");
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 987655,
            ProviderOwnerId = 12345,
            OwnerLogin = "octocat",
            Name = "worker-repository",
            FullName = "octocat/worker-repository",
            HtmlUrl = "https://github.com/octocat/worker-repository",
            LastVerifiedAt = DateTimeOffset.UtcNow
        };
        var link = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile!.Id
        };
        db.GitHubRepositories.Add(repository);
        db.ProjectGitRepositories.Add(link);
        var completedJob = new GitSyncJob
        {
            ProjectGitRepositoryId = link.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Manual,
            Status = GitSyncJobStatus.Running
        };
        var completedLease = Guid.NewGuid();
        completedJob.LeaseId = completedLease;
        completedJob.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        completedJob.StartedAt = DateTimeOffset.UtcNow;
        db.GitSyncJobs.Add(completedJob);
        await db.SaveChangesAsync();

        var service = new GitSyncJobService(db, Options.Create(new GitSyncOptions { MaxRetryAttempts = 2 }));
        var oldLeaseExpiry = completedJob.LeaseExpiresAt!.Value;
        Assert.True(await service.RenewLeaseAsync(completedJob.Id, completedLease, TimeSpan.FromMinutes(3), default));
        var renewedJob = await db.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == completedJob.Id);
        Assert.True(renewedJob.LeaseExpiresAt > oldLeaseExpiry);
        Assert.NotNull(renewedJob.LastHeartbeatAt);
        Assert.False(await service.RenewLeaseAsync(completedJob.Id, Guid.NewGuid(), TimeSpan.FromMinutes(3), default));
        await service.CompleteAsync(completedJob.Id, completedLease, default);
        var storedCompletedJob = await db.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == completedJob.Id);
        Assert.Equal(GitSyncJobStatus.Completed, storedCompletedJob.Status);
        Assert.Null(storedCompletedJob.LeaseId);
        Assert.NotNull(storedCompletedJob.CompletedAt);

        var retryLease = Guid.NewGuid();
        var retryJob = new GitSyncJob
        {
            ProjectGitRepositoryId = link.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Manual,
            Status = GitSyncJobStatus.Running,
            LeaseId = retryLease,
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
            StartedAt = DateTimeOffset.UtcNow
        };
        db.GitSyncJobs.Add(retryJob);
        await db.SaveChangesAsync();
        var retryScheduled = await service.FailAsync(retryJob.Id, retryLease,
            "GITHUB_UNAVAILABLE", "GitHub is temporarily unavailable.",
            DateTimeOffset.UtcNow.AddMinutes(1), default);
        Assert.True(retryScheduled);
        Assert.Equal(GitSyncJobStatus.RetryScheduled, retryJob.Status);
        Assert.Equal(1, retryJob.RetryCount);

        retryJob.Status = GitSyncJobStatus.Running;
        retryJob.LeaseId = Guid.NewGuid();
        retryJob.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        await db.SaveChangesAsync();
        var terminal = await service.FailAsync(retryJob.Id, retryJob.LeaseId!.Value,
            "GITHUB_UNAVAILABLE", "GitHub is temporarily unavailable.",
            DateTimeOffset.UtcNow.AddMinutes(1), default);
        Assert.False(terminal);
        Assert.Equal(GitSyncJobStatus.Failed, retryJob.Status);
        Assert.NotNull(retryJob.CompletedAt);
        Assert.Null(retryJob.LeaseId);

        var terminalJob = new GitSyncJob
        {
            ProjectGitRepositoryId = link.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Webhook,
            Status = GitSyncJobStatus.Running,
            LeaseId = Guid.NewGuid(),
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2),
            StartedAt = DateTimeOffset.UtcNow
        };
        db.GitSyncJobs.Add(terminalJob);
        await db.SaveChangesAsync();
        Assert.False(await service.FailAsync(terminalJob.Id, terminalJob.LeaseId!.Value,
            "GITHUB_REPOSITORY_ACCESS_LOST", "GitHub denied repository access.",
            DateTimeOffset.UtcNow.AddMinutes(1), default, retryable: false));
        Assert.Equal(GitSyncJobStatus.Failed, terminalJob.Status);
        Assert.Equal(1, terminalJob.RetryCount);
    }

    [Fact]
    public async Task CreatorCanLinkWithoutMembershipButMembersCannotManageAndProjectScopeIsEnforced()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "creator");
        var member = AddStudent(db, "member");
        var firstProject = CreateProject(creator.Profile!);
        var secondProject = CreateProject(creator.Profile!);
        db.Projects.AddRange(firstProject, secondProject);
        db.ProjectMembers.Add(new ProjectMember
        {
            Project = firstProject,
            User = member.Profile!,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        var connection = AddConnection(db, creator.Id, "creator-github");
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider();
        var service = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));

        var denied = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.LinkAsync(member.Id, firstProject.Id, new(1001, connection.Id), default));
        Assert.Equal(403, denied.Status);

        var link = await service.LinkAsync(creator.Id, firstProject.Id, new(1001, connection.Id), default);
        var initialJob = await db.GitSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(link.Id, initialJob.ProjectGitRepositoryId);
        Assert.Equal(GitSyncType.Initial, initialJob.SyncType);
        Assert.Equal(GitSyncJobStatus.Pending, initialJob.Status);
        var crossProjectRead = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.GetAsync(creator.Id, secondProject.Id, link.Id, default));
        Assert.Equal(404, crossProjectRead.Status);
        var duplicate = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.LinkAsync(creator.Id, firstProject.Id, new(1001, connection.Id), default));
        Assert.Equal("GITHUB_REPOSITORY_ALREADY_LINKED", duplicate.Code);
    }

    [Fact]
    public async Task NewRepositoryLinkUsesConfiguredDefaultSyncInterval()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "interval-owner");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        var connection = AddConnection(db, creator.Id, "interval-github");
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider();
        var service = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()),
            Options.Create(new GitSyncOptions { DefaultIntervalHours = 12 }));

        var linked = await service.LinkAsync(creator.Id, project.Id, new(1001, connection.Id), default);

        Assert.NotEqual(Guid.Empty, linked.Id);
        Assert.Equal(12, (await db.ProjectGitRepositories.SingleAsync()).SyncIntervalHours);
    }

    [Fact]
    public async Task ScheduledJobsAreEnqueuedOnlyForDueAuthorizedUnpausedAssociations()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "scheduler");
        var project = CreateProject(creator.Profile!);
        var connection = AddConnection(db, creator.Id, "scheduler-github");
        GitHubRepository Repository(long id, string name) => new()
        {
            ProviderRepositoryId = id,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = name,
            FullName = $"octocat/{name}",
            HtmlUrl = $"https://github.com/octocat/{name}",
            IsPrivate = false,
            IsArchived = false,
            LastVerifiedAt = DateTimeOffset.UtcNow
        };
        var dueRepository = Repository(1001, "due");
        var recentRepository = Repository(1002, "recent");
        var pausedRepository = Repository(1003, "paused");
        var dueLink = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = dueRepository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile!.Id,
            LinkedAt = DateTimeOffset.UtcNow.AddHours(-7)
        };
        var recentLink = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = recentRepository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile.Id,
            LinkedAt = DateTimeOffset.UtcNow.AddHours(-2)
        };
        var pausedLink = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = pausedRepository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile.Id,
            SyncPaused = true,
            LinkedAt = DateTimeOffset.UtcNow.AddHours(-7)
        };
        db.AddRange(project, dueRepository, recentRepository, pausedRepository, dueLink, recentLink, pausedLink);
        await db.SaveChangesAsync();

        var service = new GitSyncJobService(db, Options.Create(new GitSyncOptions { Enabled = true }));
        var count = await service.EnqueueDueScheduledJobsAsync(DateTimeOffset.UtcNow, default);

        Assert.Equal(1, count);
        var scheduled = await db.GitSyncJobs.AsNoTracking().SingleAsync();
        Assert.Equal(dueLink.Id, scheduled.ProjectGitRepositoryId);
        Assert.Equal(GitSyncType.Scheduled, scheduled.SyncType);
        Assert.Equal(GitSyncJobStatus.Pending, scheduled.Status);
    }

    [Fact]
    public async Task SyncProcessorPersistsDeduplicatedObjectsCheckpointAndProjectScopedGrants()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "sync-processor");
        var project = CreateProject(creator.Profile!);
        var connection = AddConnection(db, creator.Id, "sync-processor-github");
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 1001,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = "repo",
            FullName = "octocat/repo",
            HtmlUrl = "https://github.com/octocat/repo",
            IsPrivate = true,
            IsArchived = false,
            LastVerifiedAt = DateTimeOffset.UtcNow
        };
        var link = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile!.Id
        };
        var job = new GitSyncJob
        {
            ProjectGitRepositoryId = link.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Initial
        };
        var secondProject = CreateProject(creator.Profile);
        var secondProjectMember = AddStudent(db, "second-project-member");
        db.ProjectMembers.Add(new ProjectMember
        {
            Project = secondProject,
            User = secondProjectMember.Profile!,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        var secondLink = new ProjectGitRepository
        {
            ProjectId = secondProject.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile.Id
        };
        db.AddRange(project, secondProject, repository, link, secondLink, job);
        await db.SaveChangesAsync();

        var provider = new FakeGitHubProvider();
        provider.SetSyncPagePages("branches", null,
            ("""[{"name":"main","commit":{"sha":"abc123"}}]""", true),
            ("""[{"name":"main","commit":{"sha":"abc123"}}]""", false));
        provider.SetSyncPage("commits", "main",
            """[{"sha":"abc123","commit":{"message":"change\n\nCo-authored-by: Pat Example <pat@example.test>","committer":{"date":"2026-11-04T10:00:00Z"}},"author":{"id":42,"login":"octocat"},"committer":{"id":43,"login":"octocat-ci"}}]""");
        provider.SetSyncPage("pull-requests", null,
            """[{"id":7001,"number":7,"state":"open","updated_at":"2026-11-04T10:00:00Z","merged_at":null,"user":{"id":42,"login":"octocat"},"head":{"sha":"abc123"},"base":{"sha":"main"}}]""");
        var processor = new GitHubSyncJobProcessor(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()),
            Options.Create(new GitSyncOptions { BatchSize = 23 }));

        await processor.ProcessAsync(job, default);

        Assert.Equal(new[] { "branches", "commits", "pull-requests" },
            await db.GitHubProviderObjects.AsNoTracking().OrderBy(x => x.Category)
                .Select(x => x.Category).ToArrayAsync());
        Assert.Equal(1, await db.GitActivities.CountAsync(x => x.ActivityType == "commit.created"));
        Assert.Equal(3, await db.GitActivityActors.CountAsync(x => x.GitActivityId ==
            db.GitActivities.Where(a => a.ActivityType == "commit.created").Select(a => a.Id).Single()));
        Assert.Contains(await db.GitActivityActors.ToListAsync(), x =>
            x.Role == "co-author" && x.Login == "Pat Example" && x.ExternalAccountId == null);
        Assert.Single(await db.GitCommitBranchReferences.ToListAsync());
        Assert.Equal(3, await db.ProjectGitActivityGrants.CountAsync(x => x.ProjectGitRepositoryId == link.Id));
        Assert.Empty(await db.ProjectGitActivityGrants.Where(x => x.ProjectGitRepositoryId == secondLink.Id).ToListAsync());
        Assert.Equal(GitSyncCheckpointStatus.Completed,
            (await db.GitSyncCheckpoints.SingleAsync(x => x.Category == "commits")).Status);
        var branchCheckpoint = await db.GitSyncCheckpoints.SingleAsync(x => x.Category == "branches");
        Assert.Equal("2", branchCheckpoint.Cursor);
        Assert.Equal(2, branchCheckpoint.ItemsProcessed);
        Assert.Equal(new[] { 1, 2 }, provider.SyncRequests
            .Where(x => x.Category == "branches").Select(x => x.Page).ToArray());
        Assert.NotEmpty(provider.SyncPageSizes);
        Assert.All(provider.SyncPageSizes, pageSize => Assert.Equal(23, pageSize));
        var activityService = new ProjectGitActivityService(db);
        Assert.True(repository.IsPrivate);
        Assert.Equal(repository.Id, (await db.ProjectGitRepositories
            .Where(x => x.Id == link.Id || x.Id == secondLink.Id)
            .Select(x => x.GitHubRepositoryId).Distinct().ToListAsync()).Single());
        var firstProjectActivities = await activityService.GetAsync(
            creator.Id, project.Id, 1, 50, "commit.created", null, null, default);
        var secondProjectActivities = await activityService.GetAsync(
            creator.Id, secondProject.Id, 1, 50, "commit.created", null, null, default);
        Assert.Single(firstProjectActivities.Items);
        Assert.Empty(secondProjectActivities.Items);
        var substitutedProjectId = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            activityService.GetAsync(secondProjectMember.Id, project.Id, 1, 50, null, null, null, default));
        Assert.Equal("FORBIDDEN", substitutedProjectId.Code);
        var repositoryService = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));
        var substitutedRepositoryLink = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            repositoryService.GetAsync(secondProjectMember.Id, secondProject.Id, link.Id, default));
        Assert.Equal("GITHUB_REPOSITORY_LINK_NOT_FOUND", substitutedRepositoryLink.Code);

        provider.SetSyncPage("pull-requests", null,
            """[{"id":7001,"number":7,"state":"closed","updated_at":"2026-11-04T12:00:00Z","merged_at":"2026-11-04T11:00:00Z","user":{"id":42,"login":"octocat"},"head":{"sha":"abc123"},"base":{"sha":"main"},"merge_commit_sha":"merge123"}]""");
        var updateJob = new GitSyncJob
        {
            ProjectGitRepositoryId = secondLink.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Webhook
        };
        db.GitSyncJobs.Add(updateJob);
        await db.SaveChangesAsync();
        await processor.ProcessAsync(updateJob, default);
        Assert.Empty((await activityService.GetAsync(
            creator.Id, project.Id, 1, 50, "pull_request.merged", null, null, default)).Items);
        Assert.Single((await activityService.GetAsync(
            creator.Id, secondProject.Id, 1, 50, "pull_request.merged", null, null, default)).Items);
        updateJob.Status = GitSyncJobStatus.Completed;
        updateJob.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        provider.SetSyncPage("branches", null, """[{"name":"main","commit":{"sha":"abc123"}}]""");
        provider.RejectSyncCategory = "commits";
        var accessLostJob = new GitSyncJob
        {
            ProjectGitRepositoryId = secondLink.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Scheduled
        };
        db.GitSyncJobs.Add(accessLostJob);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(accessLostJob, default));
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active,
            (await db.ProjectGitRepositories.SingleAsync(x => x.Id == link.Id)).AccessStatus);
        Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost,
            (await db.ProjectGitRepositories.SingleAsync(x => x.Id == secondLink.Id)).AccessStatus);
        Assert.Equal(GitHubOAuthConnectionStatus.Active,
            (await db.GitHubOAuthConnections.SingleAsync(x => x.Id == connection.Id)).Status);

        provider.RejectSyncCategory = null;
        provider.SetSyncPage("branches", null, """[{"name":"main","commit":{"sha":"new-main"}}]""");
        provider.SetSyncPage("commits", "main",
            """[{"sha":"after-access-loss","commit":{"message":"new private activity","committer":{"date":"2026-11-04T13:00:00Z"}},"author":{"id":42,"login":"octocat"}}]""");
        job.Status = GitSyncJobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        var remainingProjectJob = new GitSyncJob
        {
            ProjectGitRepositoryId = link.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            SyncType = GitSyncType.Webhook
        };
        db.GitSyncJobs.Add(remainingProjectJob);
        await db.SaveChangesAsync();
        await processor.ProcessAsync(remainingProjectJob, default);
        var newlySynced = await db.GitActivities.AsNoTracking()
            .Where(x => db.GitHubProviderObjects.Any(source =>
                source.Id == x.GitHubProviderObjectId && source.ExternalObjectId == "after-access-loss"))
            .Select(x => x.Id).SingleAsync();
        Assert.Contains(newlySynced, (await activityService.GetAsync(
            creator.Id, project.Id, 1, 50, "commit.created", null, null, default)).Items.Select(x => x.Id));
        Assert.DoesNotContain(newlySynced, (await activityService.GetAsync(
            creator.Id, secondProject.Id, 1, 50, "commit.created", null, null, default)).Items.Select(x => x.Id));

        var accessLostLink = await db.ProjectGitRepositories.SingleAsync(x => x.Id == secondLink.Id);
        var accessLostAt = accessLostLink.LastAccessCheckedAt!.Value;
        var observedAfterAccessLoss = accessLostAt.AddTicks(1);
        var postAccessLossObject = new GitHubProviderObject
        {
            GitHubRepositoryId = repository.Id,
            Category = "commits",
            ExternalObjectId = "observed-after-access-loss",
            OccurredAt = accessLostAt.AddMinutes(-1),
            FirstObservedAt = observedAfterAccessLoss,
            LastObservedAt = observedAfterAccessLoss,
            PayloadJson = "{\"sha\":\"observed-after-access-loss\",\"private_marker\":\"must-not-leak\"}"
        };
        var postAccessLossActivity = new GitActivity
        {
            GitHubProviderObjectId = postAccessLossObject.Id,
            ActivityType = "commit.created",
            OccurredAt = accessLostAt.AddMinutes(-1)
        };
        db.AddRange(postAccessLossObject, postAccessLossActivity, new ProjectGitActivityVisibility
        {
            ProjectGitRepositoryId = secondLink.Id,
            GitActivityId = postAccessLossActivity.Id,
            ObservedAt = observedAfterAccessLoss
        });
        await db.SaveChangesAsync();
        var afterAccessLoss = await activityService.GetAsync(
            creator.Id, secondProject.Id, 1, 50, "commit.created", null, null, default);
        Assert.Empty(afterAccessLoss.Items);

        var visibleActivity = await activityService.GetAsync(
            creator.Id, project.Id, 1, 50, "commit.created", null, null, default);
        var serializedActivity = System.Text.Json.JsonSerializer.Serialize(visibleActivity);
        Assert.DoesNotContain("PayloadJson", serializedActivity);
        Assert.DoesNotContain("private_marker", serializedActivity);
        Assert.DoesNotContain("access-token", serializedActivity);

        await repositoryService.UnlinkAsync(creator.Id, project.Id, link.Id, default);
        var unlinkCutoff = link.UnlinkedAt!.Value;
        Assert.Null(secondLink.UnlinkedAt);
        Assert.Equal(ProjectGitRepositoryAccessStatus.AccessLost, secondLink.AccessStatus);
        Assert.Single((await activityService.GetAsync(
            creator.Id, secondProject.Id, 1, 50, "pull_request.merged", null, null, default)).Items);
        var lateObject = new GitHubProviderObject
        {
            GitHubRepositoryId = repository.Id,
            Category = "commits",
            ExternalObjectId = "late-after-unlink",
            OccurredAt = unlinkCutoff.AddMinutes(-1),
            FirstObservedAt = unlinkCutoff.AddTicks(1),
            LastObservedAt = unlinkCutoff.AddTicks(1),
            PayloadJson = """{"sha":"late-after-unlink"}"""
        };
        var lateActivity = new GitActivity
        {
            GitHubProviderObjectId = lateObject.Id,
            ActivityType = "commit.created",
            OccurredAt = unlinkCutoff.AddMinutes(-1)
        };
        db.AddRange(lateObject, lateActivity, new ProjectGitActivityGrant
        {
            ProjectGitRepositoryId = link.Id,
            GitHubProviderObjectId = lateObject.Id,
            ObservedAt = unlinkCutoff.AddTicks(1)
        });
        await db.SaveChangesAsync();
        var afterUnlink = await activityService.GetAsync(
            creator.Id, project.Id, 1, 50, "commit.created", null, null, default);
        Assert.Equal(2, afterUnlink.Items.Count);
    }

    [Fact]
    public async Task WebhookRequiresSignatureAndDeduplicatesDurableSyncEnqueue()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "webhook");
        var project = CreateProject(creator.Profile!);
        var connection = AddConnection(db, creator.Id, "webhook-github");
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 1001,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = "repo",
            FullName = "octocat/repo",
            HtmlUrl = "https://github.com/octocat/repo",
            IsPrivate = true,
            IsArchived = false,
            LastVerifiedAt = DateTimeOffset.UtcNow
        };
        var link = new ProjectGitRepository
        {
            ProjectId = project.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = connection.Id,
            LinkedByProfileId = creator.Profile!.Id
        };
        db.AddRange(project, repository, link);
        await db.SaveChangesAsync();

        const string secret = "test-webhook-secret";
        const string payload = """{"repository":{"id":1001},"action":"opened"}""";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var deliveryId = Guid.NewGuid().ToString("D");
        var signature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payloadBytes)).ToLowerInvariant();
        var service = new GitHubWebhookService(db,
            Options.Create(new GitHubWebhookOptions { Secret = secret }));

        var accepted = await service.HandleAsync(deliveryId, "pull_request", signature, payloadBytes, default);
        var duplicate = await service.HandleAsync(deliveryId, "pull_request", signature, payloadBytes, default);
        var queuedJob = await db.GitSyncJobs.SingleAsync();
        queuedJob.Status = GitSyncJobStatus.RetryScheduled;
        queuedJob.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(3);
        await db.SaveChangesAsync();
        var coalesced = await service.HandleAsync(Guid.NewGuid().ToString("D"),
            "pull_request", signature, payloadBytes, default);
        Assert.False(accepted.Duplicate);
        Assert.Equal(1, accepted.EnqueuedJobCount);
        Assert.True(duplicate.Duplicate);
        Assert.False(coalesced.Duplicate);
        Assert.Equal(0, coalesced.EnqueuedJobCount);
        Assert.Equal(2, await db.GitHubWebhookDeliveries.CountAsync());
        queuedJob = await db.GitSyncJobs.SingleAsync();
        Assert.Equal(GitSyncType.Webhook, queuedJob.SyncType);
        Assert.Equal(deliveryId, queuedJob.IdempotencyKey);

        var invalidSignature = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.HandleAsync(Guid.NewGuid().ToString("D"), "push", "sha256=bad",
                payloadBytes, default));
        Assert.Equal(401, invalidSignature.Status);
        var unsupportedSignature = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), "{}"u8)).ToLowerInvariant();
        var unsupported = await service.HandleAsync(Guid.NewGuid().ToString("D"), "deployment",
            unsupportedSignature, "{}"u8.ToArray(), default);
        Assert.False(unsupported.SupportedEvent);
        Assert.Equal(1, await db.GitSyncJobs.CountAsync());
    }

    [Fact]
    public async Task UnlinkRetainsAssociationAndAppendOnlyHistory()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "creator");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        var connection = AddConnection(db, creator.Id, "creator-github");
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider();
        var service = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));
        var link = await service.LinkAsync(creator.Id, project.Id, new(1001, connection.Id), default);

        await service.UnlinkAsync(creator.Id, project.Id, link.Id, default);

        var storedLink = await db.ProjectGitRepositories.AsNoTracking().SingleAsync();
        Assert.NotNull(storedLink.UnlinkedAt);
        Assert.Equal(2, await db.ProjectGitRepositoryHistory.CountAsync());
        Assert.Contains(await db.ProjectGitRepositoryHistory.AsNoTracking().ToListAsync(),
            item => item.Action == ProjectGitRepositoryHistoryAction.Unlinked);
        Assert.Throws<InvalidOperationException>(() =>
        {
            var history = db.ProjectGitRepositoryHistory.First();
            history.Details = "tampered";
            db.SaveChanges();
        });
    }

    [Fact]
    public async Task SharedRepositoryIsDeduplicatedButLinksRemainProjectScoped()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "creator");
        var manager = AddStudent(db, "manager");
        var outsider = AddStudent(db, "outsider");
        var firstProject = CreateProject(creator.Profile!);
        var secondProject = CreateProject(creator.Profile!);
        db.Projects.AddRange(firstProject, secondProject);
        foreach (var project in new[] { firstProject, secondProject })
        {
            db.ProjectMembers.Add(new ProjectMember
            {
                Project = project,
                User = manager.Profile!,
                Role = "Member",
                Status = ProjectMemberStatus.Active,
                JoinedAt = DateTimeOffset.UtcNow
            });
            db.ProjectGitManagerGrants.Add(new ProjectGitManagerGrant
            {
                ProjectId = project.Id,
                UserProfileId = manager.Profile!.Id,
                GrantedByProfileId = creator.Profile!.Id
            });
        }
        var connection = new GitHubOAuthConnection
        {
            AccountId = manager.Id,
            ProviderAccountId = "12345",
            Login = "octocat",
            AccessTokenEncrypted = "protected:github-access-token",
            GrantedScopes = "repo",
            Status = GitHubOAuthConnectionStatus.Active
        };
        db.GitHubOAuthConnections.Add(connection);
        await db.SaveChangesAsync();

        var provider = new FakeGitHubProvider();
        var service = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));
        var firstLink = await service.LinkAsync(manager.Id, firstProject.Id,
            new(1001, connection.Id), default);
        var secondLink = await service.LinkAsync(manager.Id, secondProject.Id,
            new(1001, connection.Id), default);

        Assert.NotEqual(firstLink.Id, secondLink.Id);
        Assert.Equal(1, await db.GitHubRepositories.CountAsync());
        Assert.Equal(2, await db.ProjectGitRepositories.CountAsync());
        var firstPage = await service.GetAsync(creator.Id, firstProject.Id, 1, 1, default);
        Assert.Equal(1, firstPage.TotalCount);
        Assert.Single(firstPage.Items);

        await new ProjectMemberService(db).LeaveAsync(firstProject.Id, manager.Id);
        var firstStoredLink = await db.ProjectGitRepositories.AsNoTracking().SingleAsync(x => x.Id == firstLink.Id);
        var secondStoredLink = await db.ProjectGitRepositories.AsNoTracking().SingleAsync(x => x.Id == secondLink.Id);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, firstStoredLink.AccessStatus);
        Assert.Equal(connection.Id, firstStoredLink.OAuthConnectionId);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, secondStoredLink.AccessStatus);
        Assert.Equal(GitHubOAuthConnectionStatus.Active,
            (await db.GitHubOAuthConnections.AsNoTracking().SingleAsync()).Status);
        Assert.DoesNotContain(await db.ProjectGitRepositoryHistory.AsNoTracking().ToListAsync(),
            history => history.ProjectGitRepositoryId == firstLink.Id &&
                history.Action == ProjectGitRepositoryHistoryAction.AuthorizationPending);

        provider.RejectRepositoryRequests = true;
        var revokedToken = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.DiscoverAsync(manager.Id, secondProject.Id, connection.Id, 1, 50, default));
        Assert.Equal("GITHUB_REAUTH_REQUIRED", revokedToken.Code);
        Assert.Equal(409, revokedToken.Status);
        Assert.All(await db.ProjectGitRepositories.AsNoTracking().ToListAsync(),
            link => Assert.Equal(ProjectGitRepositoryAccessStatus.PendingAuthorization, link.AccessStatus));
        Assert.Equal(GitHubOAuthConnectionStatus.ReauthorizationRequired,
            (await db.GitHubOAuthConnections.AsNoTracking().SingleAsync()).Status);

        await new GitHubOAuthService(db, new FakeGitHubProvider(), new FakeTokenProtector(),
            Options.Create(ConfiguredOptions())).DisconnectAsync(manager.Id, connection.Id, default);
        Assert.All(await db.ProjectGitRepositories.AsNoTracking().ToListAsync(),
            link => Assert.Equal(ProjectGitRepositoryAccessStatus.PendingAuthorization, link.AccessStatus));
        var disconnected = await db.GitHubOAuthConnections.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubOAuthConnectionStatus.Revoked, disconnected.Status);
        Assert.Empty(disconnected.AccessTokenEncrypted);
        Assert.Null(disconnected.RefreshTokenEncrypted);
        Assert.Equal(2, await db.ProjectGitRepositories.CountAsync());

        var forbidden = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.GetAsync(outsider.Id, secondProject.Id, 1, 50, default));
        Assert.Equal(403, forbidden.Status);
    }

    [Fact]
    public async Task LinkingPrivateRepositoryRequiresProviderConfirmedReadAccess()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "creator");
        var member = AddStudent(db, "member");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Project = project,
            User = member.Profile!,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        db.ProjectGitManagerGrants.Add(new ProjectGitManagerGrant
        {
            ProjectId = project.Id,
            UserProfileId = member.Profile!.Id,
            GrantedByProfileId = creator.Profile!.Id
        });
        var connection = new GitHubOAuthConnection
        {
            AccountId = member.Id,
            ProviderAccountId = "12345",
            Login = "octocat",
            AccessTokenEncrypted = "protected:github-access-token",
            GrantedScopes = "repo",
            Status = GitHubOAuthConnectionStatus.Active
        };
        db.GitHubOAuthConnections.Add(connection);
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider
        {
            Repository = new(1001, 123, "octocat", "private", "octocat/private",
                "https://github.com/octocat/private", null, "main", true, false, false)
        };
        var service = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));

        var denied = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.LinkAsync(member.Id, project.Id, new(1001, connection.Id), default));

        Assert.Equal("GITHUB_REPOSITORY_READ_ACCESS_REQUIRED", denied.Code);
        Assert.Empty(await db.ProjectGitRepositories.ToListAsync());
    }

    [Fact]
    public async Task ExpiringOAuthCredentialRefreshesAndStoresOnlyProtectedTokens()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var account = AddStudent(db, "owner");
        var connection = new GitHubOAuthConnection
        {
            AccountId = account.Id,
            ProviderAccountId = "12345",
            Login = "octocat",
            AccessTokenEncrypted = "protected:expired-access",
            RefreshTokenEncrypted = "protected:valid-refresh",
            GrantedScopes = "repo offline_access",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.GitHubOAuthConnections.Add(connection);
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider();
        var service = new GitHubCredentialService(db, provider, new FakeTokenProtector());

        var token = await service.GetValidAccessTokenAsync(account.Id, connection.Id, account.Profile!.Id, default);

        Assert.Equal("refreshed-access", token);
        Assert.Equal(1, provider.RefreshCalls);
        var stored = await db.GitHubOAuthConnections.AsNoTracking().SingleAsync();
        Assert.Equal("protected:refreshed-access", stored.AccessTokenEncrypted);
        Assert.Equal("protected:refreshed-refresh", stored.RefreshTokenEncrypted);
    }

    [Fact]
    public async Task CredentialReplacementRequiresOwnedConnectionAndVerifiesAccess()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "creator");
        var manager = AddStudent(db, "manager");
        var project = CreateProject(creator.Profile!);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            Project = project,
            User = manager.Profile!,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow
        });
        db.ProjectGitManagerGrants.Add(new ProjectGitManagerGrant
        {
            ProjectId = project.Id,
            UserProfileId = manager.Profile!.Id,
            GrantedByProfileId = creator.Profile!.Id
        });
        var managerConnection = AddConnection(db, manager.Id, "manager-github");
        var creatorConnection = AddConnection(db, creator.Id, "creator-github");
        await db.SaveChangesAsync();
        var provider = new FakeGitHubProvider();
        var service = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));
        var link = await service.LinkAsync(manager.Id, project.Id,
            new(1001, managerConnection.Id), default);

        var notOwned = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.ReplaceCredentialAsync(manager.Id, project.Id, link.Id,
                new(creatorConnection.Id), default));
        Assert.Equal("GITHUB_CONNECTION_NOT_FOUND", notOwned.Code);
        Assert.Equal(managerConnection.Id,
            (await db.ProjectGitRepositories.AsNoTracking().SingleAsync()).OAuthConnectionId);

        var replaced = await service.ReplaceCredentialAsync(creator.Id, project.Id, link.Id,
            new(creatorConnection.Id), default);

        Assert.Equal(creatorConnection.Id, replaced.OAuthConnectionId);
        var queuedSync = await db.GitSyncJobs.SingleAsync(x => x.ProjectGitRepositoryId == link.Id);
        Assert.Equal(creatorConnection.Id, queuedSync.OAuthConnectionId);
        Assert.Contains(queuedSync.Status, new[] { GitSyncJobStatus.Pending, GitSyncJobStatus.RetryScheduled });
        var history = await db.ProjectGitRepositoryHistory.AsNoTracking()
            .SingleAsync(x => x.Action == ProjectGitRepositoryHistoryAction.CredentialReplaced);
        Assert.Equal(managerConnection.Id, history.PreviousOAuthConnectionId);
        Assert.Equal(creatorConnection.Id, history.NewOAuthConnectionId);
    }

    [Fact]
    public async Task CredentialReplacementRestoresOnlyItsProjectVisibilityScope()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var creator = AddStudent(db, "credential-scope-owner");
        var projectA = CreateProject(creator.Profile!);
        var projectB = CreateProject(creator.Profile!);
        var oldConnection = AddConnection(db, creator.Id, "old-private-repo-connection");
        var replacementConnection = AddConnection(db, creator.Id, "new-private-repo-connection");
        var repository = new GitHubRepository
        {
            ProviderRepositoryId = 2001,
            ProviderOwnerId = 123,
            OwnerLogin = "octocat",
            Name = "private",
            FullName = "octocat/private",
            HtmlUrl = "https://github.com/octocat/private",
            IsPrivate = true,
            IsArchived = false
        };
        var linkA = new ProjectGitRepository
        {
            ProjectId = projectA.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = oldConnection.Id,
            LinkedByProfileId = creator.Profile!.Id,
            AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost,
            LastAccessCheckedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var linkB = new ProjectGitRepository
        {
            ProjectId = projectB.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = oldConnection.Id,
            LinkedByProfileId = creator.Profile.Id,
            AccessStatus = ProjectGitRepositoryAccessStatus.Active
        };
        var source = new GitHubProviderObject
        {
            GitHubRepositoryId = repository.Id,
            Category = "commits",
            ExternalObjectId = "only-project-b-history",
            OccurredAt = DateTimeOffset.UtcNow.AddHours(-1),
            FirstObservedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            LastObservedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            PayloadJson = "{\"sha\":\"only-project-b-history\"}"
        };
        var activity = new GitActivity
        {
            GitHubProviderObjectId = source.Id,
            ActivityType = "commit.created",
            OccurredAt = source.OccurredAt!.Value
        };
        db.AddRange(projectA, projectB, oldConnection, replacementConnection, repository,
            linkA, linkB, source, activity, new ProjectGitActivityVisibility
            {
                ProjectGitRepositoryId = linkB.Id,
                GitActivityId = activity.Id,
                ObservedAt = source.FirstObservedAt
            });
        await db.SaveChangesAsync();

        var provider = new FakeGitHubProvider
        {
            Repository = new(2001, 123, "octocat", "private", "octocat/private",
                "https://github.com/octocat/private", null, "main", true, false, true)
        };
        var repositoryService = new ProjectGitRepositoryService(db, provider,
            new GitHubCredentialService(db, provider, new FakeTokenProtector()));
        var restored = await repositoryService.ReplaceCredentialAsync(creator.Id, projectA.Id, linkA.Id,
            new(replacementConnection.Id), default);

        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, restored.AccessStatus);
        Assert.Equal(replacementConnection.Id, restored.OAuthConnectionId);
        Assert.Equal(0, await db.ProjectGitActivityVisibilities.CountAsync(x =>
            x.ProjectGitRepositoryId == linkA.Id && x.GitActivityId == activity.Id));
        var activityService = new ProjectGitActivityService(db);
        Assert.Empty((await activityService.GetAsync(
            creator.Id, projectA.Id, 1, 50, null, null, null, default)).Items);
        Assert.Contains(activity.Id, (await activityService.GetAsync(
            creator.Id, projectB.Id, 1, 50, null, null, null, default)).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task ProviderClientUsesBearerCredentialAndValidatesPrivateRepositoryPermissions()
    {
        var handler = new FakeHttpHandler("""
            {
              "id": 1001,
              "name": "private",
              "full_name": "octocat/private",
              "html_url": "https://github.com/octocat/private",
              "private": true,
              "archived": false,
              "owner": { "id": 123, "login": "octocat" },
              "description": null,
              "default_branch": "main",
              "permissions": { "pull": true }
            }
            """);
        using var http = new HttpClient(handler);
        var provider = new GitHubProviderClient(http, Options.Create(new GitHubIntegrationOptions()));

        var repository = await provider.GetRepositoryAsync("access-token", 1001, default);

        Assert.True(repository.CanRead);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.True(handler.HasExpectedAccessToken);
        Assert.Equal("Bearer access-token", handler.AuthorizationHeader);
        Assert.Equal("https://api.github.com/repositories/1001", handler.RequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task ProviderSyncPageUsesEncodedBranchPaginationAndRateLimitMetadata()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds();
        var handler = new FakeHttpHandler("""[{"sha":"abc123"}]""")
        {
            LinkHeader = "<https://api.github.com/repos/octocat/repo/commits?page=3>; rel=\"next\"",
            RateLimitRemaining = "12",
            RateLimitReset = reset.ToString()
        };
        using var http = new HttpClient(handler);
        var provider = new GitHubProviderClient(http, Options.Create(new GitHubIntegrationOptions()));

        var result = await provider.GetSyncPageAsync(
            "access-token", "octocat/repo", "commits", "feature/a b", 2, 100, default);

        Assert.Single(result.Items);
        Assert.True(result.HasMore);
        Assert.Equal(12, result.RateLimitRemaining);
        Assert.NotNull(result.RateLimitResetAt);
        Assert.Contains("page=2", handler.RequestUri!.Query);
        Assert.Contains("sha=feature%2Fa%20b", handler.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task MissingOAuthConfigurationFailsWithoutPersistingAFlow()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var account = AddStudent(db, "owner");
        await db.SaveChangesAsync();
        var service = new GitHubOAuthService(db, new FakeGitHubProvider(), new FakeTokenProtector(),
            Options.Create(new GitHubIntegrationOptions()));

        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.StartAsync(account.Id, new("expo-web"), default));

        Assert.Equal("GITHUB_NOT_CONFIGURED", error.Code);
        Assert.Equal(503, error.Status);
        Assert.Empty(await db.GitHubOAuthAuthorizationFlows.ToListAsync());
    }

    [Fact]
    public async Task ExpiredOAuthFlowCompletionClearsPendingCredentialsButRetainsFlow()
    {
        await using var database = await CreateDatabaseAsync();
        var db = database.Context;
        var account = AddStudent(db, "owner");
        var flow = new GitHubOAuthAuthorizationFlow
        {
            AccountId = account.Id,
            StateHash = Guid.NewGuid().ToString("N"),
            ReturnTarget = "expo-web",
            Status = GitHubOAuthFlowStatus.AwaitingCompletion,
            PendingProviderAccountId = "12345",
            PendingLogin = "octocat",
            PendingAccessTokenEncrypted = "protected:expired-access",
            PendingRefreshTokenEncrypted = "protected:expired-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        db.GitHubOAuthAuthorizationFlows.Add(flow);
        await db.SaveChangesAsync();
        var service = new GitHubOAuthService(db, new FakeGitHubProvider(), new FakeTokenProtector(),
            Options.Create(ConfiguredOptions()));

        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            service.CompleteAsync(account.Id, new(flow.Id), default));

        var stored = await db.GitHubOAuthAuthorizationFlows.AsNoTracking().SingleAsync();
        Assert.Equal(410, error.Status);
        Assert.Equal(GitHubOAuthFlowStatus.Expired, stored.Status);
        Assert.Null(stored.PendingAccessTokenEncrypted);
        Assert.Null(stored.PendingRefreshTokenEncrypted);
        Assert.Null(stored.PendingProviderAccountId);
        Assert.Single(await db.GitHubOAuthAuthorizationFlows.ToListAsync());
    }

    [Fact]
    public void DataProtectionEncryptsTokensAndRoundTrips()
    {
        var protector = new GitHubTokenProtector(new EphemeralDataProtectionProvider());
        const string token = "github-sensitive-access-token";

        var protectedToken = protector.Protect(token);

        Assert.NotEqual(token, protectedToken);
        Assert.DoesNotContain(token, protectedToken, StringComparison.Ordinal);
        Assert.Equal(token, protector.Unprotect(protectedToken));
    }

    private static async Task<TestDatabase> CreateDatabaseAsync()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var context = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        return new TestDatabase(context, connection);
    }

    private static Account AddStudent(UniNetDbContext db, string name)
    {
        var account = new Account
        {
            Email = $"{name}-{Guid.NewGuid():N}@example.test",
            Role = AccountRole.Student,
            Status = AccountStatus.Active,
            Profile = new UserProfile { FullName = name, Nickname = name }
        };
        db.Accounts.Add(account);
        return account;
    }

    private static Project CreateProject(UserProfile creator) => new()
    {
        Creator = creator,
        Title = "Git integration project",
        ProjectField = "Engineering",
        Description = "Test project",
        MemberTarget = 5,
        MinimumQuantity = 1,
        RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1),
        Visibility = ProjectVisibility.Public,
        Status = ProjectStatus.Active,
        RecruitmentStatus = RecruitmentStatus.Open
    };

    private static GitHubOAuthConnection AddConnection(UniNetDbContext db, Guid accountId, string login)
    {
        var connection = new GitHubOAuthConnection
        {
            AccountId = accountId,
            ProviderAccountId = $"gh-{login}",
            Login = login,
            AccessTokenEncrypted = "protected:github-access-token",
            GrantedScopes = "repo",
            Status = GitHubOAuthConnectionStatus.Active
        };
        db.GitHubOAuthConnections.Add(connection);
        return connection;
    }

    private static GitHubIntegrationOptions ConfiguredOptions() => new()
    {
        ClientId = "client-id",
        ClientSecret = "client-secret",
        CallbackUrl = "https://api.example.test/api/github/oauth/callback",
        KeyRingPath = Path.GetTempPath(),
        ReturnTargets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["expo-web"] = "https://app.example.test/oauth/github",
            ["expo-mobile"] = "exp://127.0.0.1:8081/--/oauth/github"
        }
    };

    private sealed class FakeTokenProtector : IGitHubTokenProtector
    {
        public string Protect(string token) => $"protected:{token}";
        public string Unprotect(string protectedToken) => protectedToken["protected:".Length..];
    }

    private sealed class FakeGitHubProvider : IGitHubProviderClient
    {
        public GitHubRemoteRepository Repository { get; set; } =
            new(1001, 123, "octocat", "repo", "octocat/repo",
                "https://github.com/octocat/repo", null, "main", false, false, true);
        public int RefreshCalls { get; private set; }
        public int RepositoryCalls { get; private set; }
        public bool RejectRepositoryRequests { get; set; }
        public GitHubIntegrationException? RepositoryException { get; set; }
        public GitHubIntegrationException? SyncException { get; set; }
        public string? RejectSyncCategory { get; set; }
        private readonly Dictionary<(string Category, string? Scope), Queue<GitHubSyncPage>> syncPages = [];
        public List<(string Category, string? Scope, int Page)> SyncRequests { get; } = [];
        public List<int> SyncPageSizes { get; } = [];

        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) =>
            Task.FromResult(new GitHubOAuthTokens("github-access-token", "github-refresh-token", 3600, 86400, "repo read:user"));

        public Task<GitHubOAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct)
        {
            RefreshCalls++;
            return Task.FromResult(new GitHubOAuthTokens("refreshed-access", "refreshed-refresh", 3600, 86400, "repo"));
        }

        public Task<GitHubUser> GetAuthenticatedUserAsync(string accessToken, CancellationToken ct) =>
            Task.FromResult(new GitHubUser(12345, "octocat", "Octo Cat", null));

        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(
            string accessToken, int page, int pageSize, CancellationToken ct)
        {
            if (RejectRepositoryRequests)
                throw new GitHubIntegrationException("GITHUB_CREDENTIAL_INVALID", "Invalid test credential.", 401);
            return Task.FromResult<IReadOnlyList<GitHubRemoteRepository>>([]);
        }

        public Task<GitHubRemoteRepository> GetRepositoryAsync(string accessToken, long repositoryId, CancellationToken ct)
        {
            RepositoryCalls++;
            if (RepositoryException is not null) throw RepositoryException;
            return Task.FromResult(Repository with { Id = repositoryId });
        }

        public Task<GitHubSyncPage> GetSyncPageAsync(
            string accessToken, string repositoryFullName, string category, string? scope,
            int page, int pageSize, CancellationToken ct)
        {
            SyncRequests.Add((category, scope, page));
            SyncPageSizes.Add(pageSize);
            if (category == RejectSyncCategory)
                throw new GitHubIntegrationException("GITHUB_ACCESS_DENIED", "Access denied in test.", 403);
            if (SyncException is not null) throw SyncException;
            return Task.FromResult(syncPages.TryGetValue((category, scope), out var pages) && pages.Count > 0
                ? pages.Dequeue()
                : new GitHubSyncPage([], false, 5000, null));
        }

        public void SetSyncPage(string category, string? scope, params string[] pages)
        {
            SetSyncPagePages(category, scope, pages.Select(json => (json, false)).ToArray());
        }

        public void SetSyncPagePages(string category, string? scope, params (string Json, bool HasMore)[] pages)
        {
            syncPages[(category, scope)] = new Queue<GitHubSyncPage>(pages.Select(page =>
            {
                using var document = JsonDocument.Parse(page.Json);
                return new GitHubSyncPage(
                    document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray(),
                    page.HasMore, 5000, null);
            }));
        }
    }

    private sealed class TestDatabase(UniNetDbContext context, Microsoft.Data.Sqlite.SqliteConnection connection)
        : IAsyncDisposable
    {
        public UniNetDbContext Context { get; } = context;

        public async ValueTask DisposeAsync()
        {
                await Context.DisposeAsync();
                await connection.DisposeAsync();
        }
    }

    private sealed class FakeHttpHandler(string json) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationHeader { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public bool HasExpectedAccessToken { get; private set; }
        public string? LinkHeader { get; init; }
        public string? RateLimitRemaining { get; init; }
        public string? RateLimitReset { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            HasExpectedAccessToken = request.Headers.Authorization?.Parameter == "access-token";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
            if (LinkHeader is not null) response.Headers.TryAddWithoutValidation("Link", LinkHeader);
            if (RateLimitRemaining is not null)
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", RateLimitRemaining);
            if (RateLimitReset is not null) response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", RateLimitReset);
            return Task.FromResult(response);
        }
    }
}
