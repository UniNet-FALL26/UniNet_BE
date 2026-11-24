using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.API.Services;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppWebhookRecoveryPostgresTests
{
    private sealed class AbortBeforeAuditSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("injected transaction interruption");
    }

    [Step5aPostgresFact]
    public async Task InterruptedRequeueRollsBackAndCommittedRetrySurvivesScopeRestart()
    {
        await using var db = await GitHubAppMigrationUpgradePostgresTests.CreateAsync("reqcrash"); await db.Database.MigrateAsync();
        var admin = new Account { Email = "operator@example.test", Role = AccountRole.Admin, Status = AccountStatus.Active };
        var delivery = new GitHubAppWebhookDelivery { DeliveryId = Guid.NewGuid().ToString(), EventName = "push", Route = GitHubAppWebhookRoute.RepositoryActivity, Status = GitHubAppWebhookDeliveryStatus.Failed, PayloadHash = new string('a', 64), PayloadEncrypted = "fixture" };
        db.AddRange(admin, delivery); await db.SaveChangesAsync();
        var options = Options.Create(new GitHubAppOptions { Enabled = true });
        await using (var interrupted = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).AddInterceptors(new AbortBeforeAuditSave()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new GitHubAppWebhookRecoveryService(interrupted, options, NullLogger<GitHubAppWebhookRecoveryService>.Instance).RequeueAsync(admin.Id, delivery.DeliveryId, "fixture", default));
        db.ChangeTracker.Clear(); Assert.Equal(GitHubAppWebhookDeliveryStatus.Failed, (await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.Id == delivery.Id)).Status);
        Assert.Equal(0, await db.Set<GitHubAppWebhookRequeueAudit>().CountAsync());
        await using (var retry = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options))
            await new GitHubAppWebhookRecoveryService(retry, options, NullLogger<GitHubAppWebhookRecoveryService>.Instance).RequeueAsync(admin.Id, delivery.DeliveryId, "fixture", default);
        await using var restarted = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options);
        Assert.Equal(GitHubAppWebhookDeliveryStatus.PendingHandler, (await restarted.GitHubAppWebhookDeliveries.SingleAsync(x => x.Id == delivery.Id)).Status);
        Assert.Equal(1, await restarted.Set<GitHubAppWebhookRequeueAudit>().CountAsync());
        var duplicate = await Assert.ThrowsAsync<GitHubIntegrationException>(() => new GitHubAppWebhookRecoveryService(restarted, options, NullLogger<GitHubAppWebhookRecoveryService>.Instance).RequeueAsync(admin.Id, delivery.DeliveryId, "fixture", default));
        Assert.Equal(409, duplicate.Status);
    }
    [Step5aPostgresFact]
    public async Task UnsupportedDeliveryAndOperatorLimitFailClosed()
    {
        await using var db = await GitHubAppMigrationUpgradePostgresTests.CreateAsync("reqlimit"); await db.Database.MigrateAsync();
        var admin = new Account { Email = "operator@example.test", Role = AccountRole.Admin, Status = AccountStatus.Active }; db.Add(admin);
        var id = Guid.NewGuid().ToString(); var unsupported = new GitHubAppWebhookDelivery { DeliveryId = id, EventName = "future", Route = GitHubAppWebhookRoute.Unknown, Status = GitHubAppWebhookDeliveryStatus.Unsupported, PayloadHash = new string('a', 64), PayloadEncrypted = "fixture" };
        db.Add(unsupported); await db.SaveChangesAsync();
        var enabled = Options.Create(new GitHubAppOptions { Enabled = true });
        var service = new GitHubAppWebhookRecoveryService(db, enabled, NullLogger<GitHubAppWebhookRecoveryService>.Instance);
        Assert.Equal(409, (await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequeueAsync(admin.Id, id, "fixture", default))).Status);
        var id2 = Guid.NewGuid().ToString(); var failed = new GitHubAppWebhookDelivery { DeliveryId = id2, EventName = "push", Route = GitHubAppWebhookRoute.RepositoryActivity, Status = GitHubAppWebhookDeliveryStatus.Failed, PayloadHash = new string('b', 64), PayloadEncrypted = "fixture" };
        db.Add(failed); await db.SaveChangesAsync();
        for (var index = 0; index < 3; index++)
        {
            await service.RequeueAsync(admin.Id, id2, "fixture", default);
            db.ChangeTracker.Clear(); await db.GitHubAppWebhookDeliveries.Where(x => x.Id == failed.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.Status, GitHubAppWebhookDeliveryStatus.Failed));
        }
        var limit = await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequeueAsync(admin.Id, id2, "fixture", default)); Assert.Equal("GITHUB_APP_REQUEUE_LIMIT", limit.Code);
        Assert.Equal(3, await db.Set<GitHubAppWebhookRequeueAudit>().CountAsync(x => x.DeliveryId == failed.Id));
        var stale = new GitHubAppWebhookDelivery { DeliveryId = Guid.NewGuid().ToString(), EventName = "push", Route = GitHubAppWebhookRoute.RepositoryActivity, Status = GitHubAppWebhookDeliveryStatus.Failed, PayloadHash = new string('c', 64), PayloadEncrypted = "fixture" };
        db.Add(stale); await db.SaveChangesAsync();
        await using (var other = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options))
            await other.GitHubAppWebhookDeliveries.Where(x => x.Id == stale.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.Status, GitHubAppWebhookDeliveryStatus.Processed).SetProperty(d => d.ProcessedAt, DateTimeOffset.UtcNow));
        Assert.Equal("GITHUB_APP_REQUEUE_NOT_ALLOWED", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequeueAsync(admin.Id, stale.DeliveryId, "fixture", default))).Code);
        var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261109235311_AddGitHubAppWorkerHeartbeats"));
        Assert.Equal("P0001", downgrade.SqlState);
        Assert.Equal(3, await db.Set<GitHubAppWebhookRequeueAudit>().CountAsync(x => x.DeliveryId == failed.Id));
    }

    [Step5aPostgresFact]
    public async Task ConcurrentAdminsRequeueOnceThenConsumerProcessesOriginalVerifiedDelivery()
    {
        await using var db = await GitHubAppMigrationUpgradePostgresTests.CreateAsync("requeue"); await db.Database.MigrateAsync();
        var admin = new Account { Email = "operator@example.test", Role = AccountRole.Admin, Status = AccountStatus.Active };
        var student = new Account { Email = "student@example.test", Role = AccountRole.Student, Status = AccountStatus.Active };
        db.AddRange(admin, student); await db.SaveChangesAsync();
        var app = Options.Create(new GitHubAppOptions { Enabled = true, AppId = 42, WebhookSecret = "fixture-secret" });
        var sync = Options.Create(new GitSyncOptions { Enabled = true });
        var protector = new GitHubTokenProtector(new EphemeralDataProtectionProvider());
        var payload = Encoding.UTF8.GetBytes("{\"action\":\"deleted\",\"installation\":{\"id\":123,\"app_id\":42,\"account\":{\"id\":9,\"login\":\"fixture\",\"type\":\"User\"}}}");
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("fixture-secret"), payload)).ToLowerInvariant();
        var id = Guid.NewGuid().ToString();
        await new GitHubAppWebhookService(db, protector, app, Options.Create(new GitHubWebhookOptions())).HandleAsync(id, "installation", signature, payload, default);
        var delivery = await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == id);
        var originalPayload = delivery.PayloadEncrypted; delivery.Status = GitHubAppWebhookDeliveryStatus.Failed; delivery.AttemptCount = 8; delivery.LastErrorCode = "FIXTURE_FAILURE"; await db.SaveChangesAsync();
        var service = new GitHubAppWebhookRecoveryService(db, app, NullLogger<GitHubAppWebhookRecoveryService>.Instance);
        var disabledService = new GitHubAppWebhookRecoveryService(db, Options.Create(new GitHubAppOptions { Enabled = false }), NullLogger<GitHubAppWebhookRecoveryService>.Instance);
        Assert.Equal("GITHUB_APP_DISABLED", (await Assert.ThrowsAsync<GitHubIntegrationException>(() => disabledService.RequeueAsync(admin.Id, id, "fixture", default))).Code);
        var forbidden = await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequeueAsync(student.Id, id, "fixture", default)); Assert.Equal(403, forbidden.Status);
        async Task<bool> Attempt()
        {
            await using var context = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(db.Database.GetConnectionString()).Options);
            try { await new GitHubAppWebhookRecoveryService(context, app, NullLogger<GitHubAppWebhookRecoveryService>.Instance).RequeueAsync(admin.Id, id, "provider recovered", default); return true; }
            catch (GitHubIntegrationException ex) when (ex.Status == 409) { return false; }
        }
        var attempts = await Task.WhenAll(Attempt(), Attempt()); Assert.Single(attempts, x => x);
        db.ChangeTracker.Clear(); delivery = await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == id);
        Assert.Equal(originalPayload, delivery.PayloadEncrypted); Assert.Equal(GitHubAppWebhookDeliveryStatus.PendingHandler, delivery.Status); Assert.Equal(0, delivery.AttemptCount);
        var audit = Assert.Single(await db.Set<GitHubAppWebhookRequeueAudit>().ToListAsync()); Assert.Equal(admin.Id, audit.ActorAccountId); Assert.Equal("FIXTURE_FAILURE", audit.PreviousErrorCode);
        var consumer = new GitHubAppWebhookConsumer(db, protector, null!, null!, app, sync);
        var claimTask = consumer.ClaimNextAsync(default); var requeueRace = Attempt();
        var claimed = await claimTask; Assert.False(await requeueRace);
        // SKIP LOCKED may temporarily return no claim while requeue holds the row lock.
        claimed ??= await consumer.ClaimNextAsync(default);
        Assert.NotNull(claimed);
        var leased = await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequeueAsync(admin.Id, id, "fixture", default)); Assert.Equal(409, leased.Status);
        db.ChangeTracker.Clear();
        await db.GitHubAppWebhookDeliveries.Where(x => x.Id == claimed!.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.LeaseExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var reclaimed = await consumer.ClaimNextAsync(default); Assert.NotNull(reclaimed); Assert.Equal(claimed!.Id, reclaimed!.Id);
        Assert.NotEqual(claimed.LeaseId, reclaimed.LeaseId);
        await consumer.ProcessClaimAsync(reclaimed, default); db.ChangeTracker.Clear();
        Assert.Equal(GitHubAppWebhookDeliveryStatus.Processed, (await db.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == id)).Status);
        Assert.Equal(0, await db.GitSyncJobs.CountAsync()); Assert.Equal(0, await db.GitActivities.CountAsync());
        var processed = await Assert.ThrowsAsync<GitHubIntegrationException>(() => service.RequeueAsync(admin.Id, id, "fixture", default)); Assert.Equal(409, processed.Status);
    }
}
