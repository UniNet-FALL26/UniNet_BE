using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/git/repositories/{linkId:guid}/sync-jobs")]
public sealed class GitHubAppInitialSyncRecoveryController(GitHubAppInitialSyncRecoveryService recovery) : ControllerBase
{
    [HttpPost("{jobId:guid}/recover-initial")]
    public async Task<IActionResult> Recover(Guid projectId, Guid linkId, Guid jobId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var actor)) return Unauthorized();
        return Accepted(await recovery.RecoverAsync(actor, projectId, linkId, jobId, ct));
    }
}
