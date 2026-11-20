using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/github/app/deliveries"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GitHubAppWebhookRecoveryController(GitHubAppWebhookRecoveryService recovery) : ControllerBase
{
    public sealed record RequeueRequest(string Reason);
    [HttpPost("{deliveryId}/requeue")]
    public async Task<IActionResult> Requeue(string deliveryId, RequeueRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var actor)) return Unauthorized();
        await recovery.RequeueAsync(actor, deliveryId, request.Reason, ct);
        return NoContent();
    }
}
