using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/projects/{projectId:guid}/evidence-moderation")]
public sealed class EvidenceModerationController(EvidenceModerationService moderation) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new ProjectVerificationException("UNAUTHORIZED", "Invalid session.", 401);

    [HttpGet]
    public Task<EvidenceModerationResponse> Get(Guid projectId, CancellationToken ct)
        => moderation.GetAsync(projectId, AccountId, ct);

    [HttpPost("retry")]
    public Task<EvidenceModerationResponse> Retry(Guid projectId, CancellationToken ct)
        => moderation.RetryAsync(projectId, AccountId, ct);
}
