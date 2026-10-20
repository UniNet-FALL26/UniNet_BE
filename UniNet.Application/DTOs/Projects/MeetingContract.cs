namespace UniNet.Application;

public record MeetingJoinResponse(Guid AssignmentId, Guid ProjectVerificationId, string JoinUrl);

public record MeetingResponse(Guid AssignmentId, Guid ProjectVerificationId, string? RoomName,
    string? JoinUrl, DateTimeOffset? ScheduledStartAt, DateTimeOffset? ScheduledEndAt,
    DateTimeOffset? MeetingStartAt, DateTimeOffset? MeetingEndAt);
