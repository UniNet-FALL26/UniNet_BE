using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/contribution/git-experimental/peer-cohorts")]
public sealed class ExperimentalPeerCohortController(ExperimentalPeerCohortService peers) : ControllerBase
{
    [HttpPost]
    public Task<ExperimentalPeerCohortResponse> Open(Guid projectId, ExperimentalPeerOpenRequest request, CancellationToken ct) => peers.OpenAsync(projectId, AccountId, request, ct);
    [HttpGet("{cohortId:guid}")]
    public Task<ExperimentalPeerCohortResponse> Get(Guid projectId, Guid cohortId, CancellationToken ct) => peers.ReadAsync(projectId, cohortId, AccountId, ct);
    [HttpPut("{cohortId:guid}/evaluations/{evaluateeId:guid}")]
    public Task<ExperimentalPeerCohortResponse> Submit(Guid projectId, Guid cohortId, Guid evaluateeId, ExperimentalPeerSubmitRequest request, CancellationToken ct) => peers.SubmitAsync(projectId, cohortId, AccountId, evaluateeId, request, ct);
    [HttpPost("{cohortId:guid}/close")]
    public async Task<ActionResult<object>> Close(Guid projectId, Guid cohortId, CancellationToken ct) => Ok(new { PeerSnapshotId = await peers.CloseAsync(projectId, cohortId, AccountId, ct) });
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);
}
