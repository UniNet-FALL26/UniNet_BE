using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.DTOs.Partner;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Partner"), Route("api/partner")]
public sealed class PartnerController(IPartnerService partners) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id
        : throw new UniNet.Application.Common.PartnerException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet("dashboard")]
    public Task<PartnerDashboardResponse> Dashboard(CancellationToken ct)
        => partners.GetDashboardAsync(AccountId, ct);

    [HttpGet("jobs")]
    public Task<PartnerJobsResponse> ListJobs(
        [FromQuery] PartnerJobStatus? status = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => partners.ListJobsAsync(AccountId, status, keyword, page, pageSize, ct);

    [HttpPost("jobs")]
    public async Task<ActionResult<PartnerJobResponse>> CreateJob(SavePartnerJobRequest request, CancellationToken ct)
    {
        var job = await partners.CreateJobAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(GetJob), new { jobId = job.Id }, job);
    }

    [HttpGet("jobs/{jobId:guid}")]
    public Task<PartnerJobResponse> GetJob(Guid jobId, CancellationToken ct)
        => partners.GetJobAsync(AccountId, jobId, ct);

    [HttpPut("jobs/{jobId:guid}")]
    public Task<PartnerJobResponse> UpdateJob(Guid jobId, SavePartnerJobRequest request, CancellationToken ct)
        => partners.UpdateJobAsync(AccountId, jobId, request, ct);

    [HttpPatch("jobs/{jobId:guid}/status")]
    public Task<PartnerJobResponse> UpdateJobStatus(Guid jobId, UpdatePartnerJobStatusRequest request, CancellationToken ct)
        => partners.UpdateJobStatusAsync(AccountId, jobId, request.Status, ct);

    [HttpDelete("jobs/{jobId:guid}")]
    public async Task<IActionResult> DeleteJob(Guid jobId, CancellationToken ct)
    {
        await partners.DeleteJobAsync(AccountId, jobId, ct);
        return NoContent();
    }
}
