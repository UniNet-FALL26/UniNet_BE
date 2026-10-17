using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Entities;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin,Verificator"), Route("api/verification-notifications")]
public sealed class VerificationNotificationsController(VerificationNotifications notifications) : ControllerBase
{
    [HttpGet]
    public Task<List<VerificationNotification>> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId))
            throw new ProjectVerificationException("UNAUTHORIZED", "Invalid authentication session.", 401);
        return notifications.GetMineAsync(accountId, page, pageSize, ct);
    }
}
