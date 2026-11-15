using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController]
public sealed class SpecializationsController(SpecializationService service) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);

    [AllowAnonymous, HttpGet("api/specializations")]
    public Task<List<SpecializationResponse>> List(CancellationToken ct) => service.ListAsync(ct: ct);

    [Authorize(Roles = "Admin"), HttpGet("api/admin/specializations")]
    public Task<List<SpecializationResponse>> AdminList(CancellationToken ct) => service.AdminListAsync(Actor, ct);

    [Authorize(Roles = "Admin"), HttpPost("api/admin/specializations")]
    public async Task<ActionResult<SpecializationResponse>> Create(SaveSpecializationRequest request, CancellationToken ct)
    {
        var result = await service.SaveAsync(Actor, null, request, ct);
        return StatusCode(201, result);
    }

    [Authorize(Roles = "Admin"), HttpPut("api/admin/specializations/{id:guid}")]
    public Task<SpecializationResponse> Update(Guid id, SaveSpecializationRequest request, CancellationToken ct)
        => service.SaveAsync(Actor, id, request, ct);

    [Authorize(Roles = "Admin"), HttpGet("api/admin/assessors/{userId:guid}/specializations")]
    public Task<IReadOnlyList<SpecializationResponse>> Assessor(Guid userId, CancellationToken ct)
        => service.GetAssessorAsync(Actor, userId, ct);

    [Authorize(Roles = "Admin"), HttpPut("api/admin/assessors/{userId:guid}/specializations")]
    public Task<IReadOnlyList<SpecializationResponse>> SetAssessor(Guid userId, SetAssessorSpecializationsRequest request, CancellationToken ct)
        => service.SetAssessorAsync(Actor, userId, request, ct);

    [Authorize, HttpPut("api/admin/projects/{projectId:guid}/specialization")]
    public Task<SpecializationResponse> SetProject(Guid projectId, SetProjectSpecializationRequest request, CancellationToken ct)
        => service.SetProjectAsync(Actor, projectId, request, ct);

    [Authorize(Roles = "Admin"), HttpGet("api/admin/projects/{projectId:guid}/suitable-assessors")]
    public Task<MatchingAssessorsResponse> Match(Guid projectId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => service.MatchAsync(Actor, projectId, page, pageSize, ct);
}
