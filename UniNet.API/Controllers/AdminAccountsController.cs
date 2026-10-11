using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/accounts")]
public sealed class AdminAccountsController(AuthService auth) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new AuthException("UNAUTHORIZED", "Invalid authentication session.", 401);

    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create(CreateStaffAccountRequest request, CancellationToken ct)
    {
        var result = await auth.CreateStaffAccount(AccountId, request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}
