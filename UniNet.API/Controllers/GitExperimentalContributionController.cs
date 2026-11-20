using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"),
 Route("api/projects/{projectId:guid}/contribution/git-experimental")]
public sealed class GitExperimentalContributionController(IGitExperimentalContributionService contributions) : ControllerBase
{
    [HttpPost("runs")]
    public Task<GitExperimentalRunResponse> CreateRun(
        Guid projectId, GitExperimentalRunRequest request, CancellationToken ct)
        => contributions.CreateRunAsync(projectId, AccountId, request, ct);

    [HttpGet("runs/{runId:guid}")]
    public Task<GitExperimentalRunResponse> GetRun(Guid projectId, Guid runId, CancellationToken ct)
        => contributions.GetRunAsync(projectId, runId, AccountId, ct);

    [HttpPost("runs/{runId:guid}/review")]
    public Task<GitExperimentalRunResponse> ReviewRun(
        Guid projectId, Guid runId, GitExperimentalReviewRequest request, CancellationToken ct)
        => contributions.ReviewRunAsync(projectId, runId, AccountId, request, ct);

    [HttpPost("runs/{runId:guid}/finalize")]
    public Task<GitExperimentalRunResponse> FinalizeRun(Guid projectId, Guid runId, CancellationToken ct)
        => contributions.FinalizeRunAsync(projectId, runId, AccountId, ct);

    [HttpGet("results")]
    public Task<GitExperimentalFinalResultsResponse> GetFinalResults(Guid projectId, CancellationToken ct)
        => contributions.GetFinalResultsAsync(projectId, AccountId, ct);

    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);
}
