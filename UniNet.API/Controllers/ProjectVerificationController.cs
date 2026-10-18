using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/projects")]
public sealed class ProjectVerificationController(IProjectVerificationService verifications) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new ProjectVerificationException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpPost("{projectId:guid}/verifications")]
    public async Task<ActionResult<ProjectVerificationDetailResponse>> Create(
        Guid projectId, [FromBody] CreateProjectVerificationRequest request, CancellationToken ct)
    {
        var result = await verifications.CreateAsync(projectId, AccountId, request, ct);
        return CreatedAtAction(nameof(GetDetail), new { verificationId = result.Verification.Id }, result);
    }

    [HttpGet("{projectId:guid}/verifications")]
    public Task<ProjectVerificationsResponse> GetProjectVerifications(
        Guid projectId,
        [FromQuery] ProjectVerificationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => verifications.GetMyVerificationsAsync(AccountId, projectId, status, page, pageSize, ct);

    [HttpGet("my/verifications")]
    public Task<ProjectVerificationsResponse> GetMyVerifications(
        [FromQuery] Guid? projectId = null,
        [FromQuery] ProjectVerificationStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
        => verifications.GetMyVerificationsAsync(AccountId, projectId, status, page, pageSize, ct);

    [HttpGet("verifications/{verificationId:guid}")]
    public Task<ProjectVerificationDetailResponse> GetDetail(Guid verificationId, CancellationToken ct)
        => verifications.GetDetailAsync(verificationId, AccountId, ct);

    /// <summary>Xem slot trước thanh toán; vẫn cần cấp 1 hiện hành.</summary>
    [HttpGet("verifications/{verificationId:guid}/meeting-slots")]
    public Task<VerificationMeetingSlotsResponse> GetAvailableMeetingSlots(
        Guid verificationId,
        [FromQuery, BindRequired] DateTimeOffset from,
        [FromQuery, BindRequired] DateTimeOffset to,
        CancellationToken ct)
        => verifications.GetAvailableMeetingSlotsAsync(verificationId, AccountId, from, to, ct);

    /// <summary>Giữ slot có hạn trước checkout; không đổi lịch sau thanh toán.</summary>
    [HttpPut("verifications/{verificationId:guid}/meeting")]
    public Task<ProjectVerificationDetailResponse> ScheduleMeeting(
        Guid verificationId, [FromBody] ScheduleProjectVerificationRequest request, CancellationToken ct)
        => verifications.ScheduleMeetingAsync(verificationId, AccountId, request, ct);

    [HttpPatch("verifications/{verificationId:guid}/cancel")]
    public Task<ProjectVerificationDetailResponse> Cancel(
        Guid verificationId, [FromBody] CancelProjectVerificationRequest request, CancellationToken ct)
        => verifications.CancelAsync(verificationId, AccountId, request, ct);

    [HttpGet("verifications/{verificationId:guid}/result")]
    public Task<VerificationFinalResultResponse> GetFinalResult(Guid verificationId, CancellationToken ct)
        => verifications.GetFinalResultAsync(verificationId, AccountId, ct);
}
