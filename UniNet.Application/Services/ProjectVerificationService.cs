using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectVerificationService(UniNetDbContext db, IConfiguration configuration) : IProjectVerificationService
{
    public Task<ProjectVerificationDetailResponse> CreateAsync(Guid projectId, Guid accountId,
        CreateProjectVerificationRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        var leader = await GetActiveProfileAsync(accountId, ct);
        var project = await GetOwnedProjectAsync(projectId, leader.Id, ct);
        Require(project.Status == ProjectStatus.Completed, "PROJECT_NOT_COMPLETED",
            "Only completed projects can request verification.", 409);
        Require(!await db.ProjectVerifications.AnyAsync(v => v.ProjectId == projectId &&
            v.Status == ProjectVerificationStatus.Pending, ct), "VERIFICATION_EXISTS",
            "This project already has a pending verification request.", 409);
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == request.PackageId, ct)
            ?? throw new ProjectVerificationException("PACKAGE_NOT_FOUND", "Verification package not found.", 404);
        Require(package.IsActive && package.Type == PackageType.Verification, "INVALID_VERIFICATION_PACKAGE",
            "Select an active verification package.");
        Require((leader.Account.Role == AccountRole.Student && package.Role == TargetRole.Student) ||
            (leader.Account.Role == AccountRole.Partner && package.Role == TargetRole.Company),
            "INVALID_PACKAGE_ROLE", "The package is not available for your account role.");

        var moderation = await new VerificationEligibility(db, configuration).RequirePassedAsync(projectId, null, ct);
        var now = DateTimeOffset.UtcNow;
        var verification = new ProjectVerification
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Project = project,
            RequestByUserId = leader.Id, RequestByUser = leader, PackageId = package.Id, Package = package,
            EvidenceModerationId = moderation.Id, EvidenceModeration = moderation,
            Status = ProjectVerificationStatus.Pending, SubmittedAt = now, CreatedAt = now, UpdatedAt = now
        };
        db.ProjectVerifications.Add(verification);
        // The project remains Completed; AI/payment/final decisions are separate operations.
        return ProjectVerificationMapping.ToDetailResponse(verification);
    }, ct);

    public async Task<ProjectVerificationsResponse> GetMyVerificationsAsync(Guid accountId, Guid? projectId = null,
        ProjectVerificationStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var leader = await GetActiveProfileAsync(accountId, ct);
        if (projectId.HasValue) await GetOwnedProjectAsync(projectId.Value, leader.Id, ct);
        Require(!status.HasValue || Enum.IsDefined(status.Value), "INVALID_VERIFICATION_STATUS", "Invalid verification status.");
        var query = db.ProjectVerifications.AsNoTracking().Include(v => v.Project)
            .Where(v => v.Project.CreatorId == leader.Id);
        if (projectId.HasValue) query = query.Where(v => v.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(v => v.Status == status.Value);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(v => v.CreatedAt).ThenBy(v => v.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize),
            items.Select(ProjectVerificationMapping.ToResponse).ToList());
    }

    public async Task<ProjectVerificationDetailResponse> GetDetailAsync(Guid verificationId, Guid accountId,
        CancellationToken ct = default) => ProjectVerificationMapping.ToDetailResponse(
            await GetOwnedVerificationAsync(verificationId, accountId, false, ct));

    public async Task<VerificationMeetingSlotsResponse> GetAvailableMeetingSlotsAsync(Guid verificationId, Guid accountId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var verification = await GetOwnedVerificationAsync(verificationId, accountId, false, ct);
        await new VerificationEligibility(db, configuration).RequireCaseAsync(verification, ct);
        RequireSchedulable(verification);
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        Require(to > from && to - from <= TimeSpan.FromDays(31), "INVALID_SLOT_RANGE",
            "Provide a date range of at most 31 days.");
        var policy = ProjectVerificationPolicy.Load(configuration);
        var zone = policy.RequireMeetingPolicy();
        var now = DateTimeOffset.UtcNow;
        var capacity = await GetCapacityAsync(policy, ct);
        var bookings = await GetBookingsAsync(verification.Id, from, to, ct);
        var slots = policy.GenerateSlots(from, to, now, zone)
            .Select(s => new VerificationMeetingSlotResponse(s.Start, s.End,
                RemainingCapacity(bookings, verification.Project.CreatorId, s.Start, s.End, capacity)))
            .Where(s => s.RemainingCapacity > 0).ToList();
        return new(zone.Id, slots);
    }

    public Task<ProjectVerificationDetailResponse> ScheduleMeetingAsync(Guid verificationId, Guid accountId,
        ScheduleProjectVerificationRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        var verification = await GetOwnedVerificationAsync(verificationId, accountId, true, ct);
        await new VerificationEligibility(db, configuration).RequireCaseAsync(verification, ct);
        RequireSchedulable(verification);
        var start = request.StartAt.ToUniversalTime();
        var end = request.EndAt.ToUniversalTime();
        Require(end > start && end - start <= TimeSpan.FromHours(8), "INVALID_MEETING_SLOT", "Invalid meeting interval.");
        var policy = ProjectVerificationPolicy.Load(configuration);
        var zone = policy.RequireMeetingPolicy();
        var now = DateTimeOffset.UtcNow;
        var holdMinutes = policy.RequireReservationMinutes();
        Require(!verification.VerificationTransactions.Any(t => t.Transaction.Status == PaymentStatus.Pending),
            "PAYMENT_PENDING", "Reconcile the existing checkout before changing or renewing a reservation.", 409);
        Require(policy.GenerateSlots(start, end, now, zone).Any(s => s.Start == start && s.End == end),
            "INVALID_MEETING_SLOT", "The interval does not match an available slot in the meeting policy.");
        var capacity = await GetCapacityAsync(policy, ct);
        var bookings = await GetBookingsAsync(verification.Id, start, end, ct);
        Require(RemainingCapacity(bookings, verification.Project.CreatorId, start, end, capacity) > 0,
            "MEETING_SLOT_UNAVAILABLE", "The slot is full or conflicts with another meeting of this leader.", 409);
        // Repeating the same active hold cannot extend its deadline indefinitely.
        if (verification.BookingStatus == VerificationBookingStatus.Held && verification.ReservedUntil > now &&
            verification.ScheduledStartAt == start && verification.ScheduledEndAt == end)
            return ProjectVerificationMapping.ToDetailResponse(verification);
        verification.ReservationId = Guid.NewGuid();
        verification.BookingStatus = VerificationBookingStatus.Held;
        var expires = now.AddMinutes(holdMinutes);
        verification.ReservedUntil = DateTimeOffset.FromUnixTimeSeconds((expires < start ? expires : start).ToUnixTimeSeconds());
        verification.BookingConfirmedAt = null;
        verification.AssignmentEligibleAt = null;
        verification.ScheduledStartAt = start;
        verification.ScheduledEndAt = end;
        verification.UpdatedAt = now;
        return ProjectVerificationMapping.ToDetailResponse(verification);
    }, ct);

    public Task<ProjectVerificationDetailResponse> CancelAsync(Guid verificationId, Guid accountId,
        CancelProjectVerificationRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        var verification = await GetOwnedVerificationAsync(verificationId, accountId, true, ct);
        RequireMutable(verification);
        Require(!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Trim().Length <= 2000,
            "INVALID_CANCELLATION_REASON", "Cancellation reason must be between 1 and 2000 characters.");
        verification.BookingStatus = VerificationBookingStatus.Cancelled;
        verification.Status = ProjectVerificationStatus.Cancelled;
        verification.CancellationReason = request.Reason.Trim();
        verification.CancelledAt = DateTimeOffset.UtcNow;
        verification.UpdatedAt = verification.CancelledAt.Value;
        // Cancellation never changes charge status or creates a refund; that policy is intentionally separate.
        return ProjectVerificationMapping.ToDetailResponse(verification);
    }, ct);

    public async Task<VerificationFinalResultResponse> GetFinalResultAsync(Guid verificationId, Guid accountId,
        CancellationToken ct = default)
    {
        var verification = await GetOwnedVerificationAsync(verificationId, accountId, false, ct);
        return ProjectVerificationMapping.ToFinalResponse(verification)
            ?? throw new ProjectVerificationException("VERIFICATION_RESULT_NOT_AVAILABLE", "No report or final result is available yet.", 404);
    }

    private async Task<UserProfile> GetActiveProfileAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new ProjectVerificationException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(profile.Account.Status == AccountStatus.Active, "ACCOUNT_INACTIVE", "Only active accounts can access verification.", 403);
        return profile;
    }

    private async Task<Project> GetOwnedProjectAsync(Guid projectId, Guid profileId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectVerificationException("PROJECT_NOT_FOUND", "Project not found.", 404);
        Require(project.CreatorId == profileId, "FORBIDDEN", "Only the project leader can access its verifications.", 403);
        return project;
    }

    private async Task<ProjectVerification> GetOwnedVerificationAsync(Guid verificationId, Guid accountId,
        bool tracked, CancellationToken ct)
    {
        var profile = await GetActiveProfileAsync(accountId, ct);
        IQueryable<ProjectVerification> query = db.ProjectVerifications.Include(v => v.Project)
            .Include(v => v.EvidenceModeration).ThenInclude(m => m!.Attempts).Include(v => v.Assignments).ThenInclude(a => a.Report)

            .Include(v => v.VerificationTransactions).ThenInclude(v => v.Transaction)
                .ThenInclude(t => t.RefundTransactions).AsSplitQuery();
        if (!tracked) query = query.AsNoTracking();
        var verification = await query.FirstOrDefaultAsync(v => v.Id == verificationId, ct)
            ?? throw new ProjectVerificationException("VERIFICATION_NOT_FOUND", "Verification request not found.", 404);
        Require(verification.Project.CreatorId == profile.Id, "FORBIDDEN", "Only the project leader can access this verification.", 403);
        return verification;
    }

    private static void RequireMutable(ProjectVerification verification)
    {
        Require(verification.Status == ProjectVerificationStatus.Pending && !verification.FinalizedAt.HasValue,
            "INVALID_VERIFICATION_STATE", "Only pending, unfinalized verifications can be changed.", 409);
        Require(verification.Assignments.Count == 0 && !verification.MeetingStartAt.HasValue &&
            !verification.MeetingEndAt.HasValue &&
            (!verification.ScheduledStartAt.HasValue || verification.ScheduledStartAt > DateTimeOffset.UtcNow ||
             verification.BookingStatus is VerificationBookingStatus.Expired ||
             (verification.BookingStatus == VerificationBookingStatus.Held && verification.ReservedUntil <= DateTimeOffset.UtcNow)),
            "VERIFICATION_LOCKED", "The verification cannot be changed after assignment or the scheduled meeting begins.", 409);
    }

    private static void RequireSchedulable(ProjectVerification verification)
    {
        RequireMutable(verification);
        Require(verification.BookingStatus != VerificationBookingStatus.Legacy, "LEGACY_BOOKING_REVIEW_REQUIRED",
            "Review the historical booking before changing its schedule.", 409);
        Require(verification.BookingStatus != VerificationBookingStatus.Confirmed &&
            !verification.VerificationTransactions.Any(v => v.Transaction.Status == PaymentStatus.Success),
            "BOOKING_ALREADY_PAID", "The schedule cannot be changed after payment.", 409);
    }

    // Run inside payment's Serializable transaction as well: another holder can win at expiry.
    internal async Task<bool> HasConfirmationCapacityAsync(ProjectVerification verification, CancellationToken ct)
    {
        var policy = ProjectVerificationPolicy.Load(configuration);
        policy.RequireMeetingPolicy();
        var leaderId = await db.Projects.Where(p => p.Id == verification.ProjectId).Select(p => p.CreatorId).SingleAsync(ct);
        var bookings = await GetBookingsAsync(verification.Id, verification.ScheduledStartAt!.Value,
            verification.ScheduledEndAt!.Value, ct);
        return RemainingCapacity(bookings, leaderId, verification.ScheduledStartAt.Value,
            verification.ScheduledEndAt.Value, await GetCapacityAsync(policy, ct)) > 0;
    }

    private async Task<int> GetCapacityAsync(ProjectVerificationPolicy policy, CancellationToken ct)
    {
        var staff = await db.UserProfiles.CountAsync(p => p.Account.Role == AccountRole.Verificator &&
            p.Account.Status == AccountStatus.Active, ct);
        return Math.Min(policy.Capacity, staff);
    }

    private sealed record Booking(Guid LeaderId, DateTimeOffset Start, DateTimeOffset End);

    private async Task<List<Booking>> GetBookingsAsync(Guid excludeId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.ProjectVerifications.AsNoTracking().Where(v => v.Id != excludeId &&
            v.Status != ProjectVerificationStatus.Cancelled &&
            (v.BookingStatus == VerificationBookingStatus.Confirmed || v.BookingStatus == VerificationBookingStatus.Legacy ||
             (v.BookingStatus == VerificationBookingStatus.Held && v.ReservedUntil > now)) && v.ScheduledStartAt.HasValue && v.ScheduledEndAt.HasValue &&
            v.ScheduledStartAt < to && v.ScheduledEndAt > from)
            .Select(v => new Booking(v.Project.CreatorId, v.ScheduledStartAt!.Value, v.ScheduledEndAt!.Value)).ToListAsync(ct);
    }

    private static int RemainingCapacity(List<Booking> bookings, Guid leaderId,
        DateTimeOffset start, DateTimeOffset end, int capacity)
    {
        var overlapping = bookings.Where(b => b.Start < end && b.End > start).ToList();
        if (overlapping.Any(b => b.LeaderId == leaderId)) return 0;
        // Count simultaneous occupancy, not the total number of partially overlapping meetings.
        var events = overlapping.SelectMany(b => new[]
        {
            (At: b.Start < start ? start : b.Start, Delta: 1),
            (At: b.End > end ? end : b.End, Delta: -1)
        }).OrderBy(e => e.At).ThenBy(e => e.Delta);
        var occupancy = 0;
        var peak = 0;
        foreach (var item in events) { occupancy += item.Delta; peak = Math.Max(peak, occupancy); }
        return Math.Max(0, capacity - peak);
    }

    private async Task<T> MutateAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            // Serializable protects duplicate creation, eligibility and capacity checks across API instances.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            throw new ProjectVerificationException("VERIFICATION_CONFLICT", "Verification data changed concurrently. Reload and try again.", 409);
        }
    }

    private static bool IsWriteConflict(Exception ex) => EvidenceModerationSource.IsWriteConflict(ex);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new ProjectVerificationException(code, message, status);
    }
}
