using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

internal static class VerificationMeetingLifecycle
{
    internal static void RequireActive(VerificationAssignment assignment)
    {
        Require(assignment.Status is VerificationAssignmentStatus.Assigned or VerificationAssignmentStatus.Overdue &&
            assignment.ReplacedAt == null && assignment.ReplacedBy == null,
            "ASSIGNMENT_LOCKED", "Only active, unreplaced assignments can be changed.");
        var verification = assignment.ProjectVerification;
        Require(verification.Status == ProjectVerificationStatus.Pending && verification.FinalizedAt == null,
            "VERIFICATION_LOCKED", "Only pending verifications can be updated.");
    }

    internal static void Start(VerificationAssignment assignment, DateTimeOffset now, bool allowRepeated = false)
    {
        RequireActive(assignment);
        var verification = assignment.ProjectVerification;
        Require(verification.MeetingEndAt == null, "INVALID_MEETING_STATE", "The meeting has already ended.");
        if (allowRepeated && verification.MeetingStartAt.HasValue) return;
        Require(verification.MeetingStartAt == null && verification.ScheduledStartAt.HasValue &&
            verification.ScheduledStartAt <= now, "INVALID_MEETING_STATE",
            "The meeting must be scheduled and not already started.");
        verification.MeetingStartAt = now;
        verification.UpdatedAt = assignment.UpdatedAt = now;
    }

    internal static void End(VerificationAssignment assignment, DateTimeOffset now, bool allowRepeated = false)
    {
        RequireActive(assignment);
        var verification = assignment.ProjectVerification;
        Require(verification.MeetingStartAt.HasValue, "INVALID_MEETING_STATE", "Start the meeting before ending it.");
        if (allowRepeated && verification.MeetingEndAt.HasValue) return;
        Require(verification.MeetingEndAt == null, "INVALID_MEETING_STATE", "The meeting has already ended.");
        Require(now >= verification.MeetingStartAt!.Value, "CLOCK_CONFLICT", "Server time precedes meeting start.");
        verification.MeetingEndAt = now;
        assignment.ReportDueAt = now.AddHours(48);
        verification.UpdatedAt = assignment.UpdatedAt = now;
    }

    private static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new AssignmentException(code, message, 409);
    }
}
