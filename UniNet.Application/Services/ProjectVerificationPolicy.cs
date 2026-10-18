using Microsoft.Extensions.Configuration;

namespace UniNet.Application.Services;

// No meeting hours/capacity are assumed until the deployment supplies its approved policy.
public sealed class ProjectVerificationPolicy
{
    public string? TimeZoneId { get; set; }
    public DayOfWeek[] WorkingDays { get; set; } = [];
    public TimeOnly? WorkdayStart { get; set; }
    public TimeOnly? WorkdayEnd { get; set; }
    public int SlotMinutes { get; set; }
    public int Capacity { get; set; }
    public int? MinimumNoticeMinutes { get; set; }
    public int MaxAdvanceDays { get; set; }

    public static ProjectVerificationPolicy Load(IConfiguration configuration)
    {
        try { return configuration.GetSection("ProjectVerification").Get<ProjectVerificationPolicy>() ?? new(); }
        catch (InvalidOperationException) { throw MissingPolicy(); }
    }

    public TimeZoneInfo RequireMeetingPolicy()
    {
        if (string.IsNullOrWhiteSpace(TimeZoneId) || WorkingDays.Length == 0 ||
            WorkingDays.Any(d => !Enum.IsDefined(d)) || !WorkdayStart.HasValue || !WorkdayEnd.HasValue ||
            WorkdayEnd <= WorkdayStart || SlotMinutes is < 1 or > 480 || Capacity is < 1 or > 100 ||
            !MinimumNoticeMinutes.HasValue || MinimumNoticeMinutes is < 0 or > 525600 || MaxAdvanceDays is < 1 or > 365 ||
            (WorkdayEnd.Value.ToTimeSpan() - WorkdayStart.Value.ToTimeSpan()).TotalMinutes < SlotMinutes)
            throw MissingPolicy();
        try { return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch (TimeZoneNotFoundException) { throw MissingPolicy(); }
        catch (InvalidTimeZoneException) { throw MissingPolicy(); }
    }

    private static ProjectVerificationException MissingPolicy() => new("MEETING_POLICY_NOT_CONFIGURED",
        "Configure the approved meeting timezone, working days/hours, slot duration, capacity, notice and booking horizon.", 503);

    public IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> GenerateSlots(
        DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, TimeZoneInfo zone)
    {
        var earliest = now.AddMinutes(MinimumNoticeMinutes!.Value);
        var horizon = now.AddDays(MaxAdvanceDays);
        if (to <= earliest || from > horizon) yield break;
        var scanFrom = from < earliest ? earliest : from;
        var scanTo = to > horizon.AddMinutes(SlotMinutes) ? horizon.AddMinutes(SlotMinutes) : to;
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scanFrom, zone).DateTime);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scanTo, zone).DateTime);
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            if (!WorkingDays.Contains(date.DayOfWeek)) continue;
            var close = date.ToDateTime(WorkdayEnd!.Value);
            for (var localStart = date.ToDateTime(WorkdayStart!.Value);
                localStart.AddMinutes(SlotMinutes) <= close; localStart = localStart.AddMinutes(SlotMinutes))
            {
                var localEnd = localStart.AddMinutes(SlotMinutes);
                // Ambiguous or nonexistent local times cannot form a uniquely bookable slot.
                if (zone.IsInvalidTime(localStart) || zone.IsAmbiguousTime(localStart) ||
                    zone.IsInvalidTime(localEnd) || zone.IsAmbiguousTime(localEnd)) continue;
                var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone));
                var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone));
                if (end - start != TimeSpan.FromMinutes(SlotMinutes)) continue;
                if (start >= from && end <= to && start > now &&
                    start >= now.AddMinutes(MinimumNoticeMinutes!.Value) && start <= now.AddDays(MaxAdvanceDays))
                    yield return (start, end);
            }
        }
    }
}
