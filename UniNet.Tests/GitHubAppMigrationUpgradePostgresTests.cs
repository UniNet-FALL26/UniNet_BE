using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.DataProtection;
using UniNet.API.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Net.Http.Json;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class Step5aPostgresFactAttribute : FactAttribute
{
    public Step5aPostgresFactAttribute() { if (Environment.GetEnvironmentVariable("UNINET_STEP5A_DISPOSABLE") != "1") Skip = "Requires the verified Step 5A disposable PostgreSQL container on 127.0.0.1:55433."; }
}

public sealed class GitHubAppMigrationUpgradePostgresTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private const string BeforeApp = "20261105123505_UpdatePackageIdOptional";
    private const string Admin = "Host=127.0.0.1;Port=55433;Database=postgres;Username=postgres;Timeout=5";

    // Each run gets a new database; no existing database is dropped or reused.
    internal static async Task<UniNetDbContext> CreateAsync(string kind)
    {
        var name = $"uninet_step5a_{kind}_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlConnection(Admin); await admin.OpenAsync();
        Assert.Equal("127.0.0.1", admin.Host); Assert.Equal(55433, admin.Port); Assert.Equal("postgres", admin.Database);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString;
        await using (var verify = new NpgsqlConnection(cs))
        {
            await verify.OpenAsync(); Assert.Equal(name, verify.Database);
            await using var command = new NpgsqlCommand("SELECT current_database()", verify);
            Assert.Equal(name, await command.ExecuteScalarAsync());
        }
        return new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(cs).Options);
    }

    [Step5aPostgresFact]
    public async Task EnabledRealHostAuthenticatesAndPersistsSignedAppWebhook()
    {
        await using var db = await CreateAsync("host"); await db.Database.MigrateAsync();
        var path = Path.Combine(Path.GetTempPath(), "uninet-host-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certPath = Path.Combine(path, "fixture.pfx"); await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var api = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../UniNet.API/bin/Debug/net8.0/UniNet.API.dll"));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(api); start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}"); start.Environment.Clear();
        foreach (var key in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "DOTNET_ROOT" }) if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "E2E"; start.Environment["UNINET_SKIP_LOCAL_ENV_FILE"] = "true";
        start.Environment["GitHubE2E__DisableUnrelatedWorkers"] = "true"; start.Environment["GitHubSync__Enabled"] = "false";
        start.Environment["ConnectionStrings__UniNet"] = db.Database.GetConnectionString()!; start.Environment["Jwt__Key"] = "fixture-signing-key-at-least-32-bytes-long";
        start.Environment["Jwt__Issuer"] = "fixture-issuer"; start.Environment["Jwt__Audience"] = "fixture-audience";
        start.Environment["GitHubApp__Enabled"] = "true"; start.Environment["GitHubApp__AppId"] = "42"; start.Environment["GitHubApp__ClientId"] = "fixture-app";
        start.Environment["GitHubApp__ClientSecret"] = "fixture-app-secret"; start.Environment["GitHubApp__PrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem();
        start.Environment["GitHubApp__WebhookSecret"] = "fixture-webhook-secret"; start.Environment["GitHubApp__CallbackUrl"] = "https://api.example.test/callback";
        start.Environment["GitHubApp__ReturnTargets__web"] = "https://web.example.test/complete"; start.Environment["GitHub__ClientId"] = "fixture-oauth";
        start.Environment["GitHub__ClientSecret"] = "fixture-oauth-secret"; start.Environment["GitHub__CallbackUrl"] = "https://api.example.test/oauth";
        start.Environment["GitHub__KeyRingPath"] = path; start.Environment["GitHub__KeyRingCertificatePath"] = certPath;
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(2) };
            var ready = false; var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && !process.HasExited) { try { ready = (await client.GetAsync("/health")).IsSuccessStatusCode; if (ready) break; } catch (HttpRequestException) { } catch (TaskCanceledException) { } await Task.Delay(200); }
            Assert.True(ready, "Isolated enabled App test host did not start.");
            var body = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456}}"); var id = Guid.NewGuid().ToString();
            var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("fixture-webhook-secret"), body)).ToLowerInvariant();
            async Task<HttpResponseMessage> Send(string delivery, byte[] payload, string? sig)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/github/app") { Content = new ByteArrayContent(payload) };
                message.Headers.Add("X-GitHub-Delivery", delivery); message.Headers.Add("X-GitHub-Event", "push"); if (sig is not null) message.Headers.Add("X-Hub-Signature-256", sig);
                return await client.SendAsync(message);
            }
            Assert.Equal(HttpStatusCode.Accepted, (await Send(id, body, signature)).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted, (await Send(id, body, signature)).StatusCode);
            Assert.False((await Send(Guid.NewGuid().ToString(), body, null)).IsSuccessStatusCode);
            Assert.False((await Send(Guid.NewGuid().ToString(), body, "sha256=" + new string('0', 64))).IsSuccessStatusCode);
            var changed = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":457}}");
            Assert.False((await Send(Guid.NewGuid().ToString(), changed, signature)).IsSuccessStatusCode);
            var changedSignature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("fixture-webhook-secret"), changed)).ToLowerInvariant();
            Assert.Equal(HttpStatusCode.Conflict, (await Send(id, changed, changedSignature)).StatusCode);
            var row = await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == id);
            Assert.Equal(GitHubAppWebhookDeliveryStatus.AwaitingScope, row.Status); Assert.Equal(GitHubAppWebhookRoute.RepositoryActivity, row.Route);
            Assert.Equal(1, await db.GitHubAppWebhookDeliveries.CountAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/api/projects/{Guid.NewGuid()}/git/app/repositories/link", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/github/oauth/start", null)).StatusCode);
            Account Student(string name) => new() { Email = name + "@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = name, Nickname = name } };
            var creator = Student("creator"); var manager = Student("manager"); var member = Student("member"); var outsider = Student("outsider");
            var project = new Project { Creator = creator.Profile!, Title = "HTTP fixture", ProjectField = "IT", Description = "Test", MemberTarget = 3, Status = ProjectStatus.Active };
            var now = DateTimeOffset.UtcNow;
            ProjectMember Membership(Account account) => new() { Id = Guid.NewGuid(), ProjectId = project.Id, UserId = account.Profile!.Id, Role = "Member", Status = ProjectMemberStatus.Active, JoinedAt = now, CreatedAt = now, UpdatedAt = now };
            db.AddRange(creator, manager, member, outsider, project, Membership(manager), Membership(member), new ProjectGitManagerGrant { ProjectId = project.Id, UserProfileId = manager.Profile!.Id, GrantedByProfileId = creator.Profile!.Id });
            await db.SaveChangesAsync();
            string Jwt(Account actor) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("fixture-issuer", "fixture-audience",
                new[] { new Claim(JwtRegisteredClaimNames.Sub, actor.Id.ToString()), new Claim(ClaimTypes.Role, actor.Role.ToString()), new Claim("session_version", "0") },
                expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("fixture-signing-key-at-least-32-bytes-long")), SecurityAlgorithms.HmacSha256)));
            async Task<string> Link(Account actor)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/projects/{project.Id}/git/app/repositories/link");
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Jwt(actor));
                message.Content = JsonContent.Create(new { credentialId = Guid.NewGuid(), installationId = 1, repositoryId = 1 });
                using var response = await client.SendAsync(message); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); return await response.Content.ReadAsStringAsync();
            }
            Assert.Contains("GITHUB_APP_AUTH_REQUIRED", await Link(creator));
            Assert.Contains("GITHUB_APP_AUTH_REQUIRED", await Link(manager));
            Assert.Contains("FORBIDDEN", await Link(member));
            Assert.Contains("FORBIDDEN", await Link(outsider));
            Assert.False(await db.ProjectGitRepositories.AnyAsync(x => x.ProjectId == project.Id));
            var admin = new Account { Email = "admin@example.test", Role = AccountRole.Admin, Status = AccountStatus.Active }; db.Add(admin);
            row.Status = GitHubAppWebhookDeliveryStatus.Failed; row.LastErrorCode = "FIXTURE"; await db.SaveChangesAsync();
            async Task<HttpStatusCode> Requeue(Account actor)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/github/app/deliveries/{id}/requeue") { Content = JsonContent.Create(new { reason = "fixture recovery" }) };
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Jwt(actor)); using var response = await client.SendAsync(message); return response.StatusCode;
            }
            Assert.Equal(HttpStatusCode.Forbidden, await Requeue(member));
            Assert.Equal(HttpStatusCode.NoContent, await Requeue(admin));
            Assert.Equal(HttpStatusCode.Conflict, await Requeue(admin));
            db.ChangeTracker.Clear(); Assert.Equal(1, await db.Set<GitHubAppWebhookRequeueAudit>().CountAsync(x => x.ActorAccountId == admin.Id));
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } await Task.WhenAll(stdout, stderr); Directory.Delete(path, true); }
    }

    [Step5aPostgresFact]
    public async Task ClusterProgressUsesFreshInstancesAndRejectsStaleWorkers()
    {
        await using var db = await CreateAsync("heartbeat"); await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow; var cutoff = now.AddMinutes(-15); var ttl = now.AddMinutes(-2);
        Assert.False(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, ttl, cutoff, default));
        var a = new GitHubAppWorkerHeartbeat { Id = Guid.NewGuid(), UpdatedAt = now, WorkerAt = now, ConsumerAt = now, ReconciliationAt = now };
        var b = new GitHubAppWorkerHeartbeat { Id = Guid.NewGuid(), UpdatedAt = now, WorkerAt = now, ConsumerAt = now, ReconciliationAt = now };
        db.AddRange(a, b); await db.SaveChangesAsync();
        Assert.True(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, ttl, cutoff, default));
        a.UpdatedAt = now.AddMinutes(-3); await db.SaveChangesAsync();
        Assert.True(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, ttl, cutoff, default));
        b.WorkerAt = now.AddMinutes(-16); await db.SaveChangesAsync();
        Assert.False(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, ttl, cutoff, default));
        b.UpdatedAt = now.AddMinutes(-3); await db.SaveChangesAsync();
        Assert.False(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, ttl, cutoff, default));
        var restarted = new GitHubAppWorkerHeartbeat { Id = Guid.NewGuid(), UpdatedAt = now, WorkerAt = now, ConsumerAt = now, ReconciliationAt = now };
        db.Add(restarted); await db.SaveChangesAsync();
        Assert.True(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, ttl, cutoff, default));
        // Simulate multiple long cycles that complete units of work, with no real-time wait.
        for (var step = 1; step <= 4; step++)
        {
            var future = now.AddMinutes(step * 10);
            restarted.UpdatedAt = future; restarted.WorkerAt = future; restarted.ConsumerAt = future; restarted.ReconciliationAt = future;
            await db.SaveChangesAsync();
            Assert.True(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, future.AddMinutes(-2), future.AddMinutes(-15), default));
        }
        var stalled = now.AddMinutes(56); restarted.UpdatedAt = stalled; restarted.WorkerAt = stalled; await db.SaveChangesAsync();
        Assert.False(await GitHubSharedQueueHealthCheck.HasClusterProgressAsync(db, stalled.AddMinutes(-2), stalled.AddMinutes(-15), default));
    }

    [Step5aPostgresFact]
    public async Task SharedQueueDistinguishesIdleOverdueDeliveryBackoffAndExpiredLease()
    {
        await using var db = await CreateAsync("health"); await db.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        db.Add(new GitHubAppWorkerHeartbeat { Id = Guid.NewGuid(), UpdatedAt = now, WorkerAt = now, ConsumerAt = now, ReconciliationAt = now }); await db.SaveChangesAsync();
        var services = new ServiceCollection(); services.AddSingleton(db);
        await using var container = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GitHubApp:Enabled"] = "true" }).Build();
        var health = new GitHubSharedQueueHealthCheck(container.GetRequiredService<IServiceScopeFactory>(), config, new FixedClock(now));
        async Task<HealthStatus> Status() => (await health.CheckHealthAsync(new HealthCheckContext())).Status;
        Assert.Equal(HealthStatus.Healthy, await Status());
        Assert.False(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, now.AddMinutes(-15), default));
        var delivery = new GitHubAppWebhookDelivery { DeliveryId = Guid.NewGuid().ToString(), EventName = "push", Route = GitHubAppWebhookRoute.RepositoryActivity,
            Status = GitHubAppWebhookDeliveryStatus.PendingHandler, PayloadHash = new string('a', 64), PayloadEncrypted = "fixture", ReceivedAt = now.AddMinutes(-30) };
        db.Add(delivery); await db.SaveChangesAsync();
        Assert.Equal(HealthStatus.Degraded, await Status());
        Assert.True(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, now.AddMinutes(-15), default));
        Assert.False(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, now.AddMinutes(-60), default));
        delivery.Status = GitHubAppWebhookDeliveryStatus.RetryScheduled; delivery.NextAttemptAt = now.AddMinutes(10); await db.SaveChangesAsync();
        Assert.Equal(HealthStatus.Healthy, await Status());
        Assert.False(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, now.AddMinutes(-15), default));
        delivery.Status = GitHubAppWebhookDeliveryStatus.Processing; delivery.NextAttemptAt = null; delivery.LeaseId = Guid.NewGuid(); delivery.LeaseExpiresAt = now.AddMinutes(-20); await db.SaveChangesAsync();
        Assert.True(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, now.AddMinutes(-15), default));
        delivery.LeaseExpiresAt = now.AddMinutes(-61); await db.SaveChangesAsync();
        Assert.Equal(HealthStatus.Unhealthy, await Status());
    }

    [Step5aPostgresFact]
    public async Task SharedKeyRingAllowsAnotherInstanceToClaimAndProcessVerifiedDeliveryOnce()
    {
        await using var db = await CreateAsync("delivery"); await db.Database.MigrateAsync();
        var keyPath = Path.Combine(Path.GetTempPath(), "uninet-step5b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(keyPath);
        try
        {
            GitHubTokenProtector Protector() => new(DataProtectionProvider.Create(new DirectoryInfo(keyPath), x => x.SetApplicationName("UniNet.GitHubIntegration")));
            var protectorA = Protector(); var protectorB = Protector();
            var app = Options.Create(new GitHubAppOptions { Enabled = true, AppId = 42, WebhookSecret = "fixture-signing-secret" });
            var sync = Options.Create(new GitSyncOptions { Enabled = true });
            var body = Encoding.UTF8.GetBytes("{\"action\":\"deleted\",\"installation\":{\"id\":123,\"app_id\":42,\"account\":{\"id\":9,\"login\":\"fixture\",\"type\":\"User\"}}}");
            var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("fixture-signing-secret"), body)).ToLowerInvariant();
            var deliveryId = Guid.NewGuid().ToString();
            await new GitHubAppWebhookService(db, protectorA, app, Options.Create(new GitHubWebhookOptions())).HandleAsync(deliveryId, "installation", signature, body, default);
            await using var a = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options);
            await using var b = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options);
            var consumerA = new GitHubAppWebhookConsumer(a, protectorA, null!, null!, app, sync);
            var consumerB = new GitHubAppWebhookConsumer(b, protectorB, null!, null!, app, sync);
            var claims = await Task.WhenAll(consumerA.ClaimNextAsync(default), consumerB.ClaimNextAsync(default));
            var claimed = Assert.Single(claims, x => x is not null)!;
            await consumerB.ProcessClaimAsync(claimed, default);
            db.ChangeTracker.Clear();
            Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == deliveryId)).Status);
        }
        finally { Directory.Delete(keyPath, recursive: true); }
    }

    [Step5aPostgresFact]
    public async Task DisabledAppJobRemainsPendingWhileOAuthJobCanBeClaimedAndAppResumes()
    {
        await using var db = await CreateAsync("switch"); await db.Database.MigrateAsync();
        var owner = new Account { Email = "switch@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Switch", Nickname = "Switch" } };
        var project = new Project { Creator = owner.Profile!, Title = "Switch", ProjectField = "IT", Description = "Disposable", MemberTarget = 1, Status = ProjectStatus.Active };
        var oauth = new GitHubOAuthConnection { AccountId = owner.Id, ProviderAccountId = "123", Login = "test", AccessTokenEncrypted = "fixture" };
        var repository = new GitHubRepository { ProviderRepositoryId = 444, ProviderOwnerId = 123, OwnerLogin = "test", Name = "repo", FullName = "test/repo", HtmlUrl = "https://example.test/repo" };
        var installation = new GitHubAppInstallation { ProviderInstallationId = 123, OwnerAccountId = 123, OwnerLogin = "test", OwnerType = "User" };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = 444, VerifiedAt = DateTimeOffset.UtcNow };
        var appLink = new ProjectGitRepository { ProjectId = project.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var otherProject = new Project { Creator = owner.Profile, Title = "OAuth", ProjectField = "IT", Description = "Disposable", MemberTarget = 1, Status = ProjectStatus.Active };
        var oauthLink = new ProjectGitRepository { ProjectId = otherProject.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, OAuthConnectionId = oauth.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        var appJob = new GitSyncJob { ProjectGitRepositoryId = appLink.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = GitSyncType.Initial };
        var oauthJob = new GitSyncJob { ProjectGitRepositoryId = oauthLink.Id, GitHubRepositoryId = repository.Id, OAuthConnectionId = oauth.Id, SyncType = GitSyncType.Initial };
        db.AddRange(owner, project, otherProject, oauth, repository, installation, grant, appLink, oauthLink, appJob, oauthJob); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var healthNow = DateTimeOffset.UtcNow;
        Assert.False(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, healthNow.AddMinutes(-15), default));
        await db.GitSyncJobs.Where(x => x.Id == appJob.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.CreatedAt, healthNow.AddMinutes(-30)));
        Assert.True(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, healthNow.AddMinutes(-15), default));
        Assert.False(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, healthNow.AddMinutes(-60), default));
        await db.GitSyncJobs.Where(x => x.Id == appJob.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.CreatedAt, healthNow));
        var sync = Options.Create(new GitSyncOptions { Enabled = true });
        var disabled = new GitSyncJobService(db, sync, Options.Create(new GitHubAppOptions { Enabled = false }));
        var claimedOAuth = await disabled.ClaimNextAsync(TimeSpan.FromMinutes(1), default);
        Assert.Equal(oauthJob.Id, claimedOAuth!.Id);
        Assert.Null(await disabled.ClaimNextAsync(TimeSpan.FromMinutes(1), default));
        Assert.Equal(GitSyncJobStatus.Pending, (await db.GitSyncJobs.AsNoTracking().SingleAsync(x => x.Id == appJob.Id)).Status);
        var enabled = new GitSyncJobService(db, sync, Options.Create(new GitHubAppOptions { Enabled = true }));
        Assert.Equal(appJob.Id, (await enabled.ClaimNextAsync(TimeSpan.FromMinutes(1), default))!.Id);
        await db.GitSyncJobs.Where(x => x.Id == appJob.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.LeaseExpiresAt, healthNow.AddMinutes(-20)));
        Assert.True(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, healthNow.AddMinutes(-15), default));
        await db.GitSyncJobs.Where(x => x.Id == appJob.Id).ExecuteUpdateAsync(x => x.SetProperty(j => j.LeaseExpiresAt, healthNow.AddMinutes(10)));
        await db.Set<GitHubAppInstallation>().Where(x => x.Id == installation.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.NextReconciliationAt, healthNow.AddMinutes(-20)));
        Assert.True(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, healthNow.AddMinutes(-15), default));
        await db.Set<GitHubAppInstallation>().Where(x => x.Id == installation.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.NextReconciliationAt, healthNow.AddMinutes(10)));
        Assert.False(await GitHubSharedQueueHealthCheck.HasOverdueAsync(db, healthNow.AddMinutes(-15), default));
    }

    [Step5aPostgresFact]
    public async Task FreshDatabaseMigratesEntireChainAndCanMigrateAgain()
    {
        await using var db = await CreateAsync("fresh");
        await db.Database.MigrateAsync();
        Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(11, migrations.Count(x => x.CompareTo("20261106062750_AddGitHubAppInstallationPersistence") >= 0));
    }

    [Step5aPostgresFact]
    public async Task IntermediateAuthorizationFlowsAreQuarantinedAndUpgradeAndRollbackAreSafe()
    {
        await using var db = await CreateAsync("intermediate");
        await db.GetService<IMigrator>().MigrateAsync("20261106062750_AddGitHubAppInstallationPersistence");
        var owner = new Account { Email = "intermediate@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Legacy", Nickname = "Legacy" } };
        var project = new Project { Creator = owner.Profile!, Title = "Legacy", ProjectField = "IT", Description = "Migration fixture", MemberTarget = 1, Status = ProjectStatus.Active };
        db.AddRange(owner, project); await db.SaveChangesAsync();
        var flowId = Guid.NewGuid(); var secondId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var state = "step5a-legacy-state"; var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"GitHubAppAuthorizationFlows\" (\"Id\",\"AccountId\",\"ProjectId\",\"StateHash\",\"ReturnTarget\",\"Status\",\"CreatedAt\",\"ExpiresAt\",\"Version\") VALUES ({flowId}, {owner.Id}, {project.Id}, {hash}, 'web', 0, {now}, {now.AddMinutes(10)}, {Guid.NewGuid()}), ({secondId}, {owner.Id}, {project.Id}, {'b'.ToString()}, 'web', 1, {now}, {now.AddMinutes(10)}, {Guid.NewGuid()})");
        var original = await db.Database.SqlQueryRaw<string>("SELECT jsonb_agg(to_jsonb(f) ORDER BY \"Id\")::text AS \"Value\" FROM \"GitHubAppAuthorizationFlows\" f").SingleAsync();
        // Deliberately obstruct archive creation, then remove the empty test-owned
        // object to verify transactional rollback and a subsequent retry.
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"GitHubAppAuthorizationFlowQuarantine\" (\"Fixture\" integer)");
        var failedUpgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261106064420_AddGitHubAppUserAuthorization"));
        Assert.Equal(PostgresErrorCodes.DuplicateTable, failedUpgrade.SqlState);
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"GitHubAppAuthorizationFlows\" WHERE \"Id\" IN ({flowId}, {secondId})").SingleAsync());
        Assert.DoesNotContain("20261106064420_AddGitHubAppUserAuthorization", await db.Database.GetAppliedMigrationsAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"GitHubAppAuthorizationFlowQuarantine\"");
        await db.Database.MigrateAsync();
        Assert.Equal(original, await db.Database.SqlQueryRaw<string>("SELECT jsonb_agg(to_jsonb(f) ORDER BY \"Id\")::text AS \"Value\" FROM \"GitHubAppAuthorizationFlowQuarantine\" f").SingleAsync());
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"GitHubAppAuthorizationFlowQuarantine\" WHERE \"Id\" IN ({flowId}, {secondId}) AND \"AccountId\" = {owner.Id} AND \"ProjectId\" = {project.Id}").SingleAsync());
        Assert.Equal(0, await db.Set<GitHubAppAuthorizationFlow>().CountAsync());
        var service = new GitHubAppAuthorizationService(db, null!, null!, Options.Create(new GitHubAppOptions { Enabled = true, ClientId = "fixture", ClientSecret = "fixture", CallbackUrl = "https://example.test/callback", ReturnTargets = new() { ["web"] = "https://example.test/complete" } }));
        var rejected = await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.CallbackAsync(state, "fixture", null, default));
        Assert.Equal("INVALID_APP_STATE", rejected.Code);
        var oauth = new GitHubOAuthConnection { AccountId = owner.Id, ProviderAccountId = "123", Login = "fixture", AccessTokenEncrypted = "fixture-encrypted" };
        var newFlow = new GitHubAppAuthorizationFlow { AccountId = owner.Id, ProjectId = project.Id, OAuthConnectionId = oauth.Id, StateHash = "new-flow", ReturnTarget = "web", ExpiresAt = now.AddMinutes(10) };
        db.AddRange(oauth, newFlow); await db.SaveChangesAsync();
        var fkError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitHubAppAuthorizationFlows\" SET \"OAuthConnectionId\" = {Guid.NewGuid()} WHERE \"Id\" = {newFlow.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, fkError.SqlState);
        var nullError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitHubAppAuthorizationFlows\" SET \"OAuthConnectionId\" = NULL WHERE \"Id\" = {newFlow.Id}"));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, nullError.SqlState);
        db.Remove(newFlow); await db.SaveChangesAsync();
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await db.GetService<IMigrator>().MigrateAsync("20261106062750_AddGitHubAppInstallationPersistence");
        Assert.Equal(original, await db.Database.SqlQueryRaw<string>("SELECT jsonb_agg(to_jsonb(f) ORDER BY \"Id\")::text AS \"Value\" FROM \"GitHubAppAuthorizationFlows\" f").SingleAsync());
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"GitHubAppAuthorizationFlows\" WHERE \"Id\" IN ({flowId}, {secondId}) AND \"AccountId\" = {owner.Id} AND \"ProjectId\" = {project.Id}").SingleAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"GitHubAppAuthorizationFlowQuarantine\" WHERE \"Id\" IN ({flowId}, {secondId})").SingleAsync());
    }

    [Step5aPostgresFact]
    public async Task LegacyOAuthHistorySurvivesUpgradeAndAppConstraintsAreEnforced()
    {
        await using var db = await CreateAsync("legacy");
        await db.GetService<IMigrator>().MigrateAsync(BeforeApp);
        var owner = new Account { Email = "step5a@example.test", Role = AccountRole.Student, Status = AccountStatus.Active, Profile = new UserProfile { FullName = "Legacy", Nickname = "Legacy" } };
        var project = new Project { Creator = owner.Profile!, Title = "Legacy", ProjectField = "IT", Description = "Migration fixture", MemberTarget = 1, Status = ProjectStatus.Active };
        var oauth = new GitHubOAuthConnection { AccountId = owner.Id, ProviderAccountId = "42", Login = "legacy", AccessTokenEncrypted = "fixture-encrypted", GrantedScopes = "repo" };
        var repository = new GitHubRepository { ProviderRepositoryId = 12345, ProviderOwnerId = 42, OwnerLogin = "legacy", Name = "repo", FullName = "legacy/repo", HtmlUrl = "https://example.test/repo", IsPrivate = true };
        db.AddRange(owner, project, oauth, repository); await db.SaveChangesAsync();
        var linkId = Guid.NewGuid(); var jobId = Guid.NewGuid(); var objectId = Guid.NewGuid(); var activityId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid(); var visibilityId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var json = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO "ProjectGitRepositories" ("Id","ProjectId","GitHubRepositoryId","OAuthConnectionId","LinkedByProfileId","AccessStatus","SyncPaused","LinkedAt","CreatedAt","UpdatedAt") VALUES ({linkId},{project.Id},{repository.Id},{oauth.Id},{owner.Profile!.Id},0,false,{now},{now},{now});
INSERT INTO "GitSyncJobs" ("Id","ProjectGitRepositoryId","GitHubRepositoryId","OAuthConnectionId","SyncType","Status","RetryCount","CreatedAt","UpdatedAt") VALUES ({jobId},{linkId},{repository.Id},{oauth.Id},0,0,0,{now},{now});
INSERT INTO "GitSyncCheckpoints" ("Id","SyncJobId","Category","ScopeKey","Cursor","Status","ItemsProcessed","UpdatedAt") VALUES ({checkpointId},{jobId},'commits','main','cursor-fixture',0,7,{now});
INSERT INTO "GitHubProviderObjects" ("Id","GitHubRepositoryId","Category","ExternalObjectId","FirstObservedAt","LastObservedAt","PayloadJson","RetrievalMetadataJson") VALUES ({objectId},{repository.Id},'commits','sha-fixture',{now},{now},{json}::jsonb,{json}::jsonb);
INSERT INTO "GitActivities" ("Id","GitHubProviderObjectId","ActivityType","OccurredAt","MetadataJson") VALUES ({activityId},{objectId},'commit.created',{now},{json}::jsonb);
INSERT INTO "ProjectGitActivityVisibilities" ("Id","ProjectGitRepositoryId","GitActivityId","ObservedAt") VALUES ({visibilityId},{linkId},{activityId},{now});
""");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var link = await db.Set<ProjectGitRepository>().SingleAsync(x => x.Id == linkId);
        var job = await db.Set<GitSyncJob>().SingleAsync(x => x.Id == jobId);
        Assert.Equal(GitRepositoryCredentialSource.OAuth, link.CredentialSource); Assert.Equal(oauth.Id, link.OAuthConnectionId); Assert.Null(link.GitHubAppRepositoryGrantId);
        Assert.Equal(GitRepositoryCredentialSource.OAuth, job.CredentialSource); Assert.Equal(oauth.Id, job.OAuthConnectionId); Assert.Null(job.GitHubAppRepositoryGrantId);
        Assert.Equal(1, await db.Set<GitActivity>().CountAsync(x => x.Id == activityId));
        Assert.Equal(1, await db.Set<GitSyncCheckpoint>().CountAsync(x => x.Id == checkpointId && x.Cursor == "cursor-fixture" && x.ItemsProcessed == 7));
        Assert.Equal(1, await db.Set<ProjectGitActivityVisibility>().CountAsync(x => x.Id == visibilityId));
        Assert.Equal(1, await db.Set<ProjectGitRepository>().CountAsync(x => x.ProjectId == project.Id && x.GitHubRepositoryId == repository.Id));
        await db.Database.MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        // Check constraints are tested against PostgreSQL itself, not the EF in-memory model.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectGitRepositories\" SET \"OAuthConnectionId\" = NULL WHERE \"Id\" = {linkId}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);

        var appProject = new Project { CreatorId = owner.Profile!.Id, Title = "App", ProjectField = "IT", Description = "Migration fixture", MemberTarget = 1, Status = ProjectStatus.Active };
        var installation = new GitHubAppInstallation { ProviderInstallationId = 987, OwnerAccountId = 42, OwnerLogin = "legacy", OwnerType = "User", NextReconciliationAt = now.AddHours(1), ReconciliationFailures = 2 };
        var grant = new GitHubAppRepositoryGrant { InstallationId = installation.Id, ProviderRepositoryId = repository.ProviderRepositoryId, VerifiedAt = now };
        var appLink = new ProjectGitRepository { ProjectId = appProject.Id, GitHubRepositoryId = repository.Id, LinkedByProfileId = owner.Profile!.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
        db.AddRange(appProject, installation, grant, appLink);
        foreach (var type in new[] { GitSyncType.Initial, GitSyncType.Manual, GitSyncType.Scheduled, GitSyncType.Webhook })
            db.Add(new GitSyncJob { ProjectGitRepositoryId = appLink.Id, GitHubRepositoryId = repository.Id, CredentialSource = GitRepositoryCredentialSource.GitHubApp, GitHubAppRepositoryGrantId = grant.Id, SyncType = type, Status = GitSyncJobStatus.Completed, CompletedAt = now });
        var delivery = new GitHubAppWebhookDelivery { DeliveryId = "step5a-delivery", EventName = "push", PayloadHash = new string('a', 64), PayloadEncrypted = "fixture-encrypted", Status = GitHubAppWebhookDeliveryStatus.RetryScheduled, AttemptCount = 1, NextAttemptAt = now.AddMinutes(1) };
        db.Add(delivery); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(4, await db.Set<GitSyncJob>().CountAsync(x => x.ProjectGitRepositoryId == appLink.Id));
        Assert.Equal(2, (await db.Set<GitHubAppInstallation>().SingleAsync(x => x.Id == installation.Id)).ReconciliationFailures);
        Assert.Equal(1, (await db.Set<GitHubAppWebhookDelivery>().SingleAsync(x => x.Id == delivery.Id)).AttemptCount);
        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectGitRepositories\" SET \"OAuthConnectionId\" = {oauth.Id} WHERE \"Id\" = {appLink.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectGitRepositories\" SET \"GitHubAppRepositoryGrantId\" = {Guid.NewGuid()} WHERE \"Id\" = {appLink.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"GitHubAppWebhookDeliveries\" (\"Id\",\"DeliveryId\",\"EventName\",\"Route\",\"Status\",\"PayloadHash\",\"PayloadEncrypted\",\"ReceivedAt\",\"AttemptCount\") VALUES ({Guid.NewGuid()}, {delivery.DeliveryId}, 'push', 2, 0, {delivery.PayloadHash}, 'fixture', {now}, 0)"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"GitHubAppRepositoryGrants\" (\"Id\",\"InstallationId\",\"ProviderRepositoryId\",\"GrantedAt\",\"VerifiedAt\") VALUES ({Guid.NewGuid()}, {installation.Id}, {repository.ProviderRepositoryId}, {now}, {now})"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"GitHubAppInstallations\" WHERE \"Id\" = {installation.Id}"));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitSyncJobs\" SET \"OAuthConnectionId\" = {oauth.Id} WHERE \"ProjectGitRepositoryId\" = {appLink.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"GitHubAppWebhookDeliveries\" SET \"LeaseId\" = {Guid.NewGuid()} WHERE \"Id\" = {delivery.Id}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }
}
