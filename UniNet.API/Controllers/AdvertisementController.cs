using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Advertisement;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController]
[Route("api/advertisements")]
public sealed class AdvertisementController(IAdvertisementService adService) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new AdvertisementException("UNAUTHORIZED", "Invalid login session.", 401);

    // --- PARTNER ---

    // 1. Tạo campaign (Status = Draft, PackageId = null)
    [HttpPost("campaigns"), Authorize(Roles = "Partner")]
    [Consumes("multipart/form-data")] // Yêu cầu gửi dạng form-data để upload file ảnh
    public async Task<ActionResult<CampaignResponse>> CreateCampaign([FromForm] CreateCampaignRequest request, CancellationToken ct)
    {
        var campaign = await adService.CreateCampaignAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(GetCampaignById), new { campaignId = campaign.Id }, campaign);
    }

    // 2. Gán Package -> Chuyển thẳng sang PendingPayment (Chờ thanh toán, chưa chạy AI)
    [HttpPatch("campaigns/{campaignId:guid}/package"), Authorize(Roles = "Partner")]
    public Task<CampaignResponse> AssignPackage(Guid campaignId, [FromBody] AssignPackageRequest request, CancellationToken ct)
        => adService.AssignPackageAsync(AccountId, campaignId, request, ct);

    // 3. Thanh toán -> Sau khi thanh toán thành công Webhook sẽ tự động chạy AI Review
    [HttpPost("campaigns/{campaignId:guid}/payments"), Authorize(Roles = "Partner")]
    public Task<TransactionResponse> CreateCampaignPayment(Guid campaignId, CancellationToken ct)
        => adService.CreateCampaignPaymentAsync(AccountId, campaignId, ct);

    // 4. Sửa campaign (Tự động chạy lại AI Review nếu bài viết đang ở trạng thái Rejected)
    [HttpPut("campaigns/{campaignId:guid}"), Authorize(Roles = "Partner")]
    // Lưu ý: Nếu UpdateCampaignRequest của bạn cũng dùng IFormFile để đổi ảnh, hãy đổi [FromBody] thành [FromForm] và thêm [Consumes("multipart/form-data")]
    public Task<CampaignResponse> UpdateCampaign(Guid campaignId, [FromBody] UpdateCampaignRequest request, CancellationToken ct)
        => adService.UpdateCampaignAsync(AccountId, campaignId, request, ct);

    [HttpGet("campaigns/my"), Authorize(Roles = "Partner")]
    public Task<CampaignsResponse> GetMyCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => adService.GetMyCampaignsAsync(AccountId, page, pageSize, ct);

    [HttpGet("campaigns/{campaignId:guid}"), Authorize(Roles = "Partner")]
    public Task<CampaignResponse> GetCampaignById(Guid campaignId, CancellationToken ct)
        => adService.GetCampaignByIdAsync(AccountId, campaignId, ct);


    // --- ADMIN / MODERATOR ---

    [HttpGet("admin/campaigns"), Authorize(Roles = "Admin")]
    public Task<CampaignsResponse> ListAllCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => adService.ListAllCampaignsAsync(AccountId, page, pageSize, ct);

    // Admin duyệt tay (Dành cho các bài đã thanh toán nhưng không qua được cửa AI)
    [HttpPost("admin/campaigns/{campaignId:guid}/moderate"), Authorize(Roles = "Admin")]
    public Task<CampaignResponse> ModerateCampaign(Guid campaignId, [FromBody] ModerateCampaignRequest request, CancellationToken ct)
        => adService.ModerateCampaignAsync(AccountId, campaignId, request, ct);


    // --- PUBLIC FEED & ANALYTICS ---

    [HttpGet("feed"), AllowAnonymous]
    public Task<IReadOnlyList<CampaignResponse>> GetFeedAds(CancellationToken ct)
        => adService.GetActiveAdsForDisplayAsync(ct);

    [HttpPost("campaigns/{campaignId:guid}/impression"), AllowAnonymous]
    public async Task<IActionResult> TrackImpression(Guid campaignId, CancellationToken ct)
    {
        await adService.TrackImpressionAsync(campaignId, ct);
        return NoContent();
    }

    [HttpPost("campaigns/{campaignId:guid}/click"), AllowAnonymous]
    public async Task<IActionResult> TrackClick(Guid campaignId, CancellationToken ct)
    {
        await adService.TrackClickAsync(campaignId, ct);
        return NoContent();
    }
}