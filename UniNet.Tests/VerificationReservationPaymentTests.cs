using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class TransactionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AdminRecoveryChecksPaymentAndCapacity(bool refundPending, bool noCapacity)
    {
        await using var f = await Fixture.CreateAsync();
        var checkout = await f.CheckoutAsync();
        f.Verification.ReservedUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        await new VerificationReservationTasks(f.Db).ExpireAsync(DateTimeOffset.UtcNow);
        f.Provider.WebhookResult = FakeProvider.Result(checkout.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        f.Other.Role = UniNet.Domain.AccountRole.Admin;
        if (noCapacity) f.Db.VerificatorAvailabilities.RemoveRange(await f.Db.VerificatorAvailabilities.ToListAsync());
        if (refundPending)
            f.Db.RefundTransactions.Add(new UniNet.Domain.Entities.RefundTransaction
            {
                TransactionId = checkout.Id, Amount = 37000, Status = PaymentStatus.Pending,
                Reason = "Refund requested", PaymentProvider = "PayOS"
            });
        await f.Db.SaveChangesAsync();
        var admin = new AdminProjectVerificationService(f.Db, f.Configuration);
        var request = new RecoverPaidBookingRequest(checkout.Id, "Verified delayed reconciliation; retain original slot");
        if (refundPending || noCapacity)
        {
            var error = await Assert.ThrowsAsync<ProjectVerificationException>(() =>
                admin.RecoverPaidBookingAsync(f.Verification.Id, f.Other.Id, request));
            Assert.Equal(refundPending ? "PAYMENT_NOT_RECOVERABLE" : "BOOKING_CAPACITY_UNAVAILABLE", error.Code);
            return;
        }
        var recovered = await admin.RecoverPaidBookingAsync(f.Verification.Id, f.Other.Id, request);
        Assert.Equal(VerificationBookingStatus.Confirmed, recovered.BookingStatus);
        Assert.NotNull(recovered.BookingConfirmedAt);
        var payment = await f.Service.GetAsync(f.Verification.Id, checkout.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Success, payment.Status);
        Assert.False(payment.RequiresReview);
        Assert.Null(payment.DiagnosticCode);
        var repeated = await Assert.ThrowsAsync<ProjectVerificationException>(() =>
            admin.RecoverPaidBookingAsync(f.Verification.Id, f.Other.Id, request));
        Assert.Equal("BOOKING_RECOVERY_NOT_ALLOWED", repeated.Code);
    }

    [Fact]
    public async Task CheckoutRequiresLiveHoldAndUsesItsExactDeadline()
    {
        await using var f = await Fixture.CreateAsync();
        f.Verification.BookingStatus = VerificationBookingStatus.None;
        await f.Db.SaveChangesAsync();
        await Error("RESERVATION_REQUIRED", f.CheckoutAsync);
        Assert.Equal(0, f.Provider.CreateCalls);
        f.Verification.BookingStatus = VerificationBookingStatus.Held;
        await f.Db.SaveChangesAsync();
        f.Provider.OnCreate = r =>
        {
            Assert.Equal(f.Verification.ReservedUntil, r.ExpiresAt);
            return Task.FromResult(FakeProvider.Result(r.OrderCode, r.AmountVnd));
        };
        var checkout = await f.CheckoutAsync();
        Assert.Equal(f.Verification.ReservedUntil, checkout.ExpiredAt);
        Assert.Equal(f.Verification.ReservationId, (await f.Db.VerificationTransactions.SingleAsync()).ReservationId);
    }

    [Fact]
    public async Task SuccessfulPaymentConfirmsOnceAndDelayedExpiryCannotRevokeIt()
    {
        await using var f = await Fixture.CreateAsync();
        var checkout = await f.CheckoutAsync();
        Assert.Equal(VerificationBookingStatus.Held, f.Verification.BookingStatus);
        f.Provider.WebhookResult = FakeProvider.Result(checkout.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        var confirmedAt = f.Verification.BookingConfirmedAt;
        Assert.NotNull(confirmedAt);
        Assert.Equal(VerificationBookingStatus.Confirmed, f.Verification.BookingStatus);
        Assert.Equal(confirmedAt, f.Verification.AssignmentEligibleAt);
        await f.Service.HandlePayOSWebhookAsync(default);
        await new VerificationReservationTasks(f.Db).ExpireAsync(DateTimeOffset.UtcNow.AddDays(1));
        await f.Db.Entry(f.Verification).ReloadAsync();
        Assert.Equal(VerificationBookingStatus.Confirmed, f.Verification.BookingStatus);
        Assert.Equal(confirmedAt, f.Verification.BookingConfirmedAt);
        var error = await Assert.ThrowsAsync<ProjectVerificationException>(() => new ProjectVerificationService(f.Db, f.Configuration)
            .ScheduleMeetingAsync(f.Verification.Id, f.Owner.Id, new(f.Verification.ScheduledStartAt!.Value.AddHours(1),
                f.Verification.ScheduledEndAt!.Value.AddHours(1))));
        Assert.Equal("BOOKING_ALREADY_PAID", error.Code);
    }

    [Fact]
    public async Task ExpiredReservationRecordsLateMoneyWithoutConfirmingOrLosingCharge()
    {
        await using var f = await Fixture.CreateAsync();
        var checkout = await f.CheckoutAsync();
        f.Verification.ReservedUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        await new VerificationReservationTasks(f.Db).ExpireAsync(DateTimeOffset.UtcNow);
        f.Provider.WebhookResult = FakeProvider.Result(checkout.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        await f.Service.HandlePayOSWebhookAsync(default);
        var paid = await f.Service.GetAsync(f.Verification.Id, checkout.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Success, paid.Status);
        Assert.Equal(TransactionMapping.LatePaymentReview, paid.DiagnosticCode);
        Assert.True(paid.RequiresReview);
        Assert.Equal(VerificationBookingStatus.Expired, f.Verification.BookingStatus);
        Assert.Null(f.Verification.BookingConfirmedAt);
        Assert.Null(f.Verification.AssignmentEligibleAt);
        Assert.Single(await f.Db.Transactions.ToListAsync());
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public async Task OldCheckoutCannotConfirmRenewedReservation()
    {
        await using var f = await Fixture.CreateAsync();
        var checkout = await f.CheckoutAsync();
        var oldToken = f.Verification.ReservationId;
        f.Provider.StatusResult = FakeProvider.Result(checkout.ProviderOrderCode!, 37000, PaymentStatus.Failed) with { AmountPaidVnd = 0 };
        await f.Service.ReconcileAsync(checkout.Id, f.Owner.Id);
        f.Verification.ReservationId = Guid.NewGuid();
        f.Verification.ReservedUntil = DateTimeOffset.UtcNow.AddMinutes(30);
        await f.Db.SaveChangesAsync();
        f.Provider.WebhookResult = FakeProvider.Result(checkout.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        Assert.Equal(VerificationBookingStatus.Held, f.Verification.BookingStatus);
        Assert.NotEqual(oldToken, f.Verification.ReservationId);
        Assert.Equal(TransactionMapping.LatePaymentReview,
            (await f.Service.GetAsync(f.Verification.Id, checkout.Id, f.Owner.Id)).DiagnosticCode);
    }

    [Fact]
    public async Task ChangedLevel1AfterCheckoutRecordsMoneyForReviewWithoutConfirming()
    {
        await using var f = await Fixture.CreateAsync();
        var checkout = await f.CheckoutAsync();
        f.Configuration["EvidenceModeration:PolicyVersion"] = "changed";
        f.Provider.WebhookResult = FakeProvider.Result(checkout.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        var paid = await f.Service.GetAsync(f.Verification.Id, checkout.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Success, paid.Status);
        Assert.Equal(TransactionMapping.BookingPaymentReview, paid.DiagnosticCode);
        Assert.Equal(VerificationBookingStatus.Held, f.Verification.BookingStatus);
        Assert.Null(f.Verification.BookingConfirmedAt);
    }

    [Fact]
    public async Task CancellationDuringProviderCallCannotConfirmBooking()
    {
        await using var f = await Fixture.CreateAsync();
        f.Provider.OnCreate = async r =>
        {
            await new ProjectVerificationService(f.Db, f.Configuration).CancelAsync(f.Verification.Id, f.Owner.Id, new("Cannot attend"));
            return FakeProvider.Result(r.OrderCode, r.AmountVnd, PaymentStatus.Success);
        };
        var paid = await f.CheckoutAsync();
        Assert.Equal(PaymentStatus.Success, paid.Status);
        Assert.True(paid.RequiresReview);
        Assert.Equal(VerificationBookingStatus.Cancelled, f.Verification.BookingStatus);
        Assert.Null(f.Verification.BookingConfirmedAt);
    }

    [Fact]
    public async Task PaymentCannotConfirmWhenCapacityWasReducedDuringCheckout()
    {
        await using var f = await Fixture.CreateAsync();
        var checkout = await f.CheckoutAsync();
        var staff = await f.Db.Accounts.SingleAsync(a => a.Role == UniNet.Domain.AccountRole.Verificator);
        staff.Status = UniNet.Domain.AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        f.Provider.WebhookResult = FakeProvider.Result(checkout.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        Assert.Equal(TransactionMapping.BookingPaymentReview,
            (await f.Service.GetAsync(f.Verification.Id, checkout.Id, f.Owner.Id)).DiagnosticCode);
        Assert.Equal(VerificationBookingStatus.Held, f.Verification.BookingStatus);
    }
}
