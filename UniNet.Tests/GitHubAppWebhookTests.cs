using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UniNet.API.Controllers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using UniNet.API.Services;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppWebhookTests
{
    private const string AppSecret = "fake-app-secret";
    private const string LegacySecret = "fake-legacy-secret";
    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
    private static string Sign(string secret, byte[] payload) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Bytes(secret), payload)).ToLowerInvariant();

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public UniNetDbContext Db = null!;
        public readonly IGitHubTokenProtector Protector = new GitHubTokenProtector(new EphemeralDataProtectionProvider());
        public GitHubAppWebhookService Service => MakeService(Protector);
        public UniNetDbContext Open(params IInterceptor[] interceptors) =>
            new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options);
        public GitHubAppWebhookService MakeService(IGitHubTokenProtector protector, string appSecret = AppSecret, string legacySecret = LegacySecret) =>
            new(Db, protector, Options.Create(new GitHubAppOptions { Enabled = true, WebhookSecret = appSecret }), Options.Create(new GitHubWebhookOptions { Secret = legacySecret }));
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); await f.connection.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(f.connection).Options);
            await f.Db.Database.EnsureCreatedAsync(); return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
        public async Task<GitHubAppInstallation> SeedAsync(bool withAppLink)
        {
            var installation = new GitHubAppInstallation { ProviderInstallationId = 123, OwnerAccountId = 9, OwnerLogin = "example", OwnerType = "User" };
            Db.Add(installation);
            if (withAppLink)
            {
                var owner = new Account { Email = "app-webhook@example.test", Role = AccountRole.Student, Status = AccountStatus.Active,
                    Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
                var project = new Project { Creator = owner.Profile!, Title = "Webhook", ProjectField = "Engineering", Description = "Test",
                    MemberTarget = 5, MinimumQuantity = 1, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1), Visibility = ProjectVisibility.Private,
                    Status = ProjectStatus.Active, RecruitmentStatus = RecruitmentStatus.Open };
                var repository = new GitHubRepository { ProviderRepositoryId = 456, ProviderOwnerId = 9, OwnerLogin = "example", Name = "repo",
                    FullName = "example/repo", HtmlUrl = "https://github.com/example/repo", IsPrivate = true };
                var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = 456, VerifiedAt = DateTimeOffset.UtcNow };
                var link = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id,
                    CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
                Db.AddRange(owner, project, repository, grant, link);
            }
            await Db.SaveChangesAsync(); return installation;
        }
    }

    [Theory]
    [InlineData("installation", "{\"action\":\"created\",\"installation\":{\"id\":123}}", "InstallationLifecycle")]
    [InlineData("installation_repositories", "{\"action\":\"added\",\"installation\":{\"id\":123},\"repositories_added\":[],\"repositories_removed\":[]}", "InstallationLifecycle")]
    [InlineData("push", "{\"installation\":{\"id\":123},\"repository\":{\"id\":456},\"ref\":\"refs/heads/main\"}", "RepositoryActivity")]
    [InlineData("pull_request", "{\"action\":\"opened\",\"installation\":{\"id\":123},\"repository\":{\"id\":456}}", "RepositoryActivity")]
    [InlineData("issues", "{\"action\":\"opened\",\"installation\":{\"id\":123},\"repository\":{\"id\":456}}", "RepositoryActivity")]
    [InlineData("issue_comment", "{\"action\":\"created\",\"installation\":{\"id\":123},\"repository\":{\"id\":456}}", "RepositoryActivity")]
    [InlineData("pull_request_review", "{\"action\":\"submitted\",\"installation\":{\"id\":123},\"repository\":{\"id\":456}}", "RepositoryActivity")]
    [InlineData("pull_request_review_comment", "{\"action\":\"created\",\"installation\":{\"id\":123},\"repository\":{\"id\":456}}", "RepositoryActivity")]
    [InlineData("release", "{\"action\":\"published\",\"installation\":{\"id\":123},\"repository\":{\"id\":456}}", "RepositoryActivity")]
    public async Task ValidEventsAreDurablyStagedAndNotProcessed(string eventName, string body, string route)
    {
        await using var f = await Fixture.CreateAsync(); await f.SeedAsync(withAppLink: true);
        var payload = Bytes(body); var id = Guid.NewGuid().ToString();
        var first = await f.Service.HandleAsync(id, eventName, Sign(AppSecret, payload), payload, default);
        Assert.False(first.Duplicate); Assert.True(first.SupportedEvent); Assert.Equal(route, first.Route); Assert.Equal("PendingHandler", first.Status);
        var saved = await f.Db.GitHubAppWebhookDeliveries.SingleAsync();
        Assert.Equal(body, Encoding.UTF8.GetString(Convert.FromBase64String(f.Protector.Unprotect(saved.PayloadEncrypted))));
        Assert.DoesNotContain(body, saved.PayloadEncrypted); Assert.Equal(0, saved.AttemptCount);
        var second = await f.Service.HandleAsync(id, eventName, Sign(AppSecret, payload), payload, default);
        Assert.True(second.Duplicate); Assert.Equal("PendingHandler", second.Status);
        Assert.Equal(1, await f.Db.GitHubAppWebhookDeliveries.CountAsync());
        Assert.Equal(0, await f.Db.GitSyncJobs.CountAsync()); Assert.Equal(0, await f.Db.GitActivities.CountAsync());
    }

    [Theory]
    [InlineData("missing")][InlineData("invalid")][InlineData("legacy")][InlineData("tampered")]
    public async Task InvalidSignatureNeverPersists(string mode)
    {
        await using var f = await Fixture.CreateAsync(); var body = Bytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456}}");
        var signature = mode switch { "missing" => "", "invalid" => "sha256=nope", "legacy" => Sign(LegacySecret, body), _ => Sign(AppSecret, Bytes("{}")) };
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.HandleAsync(Guid.NewGuid().ToString(), "push", signature, body, default));
        Assert.Equal("GITHUB_WEBHOOK_SIGNATURE_INVALID", error.Code); Assert.Empty(await f.Db.GitHubAppWebhookDeliveries.ToListAsync());
    }

    [Fact]
    public async Task MissingAppSecretAndOversizedBodyFailClosed()
    {
        await using var f = await Fixture.CreateAsync(); var payload = Bytes("{}");
        var missing = f.MakeService(f.Protector, "");
        Assert.Equal("GITHUB_APP_WEBHOOK_NOT_CONFIGURED", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => missing.HandleAsync(Guid.NewGuid().ToString(), "ping", "", payload, default))).Code);
        var limited = new GitHubAppWebhookService(f.Db, f.Protector, Options.Create(new GitHubAppOptions { Enabled = true, WebhookSecret = AppSecret, MaximumWebhookPayloadBytes = 1 }),
            Options.Create(new GitHubWebhookOptions { Secret = LegacySecret }));
        Assert.Equal("GITHUB_WEBHOOK_PAYLOAD_INVALID", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => limited.HandleAsync(Guid.NewGuid().ToString(), "ping", Sign(AppSecret, payload), payload, default))).Code);
        Assert.Empty(await f.Db.GitHubAppWebhookDeliveries.ToListAsync());
    }

    [Theory]
    [InlineData("push", "{}")][InlineData("installation", "{\"action\":\"created\"}")]
    [InlineData("push", "{\"installation\":{\"id\":123}}")][InlineData("installation", "{\"installation\":{\"id\":123}}")]
    public async Task SupportedEventsRequireSignedIdentifiers(string eventName, string body)
    {
        await using var f = await Fixture.CreateAsync(); var payload = Bytes(body);
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.HandleAsync(Guid.NewGuid().ToString(), eventName, Sign(AppSecret, payload), payload, default));
        Assert.Equal("GITHUB_WEBHOOK_PAYLOAD_INVALID", error.Code); Assert.Empty(await f.Db.GitHubAppWebhookDeliveries.ToListAsync());
    }

    [Fact]
    public async Task UnknownEventAndUnmatchedScopeRemainDistinctAndCannotCreateJobs()
    {
        await using var f = await Fixture.CreateAsync(); await f.SeedAsync(withAppLink: true);
        var unknown = Bytes("{\"zen\":\"fixture\"}");
        var ping = await f.Service.HandleAsync(Guid.NewGuid().ToString(), "ping", Sign(AppSecret, unknown), unknown, default);
        Assert.False(ping.SupportedEvent); Assert.Equal("Unsupported", ping.Status);
        var unmatched = Bytes("{\"installation\":{\"id\":999},\"repository\":{\"id\":456}}");
        var result = await f.Service.HandleAsync(Guid.NewGuid().ToString(), "push", Sign(AppSecret, unmatched), unmatched, default);
        Assert.Equal("AwaitingScope", result.Status); Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
        var wrongRepo = Bytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":789}}");
        Assert.Equal("AwaitingScope", (await f.Service.HandleAsync(Guid.NewGuid().ToString(), "push", Sign(AppSecret, wrongRepo), wrongRepo, default)).Status);
    }

    [Fact]
    public async Task ExistingDeliveryCannotBeReusedWithDifferentEventOrBody()
    {
        await using var f = await Fixture.CreateAsync(); var body = Bytes("{\"action\":\"opened\",\"installation\":{\"id\":123},\"repository\":{\"id\":456}}"); var id = Guid.NewGuid().ToString();
        await f.Service.HandleAsync(id, "push", Sign(AppSecret, body), body, default);
        var different = Bytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":789}}");
        Assert.Equal("GITHUB_WEBHOOK_DELIVERY_CONFLICT", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.HandleAsync(id, "push", Sign(AppSecret, different), different, default))).Code);
        Assert.Equal("GITHUB_WEBHOOK_DELIVERY_CONFLICT", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.Service.HandleAsync(id, "issues", Sign(AppSecret, body), body, default))).Code);
        Assert.Equal(1, await f.Db.GitHubAppWebhookDeliveries.CountAsync());
    }

    [Theory]
    [InlineData("unknown")][InlineData("revoked")][InlineData("unlinked")][InlineData("paused")][InlineData("lost")]
    public async Task RepositoryRoutingRequiresLocallyEligibleAppScope(string mode)
    {
        await using var f = await Fixture.CreateAsync(); var installation = await f.SeedAsync(withAppLink: true);
        var grant = await f.Db.Set<GitHubAppRepositoryGrant>().SingleAsync(); var link = await f.Db.ProjectGitRepositories.SingleAsync();
        if (mode == "unknown") installation.VerificationUnknown = true;
        if (mode == "revoked") grant.RevokedAt = DateTimeOffset.UtcNow;
        if (mode == "unlinked") link.UnlinkedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        if (mode == "paused") link.SyncPaused = true;
        if (mode == "lost") link.AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost;
        await f.Db.SaveChangesAsync();
        var payload = Bytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456}}");
        var result = await f.Service.HandleAsync(Guid.NewGuid().ToString(), "push", Sign(AppSecret, payload), payload, default);
        Assert.Equal("AwaitingScope", result.Status); Assert.Empty(await f.Db.GitSyncJobs.ToListAsync());
    }

    private sealed class BrokenProtector : IGitHubTokenProtector
    { public string Protect(string token) => throw new InvalidOperationException(); public string Unprotect(string token) => throw new InvalidOperationException(); }

    private sealed class FailFirstInsert : DbCommandInterceptor
    {
        private bool failed;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (!failed && command.CommandText.Contains("INSERT INTO \"GitHubAppWebhookDeliveries\"", StringComparison.Ordinal))
            { failed = true; throw new IOException("Simulated storage outage."); }
            return base.ReaderExecutingAsync(command, data, result, ct);
        }
    }

    [Fact]
    public async Task FailedDatabaseInsertCanBeRedeliveredInFreshRequest()
    {
        await using var f = await Fixture.CreateAsync(); var payload = Bytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456}}");
        var id = Guid.NewGuid().ToString(); var signature = Sign(AppSecret, payload);
        await using (var failedDb = f.Open(new FailFirstInsert()))
        {
            var service = new GitHubAppWebhookService(failedDb, f.Protector, Options.Create(new GitHubAppOptions { Enabled = true, WebhookSecret = AppSecret }),
                Options.Create(new GitHubWebhookOptions { Secret = LegacySecret }));
            await Assert.ThrowsAnyAsync<Exception>(() => service.HandleAsync(id, "push", signature, payload, default));
        }
        Assert.Empty(await f.Db.GitHubAppWebhookDeliveries.ToListAsync());
        Assert.False((await f.Service.HandleAsync(id, "push", signature, payload, default)).Duplicate);
        Assert.Single(await f.Db.GitHubAppWebhookDeliveries.ToListAsync());
    }

    [Fact]
    public async Task ControllerAcceptsRawSignedBodyAndReturnsOnlyStagingMetadata()
    {
        await using var f = await Fixture.CreateAsync(); await f.SeedAsync(withAppLink: true);
        var payload = Bytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456},\"marker\":\"private-fixture\"}");
        var context = new DefaultHttpContext(); context.Request.Body = new MemoryStream(payload);
        context.Request.Headers["X-GitHub-Delivery"] = Guid.NewGuid().ToString();
        context.Request.Headers["X-GitHub-Event"] = "push"; context.Request.Headers["X-Hub-Signature-256"] = Sign(AppSecret, payload);
        var controller = new GitHubAppWebhookController(f.Service) { ControllerContext = new ControllerContext { HttpContext = context } };
        var response = Assert.IsType<AcceptedResult>(await controller.Receive(default));
        var result = Assert.IsType<GitHubAppWebhookResult>(response.Value);
        Assert.Equal("PendingHandler", result.Status);
        Assert.DoesNotContain("private-fixture", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task StorageFailureAllowsRedeliveryAndSameIdAcrossSourcesIsIndependent()
    {
        await using var f = await Fixture.CreateAsync(); var appPayload = Bytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456}}"); var id = Guid.NewGuid().ToString();
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.MakeService(new BrokenProtector()).HandleAsync(id, "push", Sign(AppSecret, appPayload), appPayload, default));
        Assert.Equal("GITHUB_APP_WEBHOOK_STORAGE_UNAVAILABLE", error.Code); Assert.Empty(await f.Db.GitHubAppWebhookDeliveries.ToListAsync());
        var legacy = new GitHubWebhookService(f.Db, Options.Create(new GitHubWebhookOptions { Secret = LegacySecret }));
        var legacyPayload = Bytes("{\"repository\":{\"id\":456}}");
        Assert.False((await legacy.HandleAsync(id, "push", Sign(LegacySecret, legacyPayload), legacyPayload, default)).Duplicate);
        Assert.False((await f.Service.HandleAsync(id, "push", Sign(AppSecret, appPayload), appPayload, default)).Duplicate);
        Assert.Single(await f.Db.GitHubAppWebhookDeliveries.ToListAsync()); Assert.Single(await f.Db.GitHubWebhookDeliveries.ToListAsync());
        Assert.Equal("GITHUB_WEBHOOK_SOURCE_MISMATCH", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => legacy.HandleAsync(Guid.NewGuid().ToString(), "push", Sign(LegacySecret, appPayload), appPayload, default))).Code);
        Assert.Equal("GITHUB_APP_WEBHOOK_SECRET_CONFLICT", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => f.MakeService(f.Protector, LegacySecret).HandleAsync(Guid.NewGuid().ToString(), "push", Sign(LegacySecret, appPayload), appPayload, default))).Code);
    }
}
