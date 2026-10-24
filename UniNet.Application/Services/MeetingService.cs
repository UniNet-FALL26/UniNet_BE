using System.Data;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class MeetingService(UniNetDbContext db, IConfiguration configuration) : IMeetingService
{
    public async Task<MeetingResponse> GetAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default)
    {
        var assignment = await GetAuthorizedAsync(assignmentId, accountId, false, ct);
        return ToResponse(assignment);
    }

    public async Task<MeetingJoinResponse> JoinAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default)
    {
        var assignment = await GetAuthorizedAsync(assignmentId, accountId, false, ct);
        var verification = assignment.ProjectVerification;
        Require(verification.MeetingEndAt == null, "INVALID_MEETING_STATE", "The meeting has already ended.", 409);
        Require(verification.MeetingRoomName != null, "MEETING_ROOM_REQUIRED", "Create the meeting room before joining it.", 409);
        var meeting = ToResponse(assignment);
        return new(assignment.Id, verification.Id, meeting.JoinUrl!);
    }

    public Task<MeetingResponse> GetOrCreateRoomAsync(Guid assignmentId, Guid accountId,
        CancellationToken ct = default) => MutateAsync(assignmentId, accountId, false, assignment =>
    {
        var verification = assignment.ProjectVerification;
        Require(verification.MeetingEndAt == null, "INVALID_MEETING_STATE", "The meeting has already ended.", 409);
        if (verification.MeetingRoomName == null)
        {
            verification.MeetingRoomName = "uninet-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            verification.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }, ct);

    public Task<MeetingResponse> StartAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default) =>
        MutateAsync(assignmentId, accountId, true, assignment =>
        {
            Require(assignment.ProjectVerification.MeetingRoomName != null,
                "MEETING_ROOM_REQUIRED", "Create the meeting room before starting it.", 409);
            VerificationMeetingLifecycle.Start(assignment, DateTimeOffset.UtcNow, allowRepeated: true);
        }, ct);

    public Task<MeetingResponse> EndAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default) =>
        MutateAsync(assignmentId, accountId, true,
            assignment => VerificationMeetingLifecycle.End(assignment, DateTimeOffset.UtcNow, allowRepeated: true), ct,
            requireJitsiConfiguration: false);

    private async Task<VerificationAssignment> GetAuthorizedAsync(Guid assignmentId, Guid accountId,
        bool verificatorOnly, CancellationToken ct)
    {
        var user = await db.UserProfiles.Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new MeetingException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(user.Account.Status == AccountStatus.Active, "FORBIDDEN", "An active account is required.", 403);
        var assignment = await db.VerificationAssignments.Include(a => a.ProjectVerification).ThenInclude(v => v.Project)
            .Include(a => a.ReplacedBy).FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw new MeetingException("ASSIGNMENT_NOT_FOUND", "Assignment not found.", 404);
        var assigned = user.Account.Role == AccountRole.Verificator && assignment.AssignedToUserId == user.Id;
        var leader = !verificatorOnly && (assignment.ProjectVerification.Project.CreatorId == user.Id ||
            await db.ProjectMembers.AnyAsync(m =>
            m.ProjectId == assignment.ProjectVerification.ProjectId && m.UserId == user.Id &&
            m.Status == ProjectMemberStatus.Active && m.LeftAt == null && m.Role == "Leader", ct));
        Require(assigned || leader, "FORBIDDEN", "Only the project Leader or assigned Verificator can access this meeting.", 403);
        try { VerificationMeetingLifecycle.RequireActive(assignment); }
        catch (AssignmentException ex) { throw new MeetingException(ex.Code, ex.Message, ex.Status); }
        var verification = assignment.ProjectVerification;
        Require(verification.BookingStatus == VerificationBookingStatus.Confirmed && verification.BookingConfirmedAt.HasValue,
            "BOOKING_NOT_CONFIRMED", "A confirmed booking is required.", 409);
        Require(verification.ScheduledStartAt.HasValue && verification.ScheduledEndAt > verification.ScheduledStartAt,
            "INVALID_VERIFICATION_SCHEDULE", "A valid meeting schedule is required.", 409);
        return assignment;
    }

    private MeetingResponse ToResponse(VerificationAssignment assignment)
    {
        var verification = assignment.ProjectVerification;
        // Finished meetings retain metadata, but no longer advertise a join URL.
        var joinUrl = verification.MeetingRoomName != null && verification.MeetingEndAt == null
            ? new Uri(RequireBaseUri(), Uri.EscapeDataString(verification.MeetingRoomName)).AbsoluteUri : null;
        return new(assignment.Id, verification.Id, verification.MeetingRoomName, joinUrl,
            verification.ScheduledStartAt, verification.ScheduledEndAt, verification.MeetingStartAt, verification.MeetingEndAt);
    }

    private Uri RequireBaseUri()
    {
        var value = configuration["Jitsi:BaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            throw new MeetingException("JITSI_NOT_CONFIGURED", "Configure Jitsi:BaseUrl as an HTTPS origin without credentials, query or fragment.", 503);
        return uri;
    }

    private async Task<MeetingResponse> MutateAsync(Guid assignmentId, Guid accountId, bool verificatorOnly,
        Action<VerificationAssignment> action, CancellationToken ct, bool requireJitsiConfiguration = true)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var assignment = await GetAuthorizedAsync(assignmentId, accountId, verificatorOnly, ct);
            if (requireJitsiConfiguration) RequireBaseUri();
            action(assignment);
            var result = ToResponse(assignment);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (AssignmentException ex) { throw new MeetingException(ex.Code, ex.Message, ex.Status); }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } })
        {
            throw new MeetingException("MEETING_CONFLICT", "Meeting data changed concurrently. Reload and try again.", 409);
        }
    }

    private static void Require(bool condition, string code, string message, int status)
    {
        if (!condition) throw new MeetingException(code, message, status);
    }
}
