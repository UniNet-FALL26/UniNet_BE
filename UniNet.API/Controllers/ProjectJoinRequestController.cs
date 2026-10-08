using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects")]
public sealed class ProjectJoinRequestController(IProjectJoinRequestService joinRequests) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new ProjectJoinRequestException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpPost("{projectId:guid}/join-requests")]
    public async Task<ActionResult<ProjectJoinRequestResponse>> Send(
        Guid projectId, CreateProjectJoinRequestRequest request, CancellationToken ct)
    {
        var result = await joinRequests.SendAsync(projectId, AccountId, request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{projectId:guid}/join-requests")]
    public Task<ProjectJoinRequestsResponse> GetProjectRequests(
        Guid projectId,
        [FromQuery] ProjectJoinRequestStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => joinRequests.GetProjectRequestsAsync(projectId, AccountId, status, page, pageSize, ct);

    [HttpGet("my/join-requests")]
    public Task<ProjectJoinRequestsResponse> GetMyRequests(
        [FromQuery] ProjectJoinRequestStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => joinRequests.GetMyRequestsAsync(AccountId, status, page, pageSize, ct);

    [HttpPatch("join-requests/{requestId:guid}/cancel")]
    public Task<ProjectJoinRequestResponse> Cancel(Guid requestId, CancellationToken ct)
        => joinRequests.CancelAsync(requestId, AccountId, ct);

    [HttpPatch("join-requests/{requestId:guid}/accept")]
    public Task<ProjectJoinRequestResponse> Accept(Guid requestId, CancellationToken ct)
        => joinRequests.AcceptAsync(requestId, AccountId, ct);

    [HttpPatch("join-requests/{requestId:guid}/reject")]
    public Task<ProjectJoinRequestResponse> Reject(Guid requestId, CancellationToken ct)
        => joinRequests.RejectAsync(requestId, AccountId, ct);
}
