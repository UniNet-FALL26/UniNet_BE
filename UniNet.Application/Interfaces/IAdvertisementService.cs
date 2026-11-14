using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniNet.Application.DTOs.Advertisement;

namespace UniNet.Application.Interfaces;

public interface IAdvertisementService
{
    // Doanh nghiệp (Partner)
    Task<CampaignResponse> CreateCampaignAsync(Guid accountId, CreateCampaignRequest request, CancellationToken ct = default);
    Task<CampaignResponse> AssignPackageAsync(Guid accountId, Guid campaignId, AssignPackageRequest request, CancellationToken ct = default);
    Task<CampaignResponse> UpdateCampaignAsync(Guid accountId, Guid campaignId, UpdateCampaignRequest request, CancellationToken ct = default);
    Task<CampaignsResponse> GetMyCampaignsAsync(Guid accountId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<CampaignResponse> GetCampaignByIdAsync(Guid accountId, Guid campaignId, CancellationToken ct = default);

    // Payment Integration
    Task<TransactionResponse> CreateCampaignPaymentAsync(Guid accountId, Guid campaignId, CancellationToken ct = default);
    Task ProcessPaymentCallbackAsync(string providerOrderCode, bool isSuccess, CancellationToken ct = default);
    Task<CampaignResponse> SyncCampaignPaymentStatusAsync(Guid accountId, string orderCode, CancellationToken ct = default);

    // Admin / Moderator
    Task<CampaignResponse> ModerateCampaignAsync(Guid accountId, Guid campaignId, ModerateCampaignRequest request, CancellationToken ct = default);
    Task<CampaignsResponse> ListAllCampaignsAsync(Guid accountId, int page = 1, int pageSize = 20, CancellationToken ct = default);

    // Display & Analytics
    Task<IReadOnlyList<CampaignResponse>> GetActiveAdsForDisplayAsync(CancellationToken ct = default);
    Task TrackImpressionAsync(Guid campaignId, CancellationToken ct = default);
    Task TrackClickAsync(Guid campaignId, CancellationToken ct = default);
    Task<CampaignResponse> SubmitCampaignAsync(Guid accountId, Guid campaignId, CancellationToken ct = default);
}