using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed partial class VerificationReservationPostgresTests
{
    private sealed class ReadBarrier(bool payment = false) : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            var sql = command.CommandText;
            var match = payment
                ? sql.Contains("\"Transactions\"") && sql.Contains("\"VerificationTransactions\"") && sql.Contains("\"ProjectVerifications\"") && sql.Contains("LIMIT 1")
                : sql.Contains("\"ProjectVerifications\"") && sql.Contains("\"BookingStatus\"") && sql.Contains("<>") && !sql.Contains("\"AiSummary\"");
            if (match && Interlocked.Increment(ref arrivals) <= 2)
            {
                if (arrivals == 2) both.TrySetResult();
                await both.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            return result;
        }
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string schema = "reservation_test_" + Guid.NewGuid().ToString("N");
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        public readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProjectVerification:TimeZoneId"] = "UTC", ["ProjectVerification:WorkdayStart"] = "09:00:00",
            ["ProjectVerification:WorkdayEnd"] = "17:00:00", ["ProjectVerification:SlotMinutes"] = "60",
            ["ProjectVerification:Capacity"] = "1", ["ProjectVerification:ReservationMinutes"] = "30",
            ["ProjectVerification:MinimumNoticeMinutes"] = "0", ["ProjectVerification:MaxAdvanceDays"] = "30",
            ["Payments:ReturnUrl"] = "https://frontend.test/paid", ["Payments:CancelUrl"] = "https://frontend.test/cancel"
        }).Build();
        public Guid Owner, Other, First, Second;
        public UniNetDbContext Open(DbCommandInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString);
            if (interceptor != null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        public async Task InitializeAsync(bool sameLeader = false)
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin); await command.ExecuteNonQueryAsync();
            await using var db = Open(); await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            foreach (var day in Enum.GetValues<DayOfWeek>()) Config[$"ProjectVerification:WorkingDays:{(int)day}"] = day.ToString();
            Account User(string name, AccountRole role = AccountRole.Student) => new() { Email = name + "@test.com", Role = role,
                Profile = new() { FullName = name, Nickname = name } };
            var owner = User("owner"); var other = sameLeader ? owner : User("other");
            db.Accounts.Add(owner); if (!sameLeader) db.Accounts.Add(other); db.Accounts.Add(User("staff", AccountRole.Verificator));
            var package = new Package { Id = Guid.NewGuid(), Code = "VERIFY", Name = "Verify", IsActive = true,
                Role = TargetRole.Student, Type = PackageType.Verification, Price = 100 };
            db.Packages.Add(package);
            Project ProjectFor(Account user) => new() { Creator = user.Profile!, Title = "Completed", Description = "Done",
                ProjectField = "IT", MemberTarget = 2, Status = ProjectStatus.Completed };
            var projects = new[] { ProjectFor(owner), ProjectFor(other) }; db.Projects.AddRange(projects); await db.SaveChangesAsync();
            foreach (var project in projects)
            {
                var source = new EvidenceModerationSource(db, Config);
                var snapshot = JsonSerializer.Serialize(await source.CaptureAsync(project.Id, default));
                var policy = JsonSerializer.Serialize(source.CapturePolicy());
                db.ProjectModerations.Add(new() { Purpose = ProjectModerationPurpose.ProjectEvidence, ProjectId = project.Id, Version = 1, SnapshotJson = snapshot,
                    SourceHash = EvidenceModerationSource.Hash(snapshot), PolicyJson = policy,
                    PolicyHash = EvidenceModerationSource.Hash(policy), Status = ProjectModerationStatus.Passed, CreatedAt = DateTimeOffset.UtcNow });
            }
            await db.SaveChangesAsync();
            Owner = owner.Id; Other = other.Id;
            var service = new ProjectVerificationService(db, Config);
            First = (await service.CreateAsync(projects[0].Id, Owner, new(package.Id))).Verification.Id;
            Second = (await service.CreateAsync(projects[1].Id, Other, new(package.Id))).Verification.Id;
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", admin); await command.ExecuteNonQueryAsync();
        }
    }

    [EvidencePostgresFact]
    public async Task ConcurrentLastSlotHoldsHaveOnlyOneWinner()
    {
        await using var store = new Store(); await store.InitializeAsync();
        await AssertOneWinnerAsync(store);
    }

    [EvidencePostgresFact]
    public async Task ConcurrentHoldsCannotDoubleBookSameLeaderAcrossProjects()
    {
        await using var store = new Store(); await store.InitializeAsync(sameLeader: true);
        store.Config["ProjectVerification:Capacity"] = "2";
        await using (var seed = store.Open())
        {
            seed.Accounts.Add(new() { Email = "staff2@test.com", Role = AccountRole.Verificator,
                Profile = new() { FullName = "Staff2", Nickname = "Staff2" } }); await seed.SaveChangesAsync();
        }
        await AssertOneWinnerAsync(store);
    }

    private static async Task AssertOneWinnerAsync(Store store)
    {
        var barrier = new ReadBarrier();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
        async Task<bool> Hold(Guid id, Guid user)
        {
            await using var db = store.Open(barrier);
            try { await new ProjectVerificationService(db, store.Config).ScheduleMeetingAsync(id, user, new(start, start.AddHours(1))); return true; }
            catch (ProjectVerificationException ex) when (ex.Code is "VERIFICATION_CONFLICT" or "MEETING_SLOT_UNAVAILABLE") { return false; }
        }
        var results = await Task.WhenAll(Hold(store.First, store.Owner), Hold(store.Second, store.Other));
        Assert.Single(results, r => r);
        await using var check = store.Open();
        Assert.Equal(1, await check.ProjectVerifications.CountAsync(v => v.BookingStatus == VerificationBookingStatus.Held));
    }

    private sealed class Provider : IPaymentProvider
    {
        public string Name => "PayOS";
        public ProviderPaymentResult Result = null!;
        public void EnsureConfigured() { }
        public Task<ProviderPaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest r, CancellationToken ct)
            => Task.FromResult(new ProviderPaymentResult(r.OrderCode, r.AmountVnd, "VND", "link", PaymentStatus.Pending));
        public Task<ProviderPaymentResult> GetPaymentStatusAsync(string code, CancellationToken ct) => Task.FromResult(Result);
        public ProviderPaymentResult VerifyWebhook(JsonElement payload) => Result;
    }

    [EvidencePostgresFact]
    public async Task ConcurrentRepeatedWebhooksConfirmExactlyOnceAndExpiryCannotRevokeBooking()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var provider = new Provider();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
        await using (var seed = store.Open())
        {
            await new ProjectVerificationService(seed, store.Config).ScheduleMeetingAsync(store.First, store.Owner, new(start, start.AddHours(1)));
            var payment = await new TransactionService(seed, provider, store.Config, NullLogger<TransactionService>.Instance).CreateAsync(store.First, store.Owner);
            provider.Result = new(payment.ProviderOrderCode!, 100, "VND", "link", PaymentStatus.Success, AmountPaidVnd: 100);
        }
        var barrier = new ReadBarrier(payment: true);
        async Task Apply()
        {
            await using var db = store.Open(barrier);
            try { await new TransactionService(db, provider, store.Config, NullLogger<TransactionService>.Instance).HandlePayOSWebhookAsync(default); }
            catch (TransactionException ex) when (ex.Code == "PAYMENT_CONFLICT") { }
        }
        await Task.WhenAll(Apply(), Apply());
        await using var check = store.Open();
        await new TransactionService(check, provider, store.Config, NullLogger<TransactionService>.Instance).HandlePayOSWebhookAsync(default);
        await new VerificationReservationTasks(check).ExpireAsync(DateTimeOffset.UtcNow.AddDays(1));
        var booking = await check.ProjectVerifications.AsNoTracking().SingleAsync(v => v.Id == store.First);
        Assert.Equal(VerificationBookingStatus.Confirmed, booking.BookingStatus);
        Assert.NotNull(booking.AssignmentEligibleAt);
        Assert.Single(await check.Transactions.Where(t => t.Status == PaymentStatus.Success).ToListAsync());
        Assert.Empty(await check.RefundTransactions.ToListAsync());
    }
    [EvidencePostgresFact]
    public async Task LatePaymentRacingExpiryAndAnotherLeaderCannotStealReleasedSlot()
    {
        await using var store = new Store(); await store.InitializeAsync();
        var provider = new Provider();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
        await using (var seed = store.Open())
        {
            await new ProjectVerificationService(seed, store.Config).ScheduleMeetingAsync(store.First, store.Owner, new(start, start.AddHours(1)));
            var payment = await new TransactionService(seed, provider, store.Config, NullLogger<TransactionService>.Instance).CreateAsync(store.First, store.Owner);
            provider.Result = new(payment.ProviderOrderCode!, 100, "VND", "link", PaymentStatus.Success, AmountPaidVnd: 100);
            (await seed.ProjectVerifications.SingleAsync(v => v.Id == store.First)).ReservedUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
            await seed.SaveChangesAsync();
        }
        async Task Payment()
        {
            await using var db = store.Open();
            try { await new TransactionService(db, provider, store.Config, NullLogger<TransactionService>.Instance).HandlePayOSWebhookAsync(default); }
            catch (TransactionException ex) when (ex.Code == "PAYMENT_CONFLICT") { }
        }
        async Task Hold()
        {
            await using var db = store.Open();
            try { await new ProjectVerificationService(db, store.Config).ScheduleMeetingAsync(store.Second, store.Other, new(start, start.AddHours(1))); }
            catch (ProjectVerificationException ex) when (ex.Code == "VERIFICATION_CONFLICT") { }
        }
        await using (var expiry = store.Open())
            await Task.WhenAll(Payment(), Hold(), new VerificationReservationTasks(expiry).ExpireAsync(DateTimeOffset.UtcNow));
        // Replay/retry the same operations after any serializable conflict, never invent another charge.
        await Payment(); await Hold();
        await using var check = store.Open();
        await new VerificationReservationTasks(check).ExpireAsync(DateTimeOffset.UtcNow);
        Assert.Equal(VerificationBookingStatus.Expired, (await check.ProjectVerifications.SingleAsync(v => v.Id == store.First)).BookingStatus);
        Assert.Equal(VerificationBookingStatus.Held, (await check.ProjectVerifications.SingleAsync(v => v.Id == store.Second)).BookingStatus);
        var charge = await check.Transactions.SingleAsync();
        Assert.Equal(PaymentStatus.Success, charge.Status);
        Assert.Equal(TransactionMapping.LatePaymentReview, charge.FailureReason);
        Assert.Empty(await check.RefundTransactions.ToListAsync());
    }

}
