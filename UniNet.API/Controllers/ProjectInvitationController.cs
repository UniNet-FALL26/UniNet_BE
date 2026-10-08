using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects")]
public sealed class ProjectInvitationController(IProjectInvitationService invitations) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new ProjectInvitationException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpPost("{projectId:guid}/invitations")]
    public async Task<ActionResult<ProjectInvitationResponse>> Send(
        Guid projectId, CreateProjectInvitationRequest request, CancellationToken ct)
    {
        var result = await invitations.SendAsync(projectId, AccountId, request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{projectId:guid}/invitations")]
    public Task<ProjectInvitationsResponse> GetProjectInvitations(
        Guid projectId,
        [FromQuery] ProjectInvitationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => invitations.GetProjectInvitationsAsync(projectId, AccountId, status, page, pageSize, ct);

    [HttpGet("my/invitations")]
    public Task<ProjectInvitationsResponse> GetMyInvitations(
        [FromQuery] ProjectInvitationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => invitations.GetMyInvitationsAsync(AccountId, status, page, pageSize, ct);

    [HttpPatch("invitations/{invitationId:guid}/cancel")]
    public Task<ProjectInvitationResponse> Cancel(Guid invitationId, CancellationToken ct)
        => invitations.CancelAsync(invitationId, AccountId, ct);

    [HttpPatch("invitations/{invitationId:guid}/decline")]
    public Task<ProjectInvitationResponse> Decline(Guid invitationId, CancellationToken ct)
        => invitations.DeclineAsync(invitationId, AccountId, ct);

    [HttpPatch("invitations/{invitationId:guid}/accept")]
    public Task<ProjectInvitationResponse> Accept(Guid invitationId, CancellationToken ct)
        => invitations.AcceptAsync(invitationId, AccountId, ct);
}
