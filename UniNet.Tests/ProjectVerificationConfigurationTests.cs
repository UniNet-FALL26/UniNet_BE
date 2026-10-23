using Microsoft.Extensions.Configuration;
using UniNet.Application.Services;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task ShippedApiConfigurationOffersVietnamWorkingHoursAndAllowsReservation()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "api-appsettings.json"))
            .Build();
        var policy = ProjectVerificationPolicy.Load(configuration);
        var zone = policy.RequireMeetingPolicy();
        Assert.Equal(TimeSpan.FromHours(7), zone.GetUtcOffset(DateTimeOffset.UtcNow));
        await using var f = await Fixture.CreateAsync(configuration: configuration);
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var from = DateTimeOffset.UtcNow.AddDays(2);
        var to = from.AddDays(14);

        var available = await f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, from, to);

        Assert.Equal(zone.Id, available.TimeZoneId);
        Assert.NotEmpty(available.Slots);
        foreach (var slot in available.Slots)
        {
            var localStart = TimeZoneInfo.ConvertTime(slot.StartAt, zone);
            var localEnd = TimeZoneInfo.ConvertTime(slot.EndAt, zone);
            Assert.Contains(localStart.DayOfWeek, policy.WorkingDays);
            Assert.True(localStart.TimeOfDay >= policy.WorkdayStart!.Value.ToTimeSpan());
            Assert.True(localEnd.TimeOfDay <= policy.WorkdayEnd!.Value.ToTimeSpan());
            Assert.Equal(TimeSpan.FromMinutes(policy.SlotMinutes), slot.EndAt - slot.StartAt);
            Assert.Equal(1, slot.RemainingCapacity);
        }

        var first = available.Slots.First();
        var reserved = await f.Service.ScheduleMeetingAsync(id, f.Owner.Id, new(first.StartAt, first.EndAt));
        Assert.Equal(VerificationBookingStatus.Held, reserved.Verification.BookingStatus);
        Assert.Equal(first.StartAt, reserved.Verification.ScheduledStartAt);
        Assert.InRange((reserved.Verification.ReservedUntil!.Value - DateTimeOffset.UtcNow).TotalMinutes, 29, 30);
    }
}
