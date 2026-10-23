using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

// Shared eligibility for assignment and booking. Availability must cover the entire meeting.
internal sealed class VerificationAvailabilityCalendar
{
    private sealed record Window(Guid UserId, DateTimeOffset Start, DateTimeOffset End);
    private sealed record Busy(Guid UserId, DateTimeOffset? Start, DateTimeOffset? End, bool InProgress);
    private sealed record Booking(Guid LeaderId, Guid? UserId, DateTimeOffset Start, DateTimeOffset End);
    private readonly List<Window> windows;
    private readonly List<Busy> busy;
    private readonly List<Booking> bookings;
    private readonly HashSet<Guid> eligible;

    private VerificationAvailabilityCalendar(List<Window> windows, List<Busy> busy, List<Booking> bookings, HashSet<Guid> eligible)
        => (this.windows, this.busy, this.bookings, this.eligible) = (windows, busy, bookings, eligible);

    public static IQueryable<UserProfile> EligibleUsers(UniNetDbContext db, ProjectVerification v) =>
        db.UserProfiles.Where(p => p.Account.Role == AccountRole.Verificator && p.Account.Status == AccountStatus.Active &&
            p.Id != v.RequestByUserId && !db.Projects.Any(project => project.Id == v.ProjectId && project.CreatorId == p.Id) &&
            !db.ProjectMembers.Any(m => m.ProjectId == v.ProjectId && m.UserId == p.Id));

    public static IQueryable<UserProfile> AvailableUsers(UniNetDbContext db, ProjectVerification v, Guid? excludeAssignmentId)
    {
        var now = DateTimeOffset.UtcNow;
        return EligibleUsers(db, v).Where(p => db.VerificatorAvailabilities.Any(a => a.UserId == p.Id &&
                a.StartAt <= v.ScheduledStartAt && a.EndAt >= v.ScheduledEndAt) &&
            !db.ProjectVerifications.Any(b => b.Id != v.Id && b.ReservedVerificatorUserId == p.Id &&
                b.Status != ProjectVerificationStatus.Cancelled &&
                (b.BookingStatus == VerificationBookingStatus.Confirmed || b.BookingStatus == VerificationBookingStatus.Legacy ||
                 (b.BookingStatus == VerificationBookingStatus.Held && b.ReservedUntil > now)) &&
                b.ScheduledStartAt < v.ScheduledEndAt && b.ScheduledEndAt > v.ScheduledStartAt) &&
            !db.VerificationAssignments.Any(a => a.AssignedToUserId == p.Id && a.Id != excludeAssignmentId &&
                a.ProjectVerificationId != v.Id &&
                (a.Status == VerificationAssignmentStatus.Assigned || a.Status == VerificationAssignmentStatus.Overdue) &&
                a.ProjectVerification.Status == ProjectVerificationStatus.Pending &&
                (!a.ProjectVerification.ScheduledStartAt.HasValue || !a.ProjectVerification.ScheduledEndAt.HasValue ||
                 (a.ProjectVerification.ScheduledStartAt < v.ScheduledEndAt && a.ProjectVerification.ScheduledEndAt > v.ScheduledStartAt) ||
                 (a.ProjectVerification.MeetingStartAt.HasValue && !a.ProjectVerification.MeetingEndAt.HasValue))));
    }

    public static async Task<VerificationAvailabilityCalendar> LoadAsync(UniNetDbContext db, ProjectVerification v,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var eligible = (await EligibleUsers(db, v).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var windows = await db.VerificatorAvailabilities.AsNoTracking().Where(a => a.StartAt < to && a.EndAt > from)
            .Select(a => new Window(a.UserId, a.StartAt, a.EndAt)).ToListAsync(ct);
        var busy = await db.VerificationAssignments.AsNoTracking().Where(a => a.ProjectVerificationId != v.Id &&
            (a.Status == VerificationAssignmentStatus.Assigned || a.Status == VerificationAssignmentStatus.Overdue) &&
            a.ProjectVerification.Status == ProjectVerificationStatus.Pending &&
            (!a.ProjectVerification.ScheduledStartAt.HasValue || !a.ProjectVerification.ScheduledEndAt.HasValue ||
             (a.ProjectVerification.ScheduledStartAt < to && a.ProjectVerification.ScheduledEndAt > from) ||
             (a.ProjectVerification.MeetingStartAt.HasValue && !a.ProjectVerification.MeetingEndAt.HasValue)))
            .Select(a => new Busy(a.AssignedToUserId, a.ProjectVerification.ScheduledStartAt,
                a.ProjectVerification.ScheduledEndAt, a.ProjectVerification.MeetingStartAt.HasValue &&
                !a.ProjectVerification.MeetingEndAt.HasValue)).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var bookings = await db.ProjectVerifications.AsNoTracking().Where(b => b.Id != v.Id &&
            b.Status != ProjectVerificationStatus.Cancelled &&
            (b.BookingStatus == VerificationBookingStatus.Confirmed || b.BookingStatus == VerificationBookingStatus.Legacy ||
             (b.BookingStatus == VerificationBookingStatus.Held && b.ReservedUntil > now)) &&
            b.ScheduledStartAt < to && b.ScheduledEndAt > from)
            .Select(b => new Booking(b.Project.CreatorId, b.ReservedVerificatorUserId,
                b.ScheduledStartAt!.Value, b.ScheduledEndAt!.Value)).ToListAsync(ct);
        return new(windows, busy, bookings, eligible);
    }

    public List<Guid> Candidates(DateTimeOffset start, DateTimeOffset end) => windows
        .Where(w => w.Start <= start && w.End >= end && eligible.Contains(w.UserId))
        .Select(w => w.UserId).Distinct()
        .Where(id => !busy.Any(b => b.UserId == id && (b.InProgress || !b.Start.HasValue || !b.End.HasValue ||
                (b.Start < end && b.End > start))) &&
            !bookings.Any(b => b.UserId == id && b.Start < end && b.End > start)).OrderBy(id => id).ToList();

    public int Remaining(Guid leaderId, DateTimeOffset start, DateTimeOffset end, int capacity)
    {
        var overlapping = bookings.Where(b => b.Start < end && b.End > start).ToList();
        if (overlapping.Any(b => b.LeaderId == leaderId)) return 0;
        // Legacy reservations have no named resource. Reserve their peak occupancy conservatively.
        var unallocated = Peak(overlapping.Where(b => !b.UserId.HasValue), start, end);
        return Math.Max(0, Math.Min(capacity - Peak(overlapping, start, end), Candidates(start, end).Count - unallocated));
    }

    private static int Peak(IEnumerable<Booking> bookings, DateTimeOffset start, DateTimeOffset end)
    {
        var events = bookings.SelectMany(b => new[] { (At: b.Start < start ? start : b.Start, Delta: 1),
            (At: b.End > end ? end : b.End, Delta: -1) }).OrderBy(e => e.At).ThenBy(e => e.Delta);
        var occupancy = 0;
        var peak = 0;
        foreach (var item in events) { occupancy += item.Delta; peak = Math.Max(peak, occupancy); }
        return peak;
    }
}
