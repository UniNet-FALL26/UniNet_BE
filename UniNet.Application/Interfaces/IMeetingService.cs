namespace UniNet.Application.Interfaces;

public interface IMeetingService
{
    // accountId must come from the authenticated principal, never from a request body.
    Task<MeetingResponse> GetAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default);
    Task<MeetingJoinResponse> JoinAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default);
    Task<MeetingResponse> GetOrCreateRoomAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default);
    Task<MeetingResponse> StartAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default);
    Task<MeetingResponse> EndAsync(Guid assignmentId, Guid accountId, CancellationToken ct = default);
}
