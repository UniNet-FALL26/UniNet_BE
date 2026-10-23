using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class VerificationReservationPostgresTests
{
    [EvidencePostgresFact]
    public async Task PostgreSqlBookingPaymentAssignmentAndCalendarProtectionUseDeclaredAvailability()
    {
        await using var store = new Store();
        await store.InitializeAsync();
        await using var db = store.Open();
        var staff = await db.UserProfiles.SingleAsync(p => p.Account.Role == AccountRole.Verificator);
        var admin = CreateAdmin();
        db.Accounts.Add(admin);
        await db.SaveChangesAsync();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
        await new ProjectVerificationService(db, store.Config).ScheduleMeetingAsync(store.First, store.Owner, new(start, start.AddHours(1)));
        var provider = new Provider();
        var payments = new TransactionService(db, provider, store.Config, NullLogger<TransactionService>.Instance);
        var payment = await payments.CreateAsync(store.First, store.Owner);
        provider.Result = new(payment.ProviderOrderCode!, 100, "VND", "link", PaymentStatus.Success, AmountPaidVnd: 100);
        await payments.HandlePayOSWebhookAsync(default);
        var assignments = new AssignmentService(db, store.Config);
        Assert.Equal(staff.Id, Assert.Single(await assignments.GetSuitableVerificatorsAsync(store.First, admin.Id)).UserId);
        await assignments.CreateAsync(admin.Id, new(store.First, staff.Id));
        var window = await db.VerificatorAvailabilities.SingleAsync();
        var error = await Assert.ThrowsAsync<ProjectVerificationException>(() =>
            new VerificatorAvailabilityService(db).DeleteAsync(staff.AccountId, window.Id));
        Assert.Equal("AVAILABILITY_IN_USE", error.Code);

        static Account CreateAdmin() => new()
        {
            Email = "admin@availability.test", Role = AccountRole.Admin,
            Profile = new() { FullName = "Admin", Nickname = "Admin" }
        };
    }

    private sealed class CalendarReadBarrier : DbCommandInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data,
            DbDataReader result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("FROM \"VerificatorAvailabilities\"") && Interlocked.Increment(ref arrivals) <= 2)
            {
                if (arrivals == 2) both.TrySetResult();
                await both.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            return result;
        }
    }

    [EvidencePostgresFact]
    public async Task ConcurrentHoldsCannotReserveSameVerificatorWithSpareGlobalCapacity()
    {
        await using var store = new Store();
        await store.InitializeAsync();
        store.Config["ProjectVerification:Capacity"] = "2";
        await AssertOneWinnerAsync(store);
        await using var db = store.Open();
        var held = await db.ProjectVerifications.SingleAsync(v => v.BookingStatus == VerificationBookingStatus.Held);
        Assert.NotNull(held.ReservedVerificatorUserId);
    }

    [EvidencePostgresFact]
    public async Task ConcurrentAvailabilityRemovalAndHoldCannotLeaveUncoveredReservation()
    {
        await using var store = new Store();
        await store.InitializeAsync();
        Guid windowId, staffAccountId;
        await using (var seed = store.Open())
        {
            var window = await seed.VerificatorAvailabilities.Include(a => a.User).SingleAsync();
            windowId = window.Id;
            staffAccountId = window.User.AccountId;
        }
        var barrier = new CalendarReadBarrier();
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
        async Task Hold()
        {
            await using var db = store.Open(barrier);
            try { await new ProjectVerificationService(db, store.Config).ScheduleMeetingAsync(store.First, store.Owner, new(start, start.AddHours(1))); }
            catch (ProjectVerificationException ex) when (ex.Code is "VERIFICATION_CONFLICT" or "MEETING_SLOT_UNAVAILABLE") { }
        }
        async Task Remove()
        {
            await using var db = store.Open(barrier);
            try { await new VerificatorAvailabilityService(db).DeleteAsync(staffAccountId, windowId); }
            catch (ProjectVerificationException ex) when (ex.Code is "AVAILABILITY_CONFLICT" or "AVAILABILITY_IN_USE") { }
        }
        await Task.WhenAll(Hold(), Remove());
        await using var check = store.Open();
        var held = await check.ProjectVerifications.AnyAsync(v => v.BookingStatus == VerificationBookingStatus.Held);
        var available = await check.VerificatorAvailabilities.AnyAsync(a => a.Id == windowId);
        Assert.True(!held || available, "A live hold must retain its availability after concurrent calendar changes.");
        Assert.True(held || !available, "At least one of the competing operations should succeed.");
    }
}
