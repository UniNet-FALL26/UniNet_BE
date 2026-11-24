using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

// Separate action/controller: private OfficialV2 routes retain their existing authorization.
[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}/contribution/v2/results/public")]
public sealed class PublishedContributionController(PublishedContributionReader reader) : ControllerBase
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<PublishedContributionResponse> Get(Guid projectId, CancellationToken ct) =>
        reader.ReadAsync(projectId, Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id : throw new ProjectException("UNAUTHORIZED", "Login required.", 401), ct);
}
