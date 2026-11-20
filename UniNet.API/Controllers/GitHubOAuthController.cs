using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController, Route("api/github")]
public sealed class GitHubOAuthController(IGitHubOAuthService oauth) : ControllerBase
{
    [Authorize, HttpPost("oauth/start")]
    public Task<GitHubOAuthStartResponse> Start(
        StartGitHubOAuthRequest request, CancellationToken ct) =>
        oauth.StartAsync(AccountId, request, ct);

    [AllowAnonymous, HttpGet("oauth/callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        var result = await oauth.ProcessCallbackAsync(code, state, error, ct);
        return Redirect(result.RedirectUri.AbsoluteUri);
    }

    [Authorize, HttpPost("oauth/complete")]
    public Task<GitHubOAuthConnectionResponse> Complete(
        CompleteGitHubOAuthRequest request, CancellationToken ct) =>
        oauth.CompleteAsync(AccountId, request, ct);

    [Authorize, HttpGet("connections")]
    public Task<IReadOnlyList<GitHubOAuthConnectionResponse>> GetConnections(CancellationToken ct) =>
        oauth.GetConnectionsAsync(AccountId, ct);

    [Authorize, HttpDelete("connections/{connectionId:guid}")]
    public async Task<IActionResult> Disconnect(Guid connectionId, CancellationToken ct)
    {
        await oauth.DisconnectAsync(AccountId, connectionId, ct);
        return NoContent();
    }

    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new UniNet.Application.Common.GitHubIntegrationException(
            "UNAUTHORIZED", "Invalid login session.", 401);
}
