using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    private static async Task<Account> ClearCalendarAsync(Fixture f)
    {
        var staff = await f.Db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Role == AccountRole.Verificator);
        f.Db.VerificatorAvailabilities.RemoveRange(await f.Db.VerificatorAvailabilities.ToListAsync());
        await f.Db.SaveChangesAsync();
        return staff;
    }

    [Fact]
    public async Task AvailabilityCrudNormalizesUtcAndEnforcesOwnershipAndActiveRole()
    {
        await using var f = await Fixture.CreateAsync();
        var staff = await ClearCalendarAsync(f);
        var service = new VerificatorAvailabilityService(f.Db);
        var slot = Fixture.Slot();
        await Error("FORBIDDEN", () => service.SaveAsync(f.Owner.Id, null, new(slot.StartAt, slot.EndAt)));
        var created = await service.SaveAsync(staff.Id, null,
            new(slot.StartAt.ToOffset(TimeSpan.FromHours(7)), slot.EndAt.ToOffset(TimeSpan.FromHours(7))));
        Assert.Equal(TimeSpan.Zero, created.StartAt.Offset);
        Assert.Equal(slot.StartAt, created.StartAt);
        Assert.Equal(created.Id, Assert.Single(await service.ListAsync(staff.Id, slot.StartAt, slot.EndAt)).Id);
        var otherStaff = Fixture.Account("OtherStaff", AccountRole.Verificator);
        f.Db.Accounts.Add(otherStaff);
        await f.Db.SaveChangesAsync();
        await Error("AVAILABILITY_NOT_FOUND", () => service.SaveAsync(otherStaff.Id, created.Id, new(slot.StartAt, slot.EndAt)));
        await Error("AVAILABILITY_NOT_FOUND", () => service.DeleteAsync(otherStaff.Id, created.Id));
        var updated = await service.SaveAsync(staff.Id, created.Id, new(slot.StartAt, slot.EndAt.AddHours(1)));
        Assert.Equal(slot.EndAt.AddHours(1), updated.EndAt);
        staff.Status = AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Error("FORBIDDEN", () => service.ListAsync(staff.Id, slot.StartAt, slot.EndAt));
        staff.Status = AccountStatus.Active;
        await f.Db.SaveChangesAsync();
        await service.DeleteAsync(staff.Id, created.Id);
        Assert.Empty(await service.ListAsync(staff.Id, slot.StartAt, slot.EndAt));
    }

    [Fact]
    public async Task AvailabilityRejectsInvalidIntervalsAndOverlapButAllowsAdjacentWindows()
    {
        await using var f = await Fixture.CreateAsync();
        var staff = await ClearCalendarAsync(f);
        var service = new VerificatorAvailabilityService(f.Db);
        var slot = Fixture.Slot();
        foreach (var request in new SaveVerificatorAvailabilityRequest[]
        {
            new(slot.EndAt, slot.StartAt), new(slot.StartAt, slot.StartAt),
            new(DateTimeOffset.UtcNow.AddHours(-1), slot.EndAt),
            new(slot.StartAt, slot.StartAt.AddDays(32)), new(slot.StartAt.AddDays(365), slot.EndAt.AddDays(365))
        }) await Error("INVALID_AVAILABILITY_INTERVAL", () => service.SaveAsync(staff.Id, null, request));
        await Error("INVALID_AVAILABILITY_RANGE", () => service.ListAsync(staff.Id, slot.StartAt, slot.StartAt.AddDays(32)));
        await service.SaveAsync(staff.Id, null, new(slot.StartAt, slot.EndAt));
        await Error("AVAILABILITY_OVERLAP", () => service.SaveAsync(staff.Id, null, new(slot.StartAt.AddMinutes(1), slot.EndAt.AddMinutes(1))));
        await service.SaveAsync(staff.Id, null, new(slot.EndAt, slot.EndAt.AddHours(1)));
        Assert.Equal(2, (await service.ListAsync(staff.Id, slot.StartAt, slot.EndAt.AddHours(1))).Count);
    }

    [Fact]
    public async Task BookingRequiresAvailabilityCoveringWholeSlotAndProtectsActiveHold()
    {
        await using var f = await Fixture.CreateAsync();
        var staff = await ClearCalendarAsync(f);
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        Assert.Empty((await f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.EndAt)).Slots);
        await Error("MEETING_SLOT_UNAVAILABLE", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot));
        var service = new VerificatorAvailabilityService(f.Db);
        var window = await service.SaveAsync(staff.Id, null, new(slot.StartAt.AddMinutes(1), slot.EndAt));
        Assert.Empty((await f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.EndAt)).Slots);
        await service.SaveAsync(staff.Id, window.Id, new(slot.StartAt, slot.EndAt));
        Assert.Single((await f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.EndAt)).Slots);
        await f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot);
        Assert.Equal(staff.Profile!.Id, (await f.Db.ProjectVerifications.SingleAsync()).ReservedVerificatorUserId);
        await Error("AVAILABILITY_IN_USE", () => service.DeleteAsync(staff.Id, window.Id));
        await Error("AVAILABILITY_IN_USE", () => service.SaveAsync(staff.Id, window.Id, new(slot.StartAt.AddMinutes(1), slot.EndAt)));
        await service.SaveAsync(staff.Id, window.Id, new(slot.StartAt.AddHours(-1), slot.EndAt.AddHours(1)));
        await f.Service.CancelAsync(id, f.Owner.Id, new("Cancel reservation"));
        await service.DeleteAsync(staff.Id, window.Id);
    }

    [Fact]
    public async Task ExpiredHoldDoesNotPreventRemovingAvailability()
    {
        await using var f = await Fixture.CreateAsync();
        var staff = await ClearCalendarAsync(f);
        var slot = Fixture.Slot();
        var service = new VerificatorAvailabilityService(f.Db);
        var window = await service.SaveAsync(staff.Id, null, new(slot.StartAt, slot.EndAt));
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot);
        (await f.Db.ProjectVerifications.SingleAsync()).ReservedUntil = DateTimeOffset.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        await service.DeleteAsync(staff.Id, window.Id);
        Assert.Empty((await f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.EndAt)).Slots);
    }

    [Fact]
    public async Task NamedResourcePreventsDoubleBookingEvenWhenGlobalCapacityIsHigher()
    {
        await using var f = await Fixture.CreateAsync();
        f.Configuration["ProjectVerification:Capacity"] = "2";
        var staff = await ClearCalendarAsync(f);
        var service = new VerificatorAvailabilityService(f.Db);
        var slot = Fixture.Slot();
        await service.SaveAsync(staff.Id, null, new(slot.StartAt, slot.EndAt));
        var first = (await f.CreateVerificationAsync()).Verification.Id;
        var second = (await f.CreateOtherVerificationAsync()).Verification.Id;
        await f.Service.ScheduleMeetingAsync(first, f.Owner.Id, slot);
        Assert.Empty((await f.Service.GetAvailableMeetingSlotsAsync(second, f.Other.Id, slot.StartAt, slot.EndAt)).Slots);
        await Error("MEETING_SLOT_UNAVAILABLE", () => f.Service.ScheduleMeetingAsync(second, f.Other.Id, slot));
        var another = Fixture.Account("Another", AccountRole.Verificator);
        f.Db.Accounts.Add(another);
        await f.Db.SaveChangesAsync();
        var available = await f.Service.GetAvailableMeetingSlotsAsync(second, f.Other.Id, slot.StartAt, slot.EndAt);
        Assert.Equal(1, Assert.Single(available.Slots).RemainingCapacity);
        await f.Service.ScheduleMeetingAsync(second, f.Other.Id, slot);
        Assert.Equal(2, (await f.Db.ProjectVerifications.Select(v => v.ReservedVerificatorUserId).Distinct().ToListAsync()).Count);
    }

    [Fact]
    public async Task ProjectMemberCannotSupplyBookingAvailability()
    {
        await using var f = await Fixture.CreateAsync(passed: false);
        var staff = await f.Db.UserProfiles.SingleAsync(p => p.Account.Role == AccountRole.Verificator);
        f.Db.ProjectMembers.Add(new() { Id = Guid.NewGuid(), ProjectId = f.Project.Id, UserId = staff.Id, Role = "Member" });
        await f.Db.SaveChangesAsync();
        await f.SeedPassedAsync(f.Project.Id);
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        Assert.Empty((await f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.EndAt)).Slots);
        await Error("MEETING_SLOT_UNAVAILABLE", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot));
    }

    [Fact]
    public async Task AssignmentAndReplacementRequireAvailabilityAndProtectConfirmedCalendar()
    {
        await using var f = await Fixture.CreateAsync();
        var staff = await ClearCalendarAsync(f);
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var assignments = new AssignmentService(f.Db, f.Configuration);
        Assert.Empty(await assignments.GetSuitableVerificatorsAsync(id, f.Other.Id));
        Assert.Equal("VERIFICATOR_UNAVAILABLE", (await Assert.ThrowsAsync<AssignmentException>(() =>
            assignments.CreateAsync(f.Other.Id, new(id, staff.Profile!.Id)))).Code);
        var slot = Fixture.Slot();
        var service = new VerificatorAvailabilityService(f.Db);
        var window = await service.SaveAsync(staff.Id, null, new(slot.StartAt, slot.EndAt));
        var assigned = await assignments.CreateAsync(f.Other.Id, new(id, staff.Profile!.Id));
        await Error("AVAILABILITY_IN_USE", () => service.DeleteAsync(staff.Id, window.Id));
        var next = Fixture.Account("Next", AccountRole.Verificator);
        next.Profile!.VerificatorAvailabilities.Clear();
        f.Db.Accounts.Add(next);
        await f.Db.SaveChangesAsync();
        Assert.Equal("VERIFICATOR_UNAVAILABLE", (await Assert.ThrowsAsync<AssignmentException>(() =>
            assignments.ReplaceVerificatorAsync(assigned.Id, f.Other.Id, new(next.Profile.Id, "Change staff")))).Code);
        await service.SaveAsync(next.Id, null, new(slot.StartAt, slot.EndAt));
        await assignments.ReplaceVerificatorAsync(assigned.Id, f.Other.Id, new(next.Profile.Id, "Change staff"));
        Assert.Equal(next.Profile.Id, (await f.Db.ProjectVerifications.SingleAsync()).ReservedVerificatorUserId);
        await service.DeleteAsync(staff.Id, window.Id);
    }
}
