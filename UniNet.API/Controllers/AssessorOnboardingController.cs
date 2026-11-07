using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/assessor-onboardings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAssessorOnboardingController(AssessorOnboardingService onboarding, AssessorContractService contracts) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpPost("{id:guid}/resend-contract")]
    public async Task<IActionResult> ResendContract(Guid id, CancellationToken ct)
    {
        await contracts.Resend(Actor, id, ct);
        return Accepted();
    }

    [HttpPost("invitations")]
    public async Task<ActionResult<AssessorInvitation>> Invite(CreateAssessorInvitation request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await onboarding.Invite(Actor, request, ct));

    [HttpPost("invitations/{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await onboarding.Revoke(Actor, id, ct);
        return NoContent();
    }
}

[ApiController, AllowAnonymous, Route("api/assessor-onboarding/profile")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssessorOnboardingProfileController(AssessorOnboardingService onboarding) : ControllerBase
{
    [HttpPost("inspect")]
    public async Task<IActionResult> Inspect(AssessorProfileLink request, CancellationToken ct)
        => Ok(new { email = await onboarding.ReadForm(request.Token, ct) });

    [HttpPost]
    public async Task<IActionResult> Submit(SubmitAssessorOnboardingProfile request, CancellationToken ct)
        => Ok(new { id = await onboarding.Submit(request, ct), status = "ProfileSubmitted" });
}
