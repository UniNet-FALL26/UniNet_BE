using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/workspace/livekit-token")]
public sealed class LiveKitTokenController(ILiveKitTokenService tokens) : ControllerBase
{
    [HttpPost]
    public Task<LiveKitTokenResponse> Create(Guid projectId, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId) ||
            accountId == Guid.Empty)
            throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);
        return tokens.CreateProjectRoomTokenAsync(projectId, accountId, ct);
    }
}
