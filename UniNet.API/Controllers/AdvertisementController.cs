using System;
using System.IdentityModel.Tokens.Jwt;
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

    // --- DOANH NGHIỆP (PARTNER) ---
    [HttpPost("campaigns"), Authorize(Roles = "Partner")]
    public async Task<ActionResult<CampaignResponse>> CreateCampaign([FromBody] CreateCampaignRequest request, CancellationToken ct)
    {
        var campaign = await adService.CreateCampaignAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(GetCampaignById), new { campaignId = campaign.Id }, campaign);
    }

    [HttpPut("campaigns/{campaignId:guid}"), Authorize(Roles = "Partner")]
    public Task<CampaignResponse> UpdateCampaign(Guid campaignId, [FromBody] UpdateCampaignRequest request, CancellationToken ct)
        => adService.UpdateCampaignAsync(AccountId, campaignId, request, ct);

    [HttpGet("campaigns/my"), Authorize(Roles = "Partner")]
    public Task<CampaignsResponse> GetMyCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => adService.GetMyCampaignsAsync(AccountId, page, pageSize, ct);

    [HttpGet("campaigns/{campaignId:guid}"), Authorize(Roles = "Partner")]
    public Task<CampaignResponse> GetCampaignById(Guid campaignId, CancellationToken ct)
        => adService.GetCampaignByIdAsync(AccountId, campaignId, ct);

    [HttpPost("campaigns/{campaignId:guid}/payments"), Authorize(Roles = "Partner")]
    public Task<TransactionResponse> CreateCampaignPayment(Guid campaignId, CancellationToken ct)
        => adService.CreateCampaignPaymentAsync(AccountId, campaignId, ct);

    // --- ADMIN / MODERATOR ---
    [HttpGet("admin/campaigns"), Authorize(Roles = "Admin")]
    public Task<CampaignsResponse> ListAllCampaigns([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => adService.ListAllCampaignsAsync(AccountId, page, pageSize, ct);

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
        return Ok();
    }

    [HttpPost("campaigns/{campaignId:guid}/click"), AllowAnonymous]
    public async Task<IActionResult> TrackClick(Guid campaignId, CancellationToken ct)
    {
        await adService.TrackClickAsync(campaignId, ct);
        return Ok();
    }
}