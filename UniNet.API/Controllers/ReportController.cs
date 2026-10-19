using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController, Authorize]
public sealed class ReportController(IReportService reports) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        && id != Guid.Empty ? id : throw new ReportException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpPost("api/reports"), Authorize(Roles = "Verificator")]
    public async Task<ActionResult<ReportResponse>> Submit([FromBody] SubmitReportRequest request, CancellationToken ct)
    {
        var report = await reports.SubmitAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(GetDetail), new { reportId = report.Id }, report);
    }

    [HttpGet("api/report/{assignmentId:guid}"), Authorize(Roles = "Verificator,Student,Partner,Admin")]
    public Task<ReportResponse> GetCurrent(Guid assignmentId, CancellationToken ct)
        => reports.GetCurrentAsync(assignmentId, AccountId, ct);

    [HttpGet("api/reports/{reportId:guid}"), Authorize(Roles = "Verificator,Student,Partner,Admin")]
    public Task<ReportResponse> GetDetail(Guid reportId, CancellationToken ct)
        => reports.GetDetailAsync(reportId, AccountId, ct);

}
