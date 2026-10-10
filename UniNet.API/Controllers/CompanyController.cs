using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.DTOs.Company;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Partner"), Route("api/company")]
public sealed class CompanyController(ICompanyService companies) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id
        : throw new UniNet.Application.Common.CompanyException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet("profile/me")]
    public Task<CompanyProfileResponse> GetProfile(CancellationToken ct)
        => companies.GetProfileAsync(AccountId, ct);

    [HttpPut("profile/me")]
    public Task<CompanyProfileResponse> UpdateProfile(UpdateCompanyProfileRequest request, CancellationToken ct)
        => companies.UpdateProfileAsync(AccountId, request, ct);

    [HttpGet("dashboard")]
    public Task<CompanyDashboardResponse> Dashboard(CancellationToken ct)
        => companies.GetDashboardAsync(AccountId, ct);

    [HttpGet("jobs")]
    public Task<CompanyJobsResponse> ListJobs(
        [FromQuery] CompanyJobStatus? status = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => companies.ListJobsAsync(AccountId, status, keyword, page, pageSize, ct);

    [HttpPost("jobs")]
    public async Task<ActionResult<CompanyJobResponse>> CreateJob(SaveCompanyJobRequest request, CancellationToken ct)
    {
        var job = await companies.CreateJobAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(GetJob), new { jobId = job.Id }, job);
    }

    [HttpGet("jobs/{jobId:guid}")]
    public Task<CompanyJobResponse> GetJob(Guid jobId, CancellationToken ct)
        => companies.GetJobAsync(AccountId, jobId, ct);

    [HttpPut("jobs/{jobId:guid}")]
    public Task<CompanyJobResponse> UpdateJob(Guid jobId, SaveCompanyJobRequest request, CancellationToken ct)
        => companies.UpdateJobAsync(AccountId, jobId, request, ct);

    [HttpPatch("jobs/{jobId:guid}/status")]
    public Task<CompanyJobResponse> UpdateJobStatus(Guid jobId, UpdateCompanyJobStatusRequest request, CancellationToken ct)
        => companies.UpdateJobStatusAsync(AccountId, jobId, request.Status, ct);

    [HttpDelete("jobs/{jobId:guid}")]
    public async Task<IActionResult> DeleteJob(Guid jobId, CancellationToken ct)
    {
        await companies.DeleteJobAsync(AccountId, jobId, ct);
        return NoContent();
    }
}
