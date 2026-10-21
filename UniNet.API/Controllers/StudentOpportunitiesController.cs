using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.DTOs.Partner;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/opportunities")]
public sealed class StudentOpportunitiesController(IStudentOpportunityService opportunities) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id
        : throw new UniNet.Application.Common.PartnerException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet]
    public Task<OpportunityJobsResponse> List(
        [FromQuery] string? view = null,
        [FromQuery] string? keyword = null,
        [FromQuery] JobWorkType? workType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => opportunities.ListAsync(AccountId, view, keyword, workType, page, pageSize, ct);

    [HttpGet("{jobId:guid}")]
    public Task<OpportunityJobResponse> Get(Guid jobId, CancellationToken ct)
        => opportunities.GetAsync(AccountId, jobId, ct);

    [HttpPut("{jobId:guid}/saved")]
    public async Task<IActionResult> Save(Guid jobId, CancellationToken ct)
    {
        await opportunities.SaveAsync(AccountId, jobId, ct);
        return NoContent();
    }

    [HttpDelete("{jobId:guid}/saved")]
    public async Task<IActionResult> Unsave(Guid jobId, CancellationToken ct)
    {
        await opportunities.UnsaveAsync(AccountId, jobId, ct);
        return NoContent();
    }

    [HttpPost("{jobId:guid}/applications")]
    public async Task<IActionResult> Apply(Guid jobId, ApplyToJobRequest request, CancellationToken ct)
    {
        await opportunities.ApplyAsync(AccountId, jobId, request.CoverMessage, ct);
        return NoContent();
    }
}
