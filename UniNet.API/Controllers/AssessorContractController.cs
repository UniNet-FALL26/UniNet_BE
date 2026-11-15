using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, AllowAnonymous, Route("api/assessor-onboarding/contract")]
[EnableRateLimiting("AssessorContracts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssessorContractController(AssessorContractService contracts) : ControllerBase
{
    [HttpPost("read-otp")]
    public async Task<IActionResult> RequestReadOtp(ContractCredential request, CancellationToken ct)
    {
        await contracts.RequestReadOtp(request.Token, ct);
        return Accepted();
    }

    [HttpPost("verify-read-otp")]
    public async Task<IActionResult> VerifyReadOtp(VerifyContractOtp request, CancellationToken ct)
        => Ok(new { token = await contracts.VerifyReadOtp(request.Token, request.Code, ct) });

    [HttpPost("inspect")]
    public async Task<IActionResult> Inspect(ContractCredential request, CancellationToken ct)
        => Ok(await contracts.Inspect(request.Token, ct));

    [HttpPost("pdf")]
    public async Task<IActionResult> Pdf(ContractCredential request, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(await contracts.Pdf(request.Token, ct), "application/pdf");
    }

    [HttpPost("sign-otp")]
    public async Task<IActionResult> RequestSignOtp(ContractCredential request, CancellationToken ct)
    {
        await contracts.RequestSignOtp(request.Token, ct);
        return Accepted();
    }

    [HttpPost("sign")]
    public async Task<IActionResult> Sign(SignAssessorContract request, CancellationToken ct)
    {
        await contracts.Sign(request, ct);
        return Ok(new { status = "AwaitingApproval" });
    }
}
