using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/git")]
public sealed class ProjectGitController(
    IProjectGitManagerService managers,
    IProjectGitRepositoryService repositories,
    IProjectGitIdentityService identities,
    IGitSyncJobService syncJobs,
    IProjectGitActivityService activities) : ControllerBase
{
    [HttpGet("activities")]
    public Task<GitActivityPage> GetActivities(
        Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? activityType = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken ct = default) =>
        activities.GetAsync(AccountId, projectId, page, pageSize, activityType, from, to, ct);

    [HttpGet("sync-status")]
    public Task<ProjectGitSyncStatusPage> GetSyncStatus(Guid projectId, CancellationToken ct) =>
        syncJobs.GetProjectStatusAsync(AccountId, projectId, ct);

    [HttpGet("repositories/{linkId:guid}/sync-jobs")]
    public Task<GitSyncJobPage> GetSyncJobs(
        Guid projectId,
        Guid linkId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        syncJobs.GetJobsAsync(AccountId, projectId, linkId, page, pageSize, ct);

    [HttpPost("repositories/{linkId:guid}/sync")]
    public async Task<IActionResult> RequestSync(Guid projectId, Guid linkId, CancellationToken ct)
    {
        var result = await syncJobs.RequestManualSyncAsync(AccountId, projectId, linkId, ct);
        return Accepted(result);
    }

    [HttpPatch("repositories/{linkId:guid}/sync-settings")]
    public Task<ProjectGitSyncStatusResponse> UpdateSyncSettings(
        Guid projectId,
        Guid linkId,
        GitSyncSettingsRequest request,
        CancellationToken ct) =>
        syncJobs.UpdateSyncSettingsAsync(AccountId, projectId, linkId, request, ct);

    [HttpGet("managers")]
    public Task<IReadOnlyList<ProjectGitManagerResponse>> GetManagers(Guid projectId, CancellationToken ct) =>
        managers.GetAsync(projectId, AccountId, ct);

    [HttpPost("managers")]
    public Task<ProjectGitManagerResponse> GrantManager(
        Guid projectId, GrantProjectGitManagerRequest request, CancellationToken ct) =>
        managers.GrantAsync(projectId, AccountId, request, ct);

    [HttpDelete("managers/{userProfileId:guid}")]
    public async Task<IActionResult> RevokeManager(Guid projectId, Guid userProfileId, CancellationToken ct)
    {
        await managers.RevokeAsync(projectId, userProfileId, AccountId, ct);
        return NoContent();
    }

    [HttpGet("repositories/discover")]
    public Task<GitHubRepositoryPage> DiscoverRepositories(
        Guid projectId,
        [FromQuery] Guid connectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        repositories.DiscoverAsync(AccountId, projectId, connectionId, page, pageSize, ct);

    [HttpGet("repositories")]
    public Task<ProjectGitRepositoryPage> GetRepositories(
        Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        repositories.GetAsync(AccountId, projectId, page, pageSize, ct);

    [HttpPost("repositories")]
    public async Task<IActionResult> LinkRepository(
        Guid projectId, LinkGitHubRepositoryRequest request, CancellationToken ct)
    {
        var result = await repositories.LinkAsync(AccountId, projectId, request, ct);
        return CreatedAtAction(nameof(GetRepository), new { projectId, linkId = result.Id }, result);
    }

    [HttpGet("repositories/{linkId:guid}")]
    public Task<ProjectGitRepositoryResponse> GetRepository(Guid projectId, Guid linkId, CancellationToken ct) =>
        repositories.GetAsync(AccountId, projectId, linkId, ct);

    [HttpPut("repositories/{linkId:guid}/credential")]
    public Task<ProjectGitRepositoryResponse> ReplaceCredential(
        Guid projectId,
        Guid linkId,
        ReplaceGitHubRepositoryCredentialRequest request,
        CancellationToken ct) =>
        repositories.ReplaceCredentialAsync(AccountId, projectId, linkId, request, ct);

    [HttpDelete("repositories/{linkId:guid}")]
    public async Task<IActionResult> UnlinkRepository(Guid projectId, Guid linkId, CancellationToken ct)
    {
        await repositories.UnlinkAsync(AccountId, projectId, linkId, ct);
        return NoContent();
    }

    [HttpGet("identities")]
    public Task<ProjectGitMemberIdentityPage> GetIdentities(
        Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? userProfileId = null,
        [FromQuery] ProjectGitMemberIdentityStatus? status = null,
        CancellationToken ct = default) =>
        identities.GetAsync(AccountId, projectId, page, pageSize, userProfileId, status, ct);

    [HttpPost("identities")]
    public async Task<IActionResult> LinkIdentity(
        Guid projectId, LinkProjectGitIdentityRequest request, CancellationToken ct)
    {
        var result = await identities.LinkAsync(AccountId, projectId, request, ct);
        return CreatedAtAction(nameof(GetIdentity), new { projectId, identityId = result.Id }, result);
    }

    [HttpGet("identities/{identityId:guid}")]
    public Task<ProjectGitMemberIdentityResponse> GetIdentity(
        Guid projectId, Guid identityId, CancellationToken ct) =>
        identities.GetAsync(AccountId, projectId, identityId, ct);

    [HttpDelete("identities/{identityId:guid}")]
    public Task<ProjectGitMemberIdentityResponse> UnlinkIdentity(
        Guid projectId, Guid identityId, CancellationToken ct) =>
        identities.UnlinkAsync(AccountId, projectId, identityId, ct);

    [HttpPost("identities/{identityId:guid}/reactivate")]
    public Task<ProjectGitMemberIdentityResponse> ReactivateIdentity(
        Guid projectId, Guid identityId, ReactivateProjectGitIdentityRequest request, CancellationToken ct) =>
        identities.ReactivateAsync(AccountId, projectId, identityId, request, ct);

    [HttpGet("identities/{identityId:guid}/history")]
    public Task<ProjectGitMemberIdentityHistoryPage> GetIdentityHistory(
        Guid projectId,
        Guid identityId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default) =>
        identities.GetHistoryAsync(AccountId, projectId, identityId, page, pageSize, ct);

    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new GitHubIntegrationException("UNAUTHORIZED", "Invalid login session.", 401);
}
