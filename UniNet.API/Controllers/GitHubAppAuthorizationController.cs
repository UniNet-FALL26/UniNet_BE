using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController]
public sealed class GitHubAppAuthorizationController(IGitHubAppAuthorizationService service, IGitHubAppGrantService grants, IGitHubAppRepositoryLinkService links) : ControllerBase
{
    [Authorize(Roles = "Student"), HttpPost("api/projects/{projectId:guid}/git/app/authorization/start")]
    public Task<StartGitHubAppAuthorizationResponse> Start(Guid projectId, StartGitHubAppAuthorizationRequest request, CancellationToken ct) =>
        service.StartAsync(AccountId, projectId, request, ct);

    [AllowAnonymous, HttpGet("api/github/app/authorization/callback")]
    public async Task<IActionResult> Callback([FromQuery] string? state, [FromQuery] string? code,
        [FromQuery] string? error, CancellationToken ct)
    {
        var result = await service.CallbackAsync(state, code, error, ct);
        return Redirect(result.RedirectUri.AbsoluteUri);
    }

    [Authorize(Roles = "Student"), HttpGet("api/projects/{projectId:guid}/git/app/credentials")]
    public Task<IReadOnlyList<GitHubAppCredentialItem>> Credentials(Guid projectId, CancellationToken ct) => grants.GetCredentialsAsync(AccountId, projectId, ct);

    [Authorize(Roles = "Student"), HttpGet("api/projects/{projectId:guid}/git/app/installations")]
    public Task<GitHubAppGrantPage<GitHubAppInstallationItem>> Installations(Guid projectId, [FromQuery] Guid credentialId,
        [FromQuery] int page = 1, CancellationToken ct = default) => grants.GetInstallationsAsync(AccountId, projectId, credentialId, page, ct);

    [Authorize(Roles = "Student"), HttpGet("api/projects/{projectId:guid}/git/app/installations/{installationId:long}/repositories")]
    public Task<GitHubAppGrantPage<GitHubAppRepositoryItem>> Repositories(Guid projectId, long installationId, [FromQuery] Guid credentialId,
        [FromQuery] int page = 1, CancellationToken ct = default) => grants.GetRepositoriesAsync(AccountId, projectId, credentialId, installationId, page, ct);

    [Authorize(Roles = "Student"), HttpPost("api/projects/{projectId:guid}/git/app/repositories/link")]
    public Task<GitHubAppRepositoryLinkResponse> Link(Guid projectId, LinkGitHubAppRepositoryRequest request, CancellationToken ct) =>
        links.LinkAsync(AccountId, projectId, request, ct);

    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id :
        throw new GitHubIntegrationException("UNAUTHORIZED", "Invalid login session.", 401);
}
