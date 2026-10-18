using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task ExpiredHoldReleasesCapacityWithoutWaitingForWorkerAndRetainsAudit()
    {
        await using var f = await Fixture.CreateAsync();
        var first = (await f.CreateVerificationAsync()).Verification.Id;
        var second = (await f.CreateOtherVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        var held = await f.Service.ScheduleMeetingAsync(first, f.Owner.Id, slot);
        var stored = await f.Db.ProjectVerifications.SingleAsync(v => v.Id == first);
        stored.ReservedUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        Assert.Equal(VerificationBookingStatus.Expired, (await f.Service.GetDetailAsync(first, f.Owner.Id)).Verification.BookingStatus);
        await f.Service.ScheduleMeetingAsync(second, f.Other.Id, slot);
        await new VerificationReservationTasks(f.Db).ExpireAsync(DateTimeOffset.UtcNow);
        await f.Db.Entry(stored).ReloadAsync();
        Assert.Equal(VerificationBookingStatus.Expired, stored.BookingStatus);
        Assert.Equal(held.Verification.ReservationId, stored.ReservationId);
        Assert.Equal(slot.StartAt, stored.ScheduledStartAt);
        Assert.Empty(await f.Db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task ConfirmedBookingKeepsCapacityBeyondOriginalHoldDeadline()
    {
        await using var f = await Fixture.CreateAsync();
        var first = (await f.CreateVerificationAsync()).Verification.Id;
        var second = (await f.CreateOtherVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        await f.Service.ScheduleMeetingAsync(first, f.Owner.Id, slot);
        await f.PayAsync(first, f.Owner);
        var stored = await f.Db.ProjectVerifications.SingleAsync(v => v.Id == first);
        stored.ReservedUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        await new VerificationReservationTasks(f.Db).ExpireAsync(DateTimeOffset.UtcNow);
        await Error("MEETING_SLOT_UNAVAILABLE", () => f.Service.ScheduleMeetingAsync(second, f.Other.Id, slot));
    }

    [Fact]
    public async Task PendingCheckoutPreventsReschedulingButCancellationReleasesHold()
    {
        await using var f = await Fixture.CreateAsync();
        var first = (await f.CreateVerificationAsync()).Verification.Id;
        var second = (await f.CreateOtherVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        await f.Service.ScheduleMeetingAsync(first, f.Owner.Id, slot);
        await f.PayAsync(first, f.Owner, PaymentStatus.Pending);
        await Error("PAYMENT_PENDING", () => f.Service.ScheduleMeetingAsync(first, f.Owner.Id,
            new(slot.StartAt.AddHours(1), slot.EndAt.AddHours(1))));
        await f.Service.CancelAsync(first, f.Owner.Id, new("Cannot attend"));
        await f.Service.ScheduleMeetingAsync(second, f.Other.Id, slot);
        Assert.Equal(PaymentStatus.Pending, (await f.Db.Transactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task AssignmentTimeoutStartsAtConfirmedEligibilityInsteadOfOldPaidAt()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        (await f.Db.Transactions.SingleAsync()).PaidAt = DateTimeOffset.UtcNow.AddDays(-2);
        await f.Db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.RefundTimeoutsAsync(now, default);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
        Assert.Equal(ProjectVerificationStatus.Pending, (await f.Db.ProjectVerifications.SingleAsync()).Status);
        await tasks.RefundTimeoutsAsync(now.AddHours(24).AddSeconds(1), default);
        Assert.Single(await f.Db.RefundTransactions.ToListAsync());
    }
}
