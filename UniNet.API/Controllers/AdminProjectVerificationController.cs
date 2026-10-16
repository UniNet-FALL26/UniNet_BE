using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/project-verifications")]
public sealed class AdminProjectVerificationController(IAdminProjectVerificationService service) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new ProjectVerificationException("UNAUTHORIZED", "Invalid authentication session.", 401);

    [HttpGet]
    public Task<ProjectVerificationsResponse> List([FromQuery] ProjectVerificationStatus? status = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => service.ListAsync(AccountId, status, from, to, page, pageSize, ct: ct);

    [HttpGet("pending-assignment")]
    public Task<ProjectVerificationsResponse> PendingAssignment([FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => service.ListAsync(AccountId, page: page, pageSize: pageSize, pendingAssignment: true, ct: ct);

    [HttpGet("overdue")]
    public Task<ProjectVerificationsResponse> Overdue([FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => service.ListAsync(AccountId, page: page, pageSize: pageSize, overdue: true, ct: ct);

    [HttpGet("{verificationId:guid}")]
    public Task<AdminVerificationDetailResponse> Detail(Guid verificationId, CancellationToken ct)
        => service.GetDetailAsync(verificationId, AccountId, ct);

    [HttpGet("{verificationId:guid}/reports")]
    public Task<List<VerificationReportResponse>> Reports(Guid verificationId, CancellationToken ct)
        => service.GetReportsAsync(verificationId, AccountId, ct);

    [HttpGet("{verificationId:guid}/refunds")]
    public Task<List<VerificationRefundResponse>> Refunds(Guid verificationId, CancellationToken ct)
        => service.GetRefundsAsync(verificationId, AccountId, ct);

    [HttpPost("{verificationId:guid}/refunds")]
    public async Task<ActionResult<VerificationRefundResponse>> CreateRefund(Guid verificationId,
        CreateVerificationRefundRequest request, CancellationToken ct)
        => CreatedAtAction(nameof(Refunds), new { verificationId }, await service.CreateRefundAsync(verificationId, AccountId, request, ct));

    [HttpPost("{verificationId:guid}/reviews")]
    public async Task<ActionResult<VerificationAdminActionResponse>> Review(Guid verificationId,
        ReviewVerificationRequest request, CancellationToken ct)
        => CreatedAtAction(nameof(Detail), new { verificationId }, await service.ReviewAsync(verificationId, AccountId, request, ct));
}
