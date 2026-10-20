using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController, Authorize]
public sealed class MeetingController(IMeetingService meetings) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        && id != Guid.Empty ? id : throw new MeetingException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpGet("api/assignments/{assignmentId:guid}/meeting")]
    public Task<MeetingResponse> Get(Guid assignmentId, CancellationToken ct)
        => meetings.GetAsync(assignmentId, AccountId, ct);

    [HttpPost("api/assignments/{assignmentId:guid}/meeting")]
    public Task<MeetingResponse> GetOrCreateRoom(Guid assignmentId, CancellationToken ct)
        => meetings.GetOrCreateRoomAsync(assignmentId, AccountId, ct);

    [HttpPost("api/assignments/{assignmentId:guid}/meeting/join")]
    public Task<MeetingJoinResponse> Join(Guid assignmentId, CancellationToken ct)
        => meetings.JoinAsync(assignmentId, AccountId, ct);

    [HttpPost("api/verificator/assignments/{assignmentId:guid}/meeting/start"), Authorize(Roles = "Verificator")]
    public Task<MeetingResponse> Start(Guid assignmentId, CancellationToken ct)
        => meetings.StartAsync(assignmentId, AccountId, ct);

    [HttpPost("api/verificator/assignments/{assignmentId:guid}/meeting/end"), Authorize(Roles = "Verificator")]
    public Task<MeetingResponse> End(Guid assignmentId, CancellationToken ct)
        => meetings.EndAsync(assignmentId, AccountId, ct);
}
