using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class FriendshipPostgresTests
{
    [IsolatedPostgresFact]
    public async Task ConcurrentSameAndReverseSendsCreateOnePairAndOneNotification()
    {
        await using var store = await Store.Create();
        foreach (var reverse in new[] { false, true })
        {
            await using var a = store.Open(); await using var b = store.Open();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<int> Send(UniNetDbContext db, Guid actor, Guid receiver) {
                await gate.Task;
                try { await new FriendshipService(db).SendAsync(actor, receiver, default); return 200; }
                catch (ProjectException ex) { return ex.Status; }
            }
            var first = Send(a, store.A, store.B); var second = Send(b, reverse ? store.B : store.A, reverse ? store.A : store.B);
            gate.SetResult(); var results = await Task.WhenAll(first, second);
            Assert.Equal(new[] { 200, 409 }, results.Order().ToArray());
            await using var read = store.Open();
            Assert.Equal(1, await read.Friendships.CountAsync());
            Assert.Equal(1, await read.Set<VerificationNotification>().CountAsync());
            await read.Set<NotificationDelivery>().ExecuteDeleteAsync(); await read.Set<VerificationNotification>().ExecuteDeleteAsync(); await read.Friendships.ExecuteDeleteAsync();
        }
    }

    [IsolatedPostgresFact]
    public async Task ConcurrentAcceptAndCancelHonorVersionAndCannotBothSucceed()
    {
        await using var store = await Store.Create();
        await using var setup = store.Open();
        var sent = await new FriendshipService(setup).SendAsync(store.A, store.B, default);
        await using var a = store.Open(); await using var b = store.Open();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Decide(UniNetDbContext db, Guid actor, string action) {
            await gate.Task;
            try { await new FriendshipService(db).DecideAsync(actor, sent.FriendshipId!.Value, action, 1, default); return 200; }
            catch (ProjectException ex) { return ex.Status; }
        }
        var accept = Decide(b, store.B, "accept"); var cancel = Decide(a, store.A, "cancel"); gate.SetResult();
        Assert.Equal(new[] { 200, 409 }, (await Task.WhenAll(accept, cancel)).Order().ToArray());
        await using var read = store.Open(); var row = await read.Friendships.SingleAsync();
        Assert.Equal(2, row.Version);
        Assert.Equal(row.Status == FriendshipStatus.Accepted ? 2 : 1, await read.Set<VerificationNotification>().CountAsync());
    }

    private sealed class FailNotifications : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default) {
            if (command.CommandText.Contains("INSERT INTO \"Notifications\"")) throw new InvalidOperationException("notification persistence failure");
            return base.ReaderExecutingAsync(command, eventData, result, ct);
        }
    }
    [IsolatedPostgresFact]
    public async Task NotificationFailureRollsBackFriendshipAndOutbox()
    {
        await using var store = await Store.Create();
        await using var write = store.Open(new FailNotifications());
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => new FriendshipService(write).SendAsync(store.A, store.B, default));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        await using var read = store.Open(); Assert.Empty(await read.Friendships.ToListAsync());
        Assert.Empty(await read.Set<VerificationNotification>().ToListAsync()); Assert.Empty(await read.Set<NotificationDelivery>().ToListAsync());
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        private readonly string schema = "friendship_test_" + Guid.NewGuid().ToString("N");
        public Guid A, B;
        public UniNetDbContext Open(DbCommandInterceptor? interceptor = null) {
            var options = new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, Pooling = false }.ConnectionString);
            if (interceptor != null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        public static async Task<Store> Create() {
            var store = new Store();
            await using var admin = new NpgsqlConnection(store.connection); await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE SCHEMA {store.schema}", admin).ExecuteNonQueryAsync();
            await using var db = store.Open(); await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            Account User(string name) => new() { Email = name + "@friend-pg.test", Role = AccountRole.Student, Profile = new() { FullName = name, Nickname = name } };
            var a = User("Alice"); var b = User("Bob"); db.Accounts.AddRange(a, b); await db.SaveChangesAsync(); store.A = a.Id; store.B = b.Id;
            return store;
        }
        public async ValueTask DisposeAsync() {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }
}
