using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.DTOs.Verification;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Partner"), Route("api/partner/verification")]
public sealed class PartnerVerificationController(PartnerVerificationService verifications) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new UniNet.Application.AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet("me")]
    public Task<PartnerVerificationStatusResponse> MyStatus(CancellationToken ct)
        => verifications.GetMyStatusAsync(AccountId, ct);

    [HttpPost]
    public Task<PartnerVerificationStatusResponse> Submit(SubmitPartnerVerificationRequest request, CancellationToken ct)
        => verifications.SubmitAsync(AccountId, request, ct);
}

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/partner-verifications")]
public sealed class AdminPartnerVerificationsController(PartnerVerificationService verifications) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new UniNet.Application.AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet]
    public Task<PartnerVerificationQueueResponse> Pending([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => verifications.ListPendingAsync(page, pageSize, ct);

    [HttpGet("{verificationId:guid}")]
    public Task<PartnerVerificationDetailResponse> Detail(Guid verificationId, CancellationToken ct)
        => verifications.GetPendingAsync(verificationId, ct);

    [HttpPost("{verificationId:guid}/approve")]
    public Task<PartnerVerificationDetailResponse> Approve(Guid verificationId, CancellationToken ct)
        => verifications.ApproveAsync(AccountId, verificationId, ct);

    [HttpPost("{verificationId:guid}/reject")]
    public Task<PartnerVerificationDetailResponse> Reject(Guid verificationId, RejectPartnerVerificationRequest request, CancellationToken ct)
        => verifications.RejectAsync(AccountId, verificationId, request.Reason, ct);
}
