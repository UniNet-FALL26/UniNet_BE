using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/contribution")]
public sealed class ContributionController(IContributionService contributions) : ControllerBase
{
    [HttpGet]
    public Task<ContributionCycleResponse> GetCycle(Guid projectId, CancellationToken ct)
        => contributions.GetCycleAsync(projectId, AccountId, ct);

    [HttpGet("peer-evaluations/me")]
    public Task<IReadOnlyList<ContributionPeerEvaluationTaskResponse>> GetMyPeerEvaluations(
        Guid projectId, CancellationToken ct)
        => contributions.GetMyPeerEvaluationsAsync(projectId, AccountId, ct);

    [HttpPut("peer-evaluations/{evaluateeUserId:guid}")]
    public Task<ContributionPeerEvaluationResponse> UpsertPeerEvaluation(
        Guid projectId,
        Guid evaluateeUserId,
        ContributionPeerEvaluationRequest request,
        CancellationToken ct)
        => contributions.UpsertPeerEvaluationAsync(projectId, evaluateeUserId, AccountId, request, ct);

    [HttpPost("calculations")]
    public Task<ContributionRunResponse> Calculate(Guid projectId, CancellationToken ct)
        => contributions.CalculateAsync(projectId, AccountId, ct);

    [HttpGet("calculations/{runId:guid}")]
    public Task<ContributionRunResponse> GetRun(Guid projectId, Guid runId, CancellationToken ct)
        => contributions.GetRunAsync(projectId, runId, AccountId, ct);

    [HttpPost("calculations/{runId:guid}/review")]
    public Task<ContributionReviewResponse> ReviewRun(
        Guid projectId,
        Guid runId,
        ContributionReviewRequest request,
        CancellationToken ct)
        => contributions.ReviewRunAsync(projectId, runId, AccountId, request, ct);

    [HttpGet("results")]
    public Task<ContributionFinalResultsResponse> GetFinalResults(Guid projectId, CancellationToken ct)
        => contributions.GetFinalResultsAsync(projectId, AccountId, ct);

    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);
}
