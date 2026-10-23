using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Verificator"), Route("api/verificators/me/availability")]
public sealed class VerificatorAvailabilityController(VerificatorAvailabilityService availability) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new ProjectVerificationException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpGet]
    public Task<List<VerificatorAvailabilityResponse>> List([FromQuery, BindRequired] DateTimeOffset from,
        [FromQuery, BindRequired] DateTimeOffset to, CancellationToken ct) => availability.ListAsync(AccountId, from, to, ct);

    [HttpPost]
    public async Task<ActionResult<VerificatorAvailabilityResponse>> Create(SaveVerificatorAvailabilityRequest request,
        CancellationToken ct)
    {
        var result = await availability.SaveAsync(AccountId, null, request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("{id:guid}")]
    public Task<VerificatorAvailabilityResponse> Update(Guid id, SaveVerificatorAvailabilityRequest request,
        CancellationToken ct) => availability.SaveAsync(AccountId, id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await availability.DeleteAsync(AccountId, id, ct);
        return NoContent();
    }
}
