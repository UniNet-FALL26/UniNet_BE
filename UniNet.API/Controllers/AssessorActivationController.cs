using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, AllowAnonymous, Route("api/assessor-onboarding/password")]
[EnableRateLimiting("AssessorContracts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssessorActivationController(AssessorActivationService activation) : ControllerBase
{
    [HttpPost("inspect")]
    public async Task<IActionResult> Inspect(ContractCredential request, CancellationToken ct)
        => Ok(await activation.InspectPassword(request.Token, ct));

    [HttpPost]
    public async Task<IActionResult> SetPassword(SetAssessorPassword request, CancellationToken ct)
    {
        await activation.SetPassword(request, ct);
        return Ok(new { status = "Activated" });
    }

    [HttpPost("resend")]
    public async Task<IActionResult> Resend(ContractCredential request, CancellationToken ct)
    {
        await activation.ResendPassword(request.Token, ct);
        return Accepted();
    }
}
