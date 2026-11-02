using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class FinancePostgresTests
{
    private sealed class Store : IAsyncDisposable
    {
        private readonly string schema = "finance_" + Guid.NewGuid().ToString("N");
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_FINANCE_TEST_POSTGRES")!;
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
        public async Task Start()
        {
            var host = new NpgsqlConnectionStringBuilder(connection).Host;
            if (host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Finance tests require an isolated local Postgres instance.");
            await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
            await new NpgsqlCommand($"CREATE SCHEMA {schema}", c).ExecuteNonQueryAsync();
        }
        public async ValueTask DisposeAsync()
        {
            await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", c).ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task MigrationPreservesStaffIdentityAndRevokesOldSessions()
    {
        if (Environment.GetEnvironmentVariable("UNINET_FINANCE_TEST_POSTGRES") is null) return;
        await using var store = new Store(); await store.Start(); await using var db = store.Open();
        await db.GetService<IMigrator>().MigrateAsync("20261025101348_AdminUserManagement");
        var accounts = new[] { 2, 3, 4 }.Select(n => new Account { Email = $"old-{n}@finance.test", Role = (AccountRole)n }).ToArray();
        db.AddRange(accounts); await db.SaveChangesAsync();
        db.RefreshTokens.Add(new() { AccountId = accounts[0].Id, TokenHash = "old-session", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) }); await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); await db.Database.MigrateAsync();
        Assert.Equal(AccountRole.Admin, (await db.Accounts.SingleAsync(a => a.Email == "old-2@finance.test")).Role);
        Assert.Equal(AccountRole.Moderator, (await db.Accounts.SingleAsync(a => a.Email == "old-3@finance.test")).Role);
        Assert.Equal(AccountRole.Assessor, (await db.Accounts.SingleAsync(a => a.Email == "old-4@finance.test")).Role);
        Assert.All(await db.Accounts.ToListAsync(), a => Assert.Equal(1, a.SessionVersion));
        Assert.NotNull((await db.RefreshTokens.SingleAsync()).RevokedAt);
        await db.GetService<IMigrator>().MigrateAsync("20261025101348_AdminUserManagement");
        db.ChangeTracker.Clear();
        Assert.Equal((AccountRole)2, (await db.Accounts.SingleAsync(a => a.Email == "old-2@finance.test")).Role);
    }

    [Fact]
    public async Task ConcurrentRequestsCannotSpendSameAvailableBalance()
    {
        if (Environment.GetEnvironmentVariable("UNINET_FINANCE_TEST_POSTGRES") is null) return;
        await using var store = new Store(); await store.Start(); await using var db = store.Open(); await db.Database.MigrateAsync();
        var owner = new Account { Email = "owner@finance.test", Role = AccountRole.Assessor };
        var actor = new Account { Email = "accountant@finance.test", Role = AccountRole.Accountant };
        db.AddRange(owner, actor); await db.SaveChangesAsync();
        await new FinanceService(db).CreditAsync(actor.Id, new(owner.Id, 1000, "INCOME", "Income"), default);
        async Task<bool> Request()
        {
            await using var other = store.Open();
            try { await new FinanceService(other).RequestAsync(owner.Id, new(Guid.NewGuid(), 700, "Bank", "123", "Owner"), default); return true; }
            catch (AuthException e) when (e.Status == 409) { return false; }
        }
        var results = await Task.WhenAll(Request(), Request());
        Assert.Single(results, success => success);
        Assert.Equal(300, (await new FinanceService(db).WalletAsync(owner.Id, default)).Available);
    }

    [Fact]
    public async Task SameBankEvidenceCannotConcurrentlyConfirmRefundAndWithdrawal()
    {
        if (Environment.GetEnvironmentVariable("UNINET_FINANCE_TEST_POSTGRES") is null) return;
        await using var store = new Store(); await store.Start(); await using var db = store.Open(); await db.Database.MigrateAsync();
        var owner = new Account { Email = "payout-owner@finance.test", Role = AccountRole.Assessor, Profile = new() { FullName = "Owner" } };
        var actor = new Account { Email = "payout-accountant@finance.test", Role = AccountRole.Accountant, Profile = new() { FullName = "Accountant" } };
        var reviewer = new Account { Email = "payout-reviewer@finance.test", Role = AccountRole.Admin };
        db.AddRange(owner, actor, reviewer); await db.SaveChangesAsync();
        var service = new FinanceService(db); await service.CreditAsync(actor.Id, new(owner.Id, 1000, "INCOME", "Income"), default);
        var withdrawal = await service.RequestAsync(owner.Id, new(Guid.NewGuid(), 400, "Bank", "123", "Owner"), default);
        await service.ReviewAsync(actor.Id, withdrawal.Id, new(true, "Approved"), default); await service.OrderAsync(actor.Id, withdrawal.Id, default);
        var payment = new UniNet.Domain.Entities.Transaction { Id = Guid.NewGuid(), UserId = owner.Profile!.Id, Amount = 1000, Status = UniNet.Domain.Enums.PaymentStatus.Success,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, Package = new() { Id = Guid.NewGuid(), Code = "EVIDENCE", Name = "Service" } };
        db.Add(payment); await db.SaveChangesAsync();
        var refund = await service.CreateRefundAsync(actor.Id, new(Guid.NewGuid(), payment.Id, 500, "Refund"), default);
        await service.ReviewRefundAsync(reviewer.Id, refund.Id, new(true, "Approved"), default);
        async Task<bool> Confirm(bool isRefund)
        {
            await using var other = store.Open(); var f = new FinanceService(other);
            try {
                if (isRefund) await f.ConfirmRefundAsync(actor.Id, refund.Id, new("BANK-999", "ONE-TRANSFER"), default);
                else await f.ConfirmAsync(actor.Id, withdrawal.Id, new("BANK-999", "ONE-TRANSFER"), default);
                return true;
            } catch (AuthException e) when (e.Status == 409) { return false; }
        }
        var results = await Task.WhenAll(Confirm(true), Confirm(false)); Assert.Single(results, success => success);
    }
}
