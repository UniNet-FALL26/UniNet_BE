using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/assessor-onboardings")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAssessorOnboardingController(
    AssessorOnboardingService onboarding, AssessorContractService contracts, AssessorActivationService activation,
    IAssessorContractFiles files) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Review(Guid id, CancellationToken ct)
        => Ok(await activation.Review(Actor, id, ct));

    [HttpGet("{id:guid}/contract")]
    public async Task<IActionResult> ReviewContract(Guid id, CancellationToken ct)
    {
        // Get the immutable PDF through the same integrity-checked private store.
        return File(await activation.ReviewPdf(Actor, id, files, ct), "application/pdf");
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var result = await activation.Approve(Actor, id, ct);
        return result.Status == "ApprovalConflict" ? Conflict(result) : Ok(result);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, RejectAssessorRequest request, CancellationToken ct)
        => Ok(await activation.Reject(Actor, id, request.Reason, ct));

    [HttpPost("{id:guid}/resend-password")]
    public async Task<IActionResult> ResendPassword(Guid id, CancellationToken ct)
    {
        await activation.ResendPasswordByAdmin(Actor, id, ct);
        return Accepted();
    }

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
