using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/contribution/v2")]
public sealed class OfficialContributionController(OfficialContributionService service) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);
    [HttpGet("workflow")]
    public Task<OfficialWorkflowResponse> Workflow(Guid projectId, CancellationToken ct) => service.StatusAsync(projectId, AccountId, ct);
    [HttpPost("snapshots")]
    public async Task<object> Capture(Guid projectId, OfficialSnapshotRequest request, CancellationToken ct) =>
        new { contractVersion = "1", snapshotId = await service.CaptureAsync(projectId, AccountId, request, ct) };
    [HttpGet("snapshots/{snapshotId:guid}")]
    public Task<OfficialSnapshotResponse> Snapshot(Guid projectId, Guid snapshotId, CancellationToken ct) => service.SnapshotAsync(projectId, snapshotId, AccountId, ct);
    [HttpGet("snapshots/{snapshotId:guid}/memberships")]
    public Task<OfficialPage<OfficialSnapshotMember>> Membership(Guid projectId, Guid snapshotId, CancellationToken ct, int page = 1, int pageSize = 20, string? category = null) => service.MembershipAsync(projectId, snapshotId, AccountId, page, pageSize, category, ct);
    [HttpGet("snapshots/{snapshotId:guid}/readiness")]
    public Task<OfficialReadinessResponse> Readiness(Guid projectId, Guid snapshotId, CancellationToken ct) => service.ReadinessAsync(projectId, snapshotId, AccountId, ct);
    [HttpPost("peer-cohorts")]
    public Task<ExperimentalPeerCohortResponse> OpenPeer(Guid projectId, ExperimentalPeerOpenRequest request, CancellationToken ct) => service.OpenPeerAsync(projectId, AccountId, request, ct);
    [HttpGet("peer-cohorts/{cohortId:guid}")]
    public Task<ExperimentalPeerCohortResponse> Peer(Guid projectId, Guid cohortId, CancellationToken ct) => service.PeerAsync(projectId, cohortId, AccountId, ct);
    [HttpPut("peer-cohorts/{cohortId:guid}/evaluations/{evaluateeId:guid}")]
    public Task<ExperimentalPeerCohortResponse> SubmitPeer(Guid projectId, Guid cohortId, Guid evaluateeId, ExperimentalPeerSubmitRequest request, CancellationToken ct) => service.SubmitPeerAsync(projectId, cohortId, evaluateeId, AccountId, request, ct);
    [HttpPost("peer-cohorts/{cohortId:guid}/close")]
    public async Task<object> ClosePeer(Guid projectId, Guid cohortId, CancellationToken ct) => new { contractVersion = "1", peerSnapshotId = await service.ClosePeerAsync(projectId, cohortId, AccountId, ct) };
    [HttpGet("peer-snapshots/{snapshotId:guid}")]
    public Task<OfficialPeerSnapshotResponse> PeerSnapshot(Guid projectId, Guid snapshotId, CancellationToken ct) => service.PeerSnapshotAsync(projectId, snapshotId, AccountId, ct);
    [HttpPost("runs")]
    public Task<OfficialRunResponse> Create(Guid projectId, OfficialRunRequest request, CancellationToken ct) => service.CreateAsync(projectId, AccountId, request, ct);
    [HttpGet("runs")]
    public Task<OfficialPage<OfficialRunListItem>> List(Guid projectId, CancellationToken ct, int page = 1, int pageSize = 20, string? state = null) => service.ListAsync(projectId, AccountId, page, pageSize, state, ct);
    [HttpGet("runs/{runId:guid}")]
    public Task<OfficialRunResponse> Run(Guid projectId, Guid runId, CancellationToken ct) => service.GetAsync(projectId, runId, AccountId, ct);
    [HttpPost("runs/{runId:guid}/submit")]
    public Task<OfficialRunResponse> Submit(Guid projectId, Guid runId, OfficialTransitionRequest request, CancellationToken ct) => service.TransitionAsync(projectId, runId, AccountId, "submit", request, ct);
    [HttpPost("runs/{runId:guid}/approve")]
    public Task<OfficialRunResponse> Approve(Guid projectId, Guid runId, OfficialTransitionRequest request, CancellationToken ct) => service.TransitionAsync(projectId, runId, AccountId, "approve", request, ct);
    [HttpPost("runs/{runId:guid}/reject")]
    public Task<OfficialRunResponse> Reject(Guid projectId, Guid runId, OfficialTransitionRequest request, CancellationToken ct) => service.TransitionAsync(projectId, runId, AccountId, "reject", request, ct);
    [HttpPost("runs/{runId:guid}/finalize")]
    public Task<OfficialRunResponse> FinalizeRun(Guid projectId, Guid runId, OfficialTransitionRequest request, CancellationToken ct) => service.TransitionAsync(projectId, runId, AccountId, "finalize", request, ct);
    [HttpPost("runs/{runId:guid}/publish")]
    public Task<OfficialRunResponse> Publish(Guid projectId, Guid runId, OfficialTransitionRequest request, CancellationToken ct) => service.TransitionAsync(projectId, runId, AccountId, "publish", request, ct);
    [HttpGet("runs/{runId:guid}/audit")]
    public Task<OfficialPage<OfficialAuditResponse>> Audit(Guid projectId, Guid runId, CancellationToken ct, int page = 1, int pageSize = 20) => service.AuditAsync(projectId, runId, AccountId, page, pageSize, ct);
    [HttpGet("runs/{runId:guid}/exclusions")]
    public Task<OfficialPage<GitExperimentalExcludedActivityResponse>> Exclusions(Guid projectId, Guid runId, CancellationToken ct, int page = 1, int pageSize = 20, string? category = null) => service.ExclusionsAsync(projectId, runId, AccountId, page, pageSize, category, ct);
    [HttpGet("runs/{runId:guid}/activities")]
    public Task<OfficialPage<OfficialScoredActivity>> Activities(Guid projectId, Guid runId, CancellationToken ct, int page = 1, int pageSize = 20, string? category = null) => service.ScoredAsync(projectId, runId, AccountId, page, pageSize, category, ct);
    [HttpGet("results/me")]
    public Task<OfficialMemberResultResponse> MyResult(Guid projectId, CancellationToken ct) => service.MyResultAsync(projectId, AccountId, ct);
}
