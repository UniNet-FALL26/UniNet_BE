using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Advertisement;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AdvertisementService(
    UniNetDbContext db,
    IPaymentProvider provider,
    IConfiguration configuration) : IAdvertisementService
{
    public async Task<CampaignResponse> CreateCampaignAsync(Guid accountId, CreateCampaignRequest request, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        ValidateCreateRequest(request);

        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PackageId, ct);
        Require(package != null && package.IsActive && package.Type == PackageType.Advertisement && package.Role == TargetRole.Company,
            "INVALID_AD_PACKAGE", "An active Partner advertisement package is required.", 400);

        var now = DateTimeOffset.UtcNow;
        var campaign = new AdvertisementCampaign
        {
            Id = Guid.NewGuid(),
            CompanyUserId = partnerProfile.Id,
            PackageId = package.Id,
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            DestinationUrl = request.DestinationUrl.Trim(),
            MediaUrl = request.MediaUrl?.Trim(),
            BudgetCap = package.Price,
            Status = AdCampaignStatus.PendingPayment,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.AdvertisementCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", package.Name);
    }

    public async Task<CampaignResponse> UpdateCampaignAsync(Guid accountId, Guid campaignId, UpdateCampaignRequest request, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.Status is AdCampaignStatus.Draft or AdCampaignStatus.PendingPayment or AdCampaignStatus.Rejected,
            "INVALID_CAMPAIGN_STATE", "Only campaigns in Draft, PendingPayment or Rejected status can be updated.", 409);

        Require(!string.IsNullOrWhiteSpace(request.Title) && request.Title.Trim().Length <= 255, "INVALID_TITLE", "Title is required.");
        Require(!string.IsNullOrWhiteSpace(request.Content), "INVALID_CONTENT", "Content is required.");
        Require(!string.IsNullOrWhiteSpace(request.DestinationUrl), "INVALID_URL", "Destination URL is required.");

        campaign.Title = request.Title.Trim();
        campaign.Content = request.Content.Trim();
        campaign.DestinationUrl = request.DestinationUrl.Trim();
        campaign.MediaUrl = request.MediaUrl?.Trim();
        campaign.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", campaign.Package.Name);
    }

    public async Task<CampaignsResponse> GetMyCampaignsAsync(Guid accountId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var query = db.AdvertisementCampaigns.AsNoTracking()
            .Include(c => c.Package)
            .Where(c => c.CompanyUserId == partnerProfile.Id);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var responses = items.Select(c => MapToResponse(c, partnerProfile.FullName ?? "Doanh nghiệp", c.Package.Name)).ToList();
        return new(page, pageSize, total, (int)Math.Ceiling((double)total / pageSize), responses);
    }

    public async Task<CampaignResponse> GetCampaignByIdAsync(Guid accountId, Guid campaignId, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var campaign = await db.AdvertisementCampaigns.AsNoTracking()
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", campaign.Package.Name);
    }

    public async Task<TransactionResponse> CreateCampaignPaymentAsync(Guid accountId, Guid campaignId, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var options = PaymentOptions.Load(configuration);

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.Status is AdCampaignStatus.PendingPayment or AdCampaignStatus.Draft,
            "INVALID_CAMPAIGN_STATE", "Campaign is not eligible for payment.", 409);

        // Kiểm tra xem đã có giao dịch Pending nào chưa
        var existingTransaction = await db.Transactions.AsNoTracking()
            .Include(t => t.AdvertisementTransaction)
            .Where(t => t.AdvertisementTransaction != null && t.AdvertisementTransaction.CampaignId == campaignId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existingTransaction is { Status: PaymentStatus.Success })
            throw new AdvertisementException("CAMPAIGN_ALREADY_PAID", "This campaign is already paid.", 409);

        if (existingTransaction is { Status: PaymentStatus.Pending })
        {
            return TransactionMapping.ToResponse(existingTransaction, campaignId);
        }

        options.RequireCheckoutConfiguration();
        provider.EnsureConfigured();

        var now = DateTimeOffset.UtcNow;
        var orderCode = ((BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & ((1UL << 52) - 1)) + 1).ToString(CultureInfo.InvariantCulture);

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = partnerProfile.Id,
            PackageId = campaign.PackageId,
            Amount = campaign.BudgetCap,
            Status = PaymentStatus.Pending,
            PaymentMethod = provider.Name,
            ProviderOrderCode = orderCode,
            ExpiredAt = now.AddMinutes(options.LinkExpirationMinutes),
            CreatedAt = now,
            UpdatedAt = now
        };

        var adTransaction = new AdvertisementTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            CampaignId = campaign.Id,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Transactions.Add(transaction);
        db.AdvertisementTransactions.Add(adTransaction);

        await db.SaveChangesAsync(ct);

        // Gọi PayOS tạo link checkout
        var result = await provider.CreatePaymentAsync(new(
            transaction.ProviderOrderCode,
            (long)transaction.Amount,
            $"Quang cao UniNet - {campaign.Title}",
            options.ReturnUrl!,
            options.CancelUrl!,
            transaction.ExpiredAt.Value
        ), ct);

        transaction.ProviderPaymentLinkId = result.PaymentLinkId;
        transaction.PaymentUrl = result.CheckoutUrl;
        campaign.Status = AdCampaignStatus.PendingPayment;

        await db.SaveChangesAsync(ct);

        return TransactionMapping.ToResponse(transaction, campaignId);
    }

    public async Task<CampaignResponse> ModerateCampaignAsync(Guid accountId, Guid campaignId, ModerateCampaignRequest request, CancellationToken ct = default)
    {
        await EnsureAdminAsync(accountId, ct);

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .Include(c => c.CompanyUser)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw NotFound();

        Require(campaign.Status == AdCampaignStatus.PendingModeration,
            "INVALID_STATUS", "Only campaigns in PendingModeration state can be moderated.", 409);

        var now = DateTimeOffset.UtcNow;
        if (request.IsApproved)
        {
            campaign.Status = AdCampaignStatus.Active;
            campaign.StartedAt = now;
            if (campaign.Package.DurationInDays.HasValue)
            {
                campaign.ExpiredAt = now.AddDays(campaign.Package.DurationInDays.Value);
            }
        }
        else
        {
            campaign.Status = AdCampaignStatus.Rejected;
        }

        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, campaign.CompanyUser.FullName ?? "Doanh nghiệp", campaign.Package.Name);
    }

    public async Task<CampaignsResponse> ListAllCampaignsAsync(Guid accountId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        await EnsureAdminAsync(accountId, ct);

        var query = db.AdvertisementCampaigns.AsNoTracking()
            .Include(c => c.Package)
            .Include(c => c.CompanyUser);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var responses = items.Select(c => MapToResponse(c, c.CompanyUser.FullName ?? "Doanh nghiệp", c.Package.Name)).ToList();
        return new(page, pageSize, total, (int)Math.Ceiling((double)total / pageSize), responses);
    }

    public async Task<IReadOnlyList<CampaignResponse>> GetActiveAdsForDisplayAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var activeAds = await db.AdvertisementCampaigns.AsNoTracking()
            .Include(c => c.Package)
            .Include(c => c.CompanyUser)
            .Where(c => c.Status == AdCampaignStatus.Active && (c.ExpiredAt == null || c.ExpiredAt > now))
            .OrderBy(c => Guid.NewGuid()) // Hiển thị ngẫu nhiên
            .Take(5)
            .ToListAsync(ct);

        return activeAds.Select(c => MapToResponse(c, c.CompanyUser.FullName ?? "Doanh nghiệp", c.Package.Name)).ToList();
    }

    public async Task TrackImpressionAsync(Guid campaignId, CancellationToken ct = default)
    {
        await db.AdvertisementCampaigns
            .Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ImpressionCount, c => c.ImpressionCount + 1), ct);
    }

    public async Task TrackClickAsync(Guid campaignId, CancellationToken ct = default)
    {
        await db.AdvertisementCampaigns
            .Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ClickCount, c => c.ClickCount + 1), ct);
    }

    private async Task<UserProfile> GetPartnerProfileAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking()
            .Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new AdvertisementException("PROFILE_NOT_FOUND", "User profile not found.", 404);

        Require(profile.Account.Status == AccountStatus.Active && profile.Account.Role == AccountRole.Partner,
            "FORBIDDEN", "Only active Partners can access advertisement services.", 403);

        return profile;
    }

    private async Task EnsureAdminAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new AdvertisementException("UNAUTHORIZED", "Account not found.", 401);

        Require(account.Status == AccountStatus.Active && account.Role == AccountRole.Admin,
            "FORBIDDEN", "Admin access required.", 403);
    }

    private static void ValidateCreateRequest(CreateCampaignRequest request)
    {
        Require(!string.IsNullOrWhiteSpace(request.Title) && request.Title.Trim().Length <= 255, "INVALID_TITLE", "Title is required.");
        Require(!string.IsNullOrWhiteSpace(request.Content), "INVALID_CONTENT", "Content is required.");
        Require(!string.IsNullOrWhiteSpace(request.DestinationUrl), "INVALID_URL", "Destination URL is required.");
    }

    private static CampaignResponse MapToResponse(AdvertisementCampaign c, string companyName, string packageName) => new(
        c.Id, c.CompanyUserId, companyName, c.PackageId, packageName,
        c.Title, c.Content, c.DestinationUrl, c.MediaUrl,
        c.Status, c.BudgetCap, c.ImpressionCount, c.ClickCount,
        c.StartedAt, c.ExpiredAt, c.CreatedAt, c.UpdatedAt);

    private static AdvertisementException NotFound() => new("CAMPAIGN_NOT_FOUND", "Advertisement campaign was not found.", 404);

    private static void Require(bool condition, string code, string message, int statusCode = 400)
    {
        if (!condition) throw new AdvertisementException(code, message, statusCode);
    }
}