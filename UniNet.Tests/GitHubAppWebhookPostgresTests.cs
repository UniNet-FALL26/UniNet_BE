using System.Security.Cryptography;
using System.Text;
using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.API.Services;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class Step4aPostgresFactAttribute : FactAttribute
{
    public Step4aPostgresFactAttribute()
    { if (Environment.GetEnvironmentVariable("UNINET_STEP4A_DISPOSABLE") != "1") Skip = "Requires explicitly enabled disposable PostgreSQL."; }
}

public sealed class GitHubAppWebhookPostgresTests
{
    private static string Database => GitHubAppDisposableDatabase.Name("uninet_step4a_step4c_regression");
    private static string Connection => "Host=127.0.0.1;Port=55433;Database=" + Database + ";Username=postgres;Timeout=5";
    private static UniNetDbContext Open(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(Connection).AddInterceptors(interceptors).Options);
    private const string Secret = "app-secret-fixture";
    private static string Sign(byte[] payload) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), payload)).ToLowerInvariant();

    private sealed class SimultaneousInserts : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"GitHubAppWebhookDeliveries\"", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals) == 12) all.TrySetResult();
                await all.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            }
            return result;
        }
    }

    [Step4aPostgresFact]
    public async Task ConcurrentRedeliveriesStageExactlyOnceAndContentConflictsAreRejected()
    {
        await using (var check = new NpgsqlConnection(Connection))
        { await check.OpenAsync(); Assert.Equal(Database, check.Database); Assert.Equal("127.0.0.1", check.Host); Assert.Equal(55433, check.Port); }
        await using (var setup = Open()) await setup.Database.EnsureCreatedAsync();
        var deliveryId = Guid.NewGuid().ToString();
        var payload = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456}}");
        var protector = new GitHubTokenProtector(new EphemeralDataProtectionProvider());
        var barrier = new SimultaneousInserts();
        async Task<GitHubAppWebhookResult> Deliver(string eventName, byte[] body)
        {
            await using var db = Open(barrier);
            return await new GitHubAppWebhookService(db, protector, Options.Create(new GitHubAppOptions { Enabled = true, WebhookSecret = Secret }),
                Options.Create(new GitHubWebhookOptions { Secret = "legacy-fixture" })).HandleAsync(deliveryId, eventName, Sign(body), body, default);
        }
        var deliveries = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Deliver("push", payload)));
        Assert.Single(deliveries, x => !x.Duplicate); Assert.Equal(11, deliveries.Count(x => x.Duplicate));
        Assert.All(deliveries, x => Assert.Equal("AwaitingScope", x.Status));
        await using var verify = Open();
        var row = await verify.GitHubAppWebhookDeliveries.SingleAsync(x => x.DeliveryId == deliveryId);
        Assert.Equal(payload, Convert.FromBase64String(protector.Unprotect(row.PayloadEncrypted)));
        Assert.Equal(0, row.AttemptCount);
        Assert.False(await verify.GitSyncJobs.AnyAsync(x => x.IdempotencyKey == deliveryId));
        var changed = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":789}}");
        var ex = await Assert.ThrowsAsync<GitHubIntegrationException>(() => Deliver("push", changed));
        Assert.Equal("GITHUB_WEBHOOK_DELIVERY_CONFLICT", ex.Code);
    }

    [Step4aPostgresFact]
    public async Task ConcurrentConflictingBodiesNeverShareAStoredDelivery()
    {
        await using (var check = new NpgsqlConnection(Connection))
        { await check.OpenAsync(); Assert.Equal(Database, check.Database); Assert.Equal("127.0.0.1", check.Host); Assert.Equal(55433, check.Port); }
        await using (var setup = Open()) await setup.Database.EnsureCreatedAsync();
        var deliveryId = Guid.NewGuid().ToString();
        var first = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":456}}");
        var second = Encoding.UTF8.GetBytes("{\"installation\":{\"id\":123},\"repository\":{\"id\":789}}");
        var protector = new GitHubTokenProtector(new EphemeralDataProtectionProvider()); var barrier = new SimultaneousInserts();
        async Task<string> Deliver(byte[] body)
        {
            await using var db = Open(barrier);
            try
            {
                var result = await new GitHubAppWebhookService(db, protector, Options.Create(new GitHubAppOptions { Enabled = true, WebhookSecret = Secret }),
                    Options.Create(new GitHubWebhookOptions { Secret = "legacy-fixture" })).HandleAsync(deliveryId, "push", Sign(body), body, default);
                return result.Duplicate ? "duplicate" : "created";
            }
            catch (GitHubIntegrationException ex) { return ex.Code; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Deliver(i < 6 ? first : second)));
        Assert.Single(results, x => x == "created"); Assert.Equal(5, results.Count(x => x == "duplicate"));
        Assert.Equal(6, results.Count(x => x == "GITHUB_WEBHOOK_DELIVERY_CONFLICT"));
        await using var verify = Open();
        Assert.Equal(1, await verify.GitHubAppWebhookDeliveries.CountAsync(x => x.DeliveryId == deliveryId));
    }
}
