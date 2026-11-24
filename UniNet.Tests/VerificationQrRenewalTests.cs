using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class TransactionTests
{
    private static async Task PrepareRenewalAsync(Fixture f)
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(9), TimeSpan.Zero);
        f.Verification.ScheduledStartAt = start;
        f.Verification.ScheduledEndAt = start.AddHours(1);
        f.Configuration["ProjectVerification:WorkingDays:0"] = start.DayOfWeek.ToString();
        f.Configuration["ProjectVerification:ReservationMinutes"] = "30";
        f.Provider.OnCreate = request => Task.FromResult(FakeProvider.Result(request.OrderCode, request.AmountVnd) with { PaymentLinkId = $"link-{request.OrderCode}", QrCode = "bank-qr" });
        await f.Db.SaveChangesAsync();
    }

    private static async Task ExpireCheckoutAsync(Fixture f)
    {
        f.Verification.ReservedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        f.Verification.BookingStatus = VerificationBookingStatus.Expired;
        (await f.Db.Transactions.SingleAsync()).ExpiredAt = f.Verification.ReservedUntil;
        await f.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task RenewalClosesUnpaidExpiredAttemptRechecksSlotAndIsIdempotent()
    {
        await using var f = await Fixture.CreateAsync();
        await PrepareRenewalAsync(f);
        var first = await f.CheckoutAsync();
        var token = f.Verification.ReservationId;
        await ExpireCheckoutAsync(f);
        f.Provider.StatusResult = FakeProvider.Result(first.ProviderOrderCode!, 37000, PaymentStatus.Failed) with { PaymentLinkId = first.ProviderPaymentLinkId!, AmountPaidVnd = 0 };
        var renewal = new VerificationPaymentRenewalService(f.Service, new ProjectVerificationService(f.Db, f.Configuration));
        var renewed = await renewal.RenewAsync(f.Verification.Id, f.Owner.Id);
        Assert.NotEqual(first.Id, renewed.Id);
        Assert.NotEqual(token, f.Verification.ReservationId);
        Assert.True(renewed.ExpiredAt > DateTimeOffset.UtcNow);
        Assert.Equal(f.Verification.ReservedUntil, renewed.ExpiredAt);
        Assert.Equal(PaymentStatus.Failed, (await f.Db.Transactions.FindAsync(first.Id))!.Status);
        Assert.Equal(2, f.Provider.CreateCalls);
        var deadline = renewed.ExpiredAt;
        var repeated = await renewal.RenewAsync(f.Verification.Id, f.Owner.Id);
        Assert.Equal(renewed.Id, repeated.Id);
        Assert.Equal(deadline, repeated.ExpiredAt);
        Assert.Equal(2, f.Provider.CreateCalls);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending, 0)]
    [InlineData(PaymentStatus.Success, 37000)]
    [InlineData(PaymentStatus.Failed, 100)]
    public async Task RenewalCannotReplaceUnresolvedPaidOrPartiallyPaidAttempts(PaymentStatus status, decimal amountPaid)
    {
        await using var f = await Fixture.CreateAsync();
        await PrepareRenewalAsync(f);
        var first = await f.CheckoutAsync();
        await ExpireCheckoutAsync(f);
        f.Provider.StatusResult = FakeProvider.Result(first.ProviderOrderCode!, 37000, status) with { PaymentLinkId = first.ProviderPaymentLinkId!, AmountPaidVnd = amountPaid };
        var renewal = new VerificationPaymentRenewalService(f.Service, new ProjectVerificationService(f.Db, f.Configuration));
        var result = await renewal.RenewAsync(f.Verification.Id, f.Owner.Id);
        Assert.Equal(first.Id, result.Id);
        Assert.Equal(1, f.Provider.CreateCalls);
        Assert.Single(await f.Db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task RenewalCannotReserveUnavailableSlotOrBypassOwner()
    {
        await using var f = await Fixture.CreateAsync();
        await PrepareRenewalAsync(f);
        var first = await f.CheckoutAsync();
        await ExpireCheckoutAsync(f);
        f.Provider.StatusResult = FakeProvider.Result(first.ProviderOrderCode!, 37000, PaymentStatus.Failed) with { PaymentLinkId = first.ProviderPaymentLinkId!, AmountPaidVnd = 0 };
        var renewal = new VerificationPaymentRenewalService(f.Service, new ProjectVerificationService(f.Db, f.Configuration));
        await Assert.ThrowsAsync<ProjectVerificationException>(() => renewal.RenewAsync(f.Verification.Id, f.Other.Id));
        f.Db.VerificatorAvailabilities.RemoveRange(await f.Db.VerificatorAvailabilities.ToListAsync());
        await f.Db.SaveChangesAsync();
        var failure = await Assert.ThrowsAsync<ProjectVerificationException>(() => renewal.RenewAsync(f.Verification.Id, f.Owner.Id));
        Assert.Equal("MEETING_SLOT_UNAVAILABLE", failure.Code);
        Assert.Equal(1, f.Provider.CreateCalls);
    }
}
