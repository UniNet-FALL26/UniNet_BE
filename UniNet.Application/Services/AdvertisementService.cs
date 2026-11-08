using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    IAIModerationService aiModeration,
    ICloudImageStorage imageStorage,
    IConfiguration configuration) : IAdvertisementService
{

    public async Task<CampaignResponse> CreateCampaignAsync(Guid accountId, CreateCampaignRequest request, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        ValidateCreateRequest(request);

        var now = DateTimeOffset.UtcNow;
        var campaign = new AdvertisementCampaign
        {
            Id = Guid.NewGuid(),
            CompanyUserId = partnerProfile.Id,
            PackageId = null, // Chưa có gói lúc tạo nháp
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            DestinationUrl = request.DestinationUrl.Trim(),
            BudgetCap = 0,
            Status = AdCampaignStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Xử lý upload ảnh duy nhất (Single Image Upload)
        if (request.MediaFile != null)
        {
            // Kiểm tra an toàn nếu FormFileCollection lỡ nhận nhiều hơn 1 file (phòng hờ trường hợp dùng IFormFileCollection)
            // Nếu dùng IFormFile đơn lẻ thì cú pháp trên là đủ, nhưng nếu Controller nhận List<IFormFile> thì cần check:
            // if (request.MediaFiles?.Count > 1) throw new AdvertisementException("TOO_MANY_IMAGES", "Chỉ được phép upload tối đa 1 ảnh cho mỗi chiến dịch.", 400);

            if (request.MediaFile.Length > 0)
            {
                var extension = Path.GetExtension(request.MediaFile.FileName).ToLowerInvariant().TrimStart('.');
                if (extension is not ("png" or "jpg" or "jpeg" or "webp"))
                {
                    throw new AdvertisementException("INVALID_IMAGE_FORMAT", $"Định dạng ảnh .{extension} không hợp lệ.", 400);
                }

                using var memoryStream = new MemoryStream();
                await request.MediaFile.CopyToAsync(memoryStream, ct);

                var publicId = $"advertisements/campaigns/{campaign.Id}/{Guid.NewGuid():N}";
                var format = extension == "jpeg" ? "jpg" : extension;

                var uploadResult = await imageStorage.Upload(
                    new ImageUploadRequest(memoryStream.ToArray(), publicId, format, "image"),
                    ct
                );

                campaign.MediaUrl = uploadResult.ImageUrl;
            }
        }

        db.AdvertisementCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", null);
    }
    public async Task<CampaignResponse> AssignPackageAsync(Guid accountId, Guid campaignId, AssignPackageRequest request, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.Status == AdCampaignStatus.Draft,
            "INVALID_CAMPAIGN_STATE", "Only campaigns in Draft status can be assigned a package.", 409);

        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PackageId, ct);
        Require(package != null && package.IsActive && package.Type == PackageType.Advertisement && package.Role == TargetRole.Company,
            "INVALID_AD_PACKAGE", "An active Partner advertisement package is required.", 400);

        // LƯU Ý LUỒNG MỚI: Chỉ gán gói và chuyển thẳng sang trạng thái chờ thanh toán (Không check AI ở đây nữa)
        campaign.PackageId = package.Id;
        campaign.BudgetCap = package.Price;
        campaign.Status = AdCampaignStatus.PendingPayment;
        campaign.RejectionReason = null;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", package.Name);
    }

    // Admin duyệt tay (Dành cho các bài đã thanh toán nhưng rớt AI)
    public async Task<CampaignResponse> ModerateCampaignAsync(Guid accountId, Guid campaignId, ModerateCampaignRequest request, CancellationToken ct = default)
    {
        await EnsureAdminAsync(accountId, ct);

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .Include(c => c.CompanyUser)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw NotFound();

        Require(campaign.Status == AdCampaignStatus.PendingModeration,
            "INVALID_STATUS", "Only campaigns in PendingModeration state can be reviewed by Admin.", 409);

        var now = DateTimeOffset.UtcNow;
        if (request.IsApproved)
        {
            // LUỒNG MỚI: Admin duyệt pass -> Chuyển thẳng sang Active vì ĐÃ THANH TOÁN TRƯỚC ĐÓ RỒI
            campaign.Status = AdCampaignStatus.Active;
            campaign.RejectionReason = null;
            campaign.StartedAt = now;
            if (campaign.Package.DurationInDays.HasValue)
            {
                campaign.ExpiredAt = now.AddDays(campaign.Package.DurationInDays.Value);
            }
        }
        else
        {
            // Admin từ chối -> Rejected (Lưu ý: Tiền đã thu, tính toán logic hoàn tiền bên ngoài nếu cần)
            campaign.Status = AdCampaignStatus.Rejected;
            campaign.RejectionReason = request.Reason ?? "Nội dung quảng cáo không phù hợp với tiêu chuẩn cộng đồng.";
        }

        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, campaign.CompanyUser.FullName ?? "Doanh nghiệp", campaign.Package.Name);
    }
    public async Task<CampaignResponse> SubmitCampaignAsync(Guid accountId, Guid campaignId, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.Status is AdCampaignStatus.Draft or AdCampaignStatus.Rejected,
            "INVALID_CAMPAIGN_STATE", "Only Draft or Rejected campaigns can be submitted.", 409);

        // Kích hoạt AI Review khi bấm Submit từ Nháp
        bool isSuitable = await aiModeration.ReviewContentAsync(campaign.Title, campaign.Content, ct);
        campaign.Status = isSuitable ? AdCampaignStatus.PendingPayment : AdCampaignStatus.PendingModeration;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
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

        Require(campaign.Status == AdCampaignStatus.PendingPayment,
            "INVALID_CAMPAIGN_STATE", "Campaign is not eligible for payment. It must be approved first.", 409);

        var existingTransaction = await db.Transactions.AsNoTracking()
    .Include(t => t.AdvertisementTransaction)
    .Where(t => t.AdvertisementTransaction != null && t.AdvertisementTransaction.CampaignId == campaignId)
    .OrderByDescending(t => t.CreatedAt)
    .FirstOrDefaultAsync(ct);

        if (existingTransaction is { Status: PaymentStatus.Success })
            throw new AdvertisementException("CAMPAIGN_ALREADY_PAID", "This campaign is already paid.", 409);

        // NẾU CÓ GIAO DỊCH PENDING:
        if (existingTransaction is { Status: PaymentStatus.Pending })
        {
            // Kiểm tra xem đã quá hạn (ExpiredAt) hay chưa
            if (existingTransaction.ExpiredAt > DateTimeOffset.UtcNow)
            {
                // Nếu chưa quá hạn và vẫn còn link -> Trả về link cũ bình thường
                return TransactionMapping.ToResponse(existingTransaction, campaignId);
            }
            else
            {
                // Nếu đã quá hạn (hoặc user đã bấm hủy làm link chết), 
                // ta cập nhật transaction cũ thành Failed (hoặc bỏ qua nó) để tạo transaction mới bên dưới
                var oldTx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == existingTransaction.Id, ct);
                if (oldTx != null)
                {
                    oldTx.Status = PaymentStatus.Failed;
                    oldTx.UpdatedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
        }

        // --- TIẾP TỤC CODE TẠO GIAO DỊCH VÀ LINK PAYOS MỚI Ở DƯỚI ---
        options.RequireCheckoutConfiguration();
        // provider.EnsureConfigured(); // Đã được gọi ngầm trong phương thức CreatePaymentAsync của Provider, bạn có thể bỏ dòng này đi

        var now = DateTimeOffset.UtcNow;
        var orderCode = ((BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & ((1UL << 52) - 1)) + 1).ToString(CultureInfo.InvariantCulture);

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = partnerProfile.Id,
            PackageId = campaign.PackageId ?? throw new AdvertisementException("PACKAGE_REQUIRED", "Campaign must have a package before payment.", 400),
            Amount = campaign.BudgetCap,
            Status = PaymentStatus.Pending,
            PaymentMethod = "PayOS", // Sửa thành string cứng nếu biến provider.Name không public
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

        // Xử lý an toàn cho Description (PayOS giới hạn tối đa 25 ký tự)
        string rawDescription = $"QC {orderCode}";
        string safeDescription = rawDescription.Length > 25 ? rawDescription.Substring(0, 25) : rawDescription;

        // Truyền dữ liệu vào record CreateProviderPaymentRequest chuẩn của bạn
        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest(
            OrderCode: transaction.ProviderOrderCode,
            AmountVnd: (long)transaction.Amount,
            Description: safeDescription,
            ReturnUrl: options.ReturnUrl!,
            CancelUrl: options.CancelUrl!,
            ExpiresAt: transaction.ExpiredAt.Value
        ), ct);

        // Map kết quả trả về từ ProviderPaymentResult
        // Lưu ý: Thay đổi "Id" và "CheckoutUrl" thành đúng tên thuộc tính trong record ProviderPaymentResult của bạn
        transaction.ProviderPaymentLinkId = result.PaymentLinkId;
        transaction.PaymentQrCode = result.QrCode;

        await db.SaveChangesAsync(ct);

        return TransactionMapping.ToResponse(transaction, campaignId);
    }

    // Xử lý Webhook khi thanh toán thành công (BƯỚC CHẠY AI REVIEW)
    public async Task ProcessPaymentCallbackAsync(string providerOrderCode, bool isSuccess, CancellationToken ct = default)
    {
        var transaction = await db.Transactions
            .Include(t => t.AdvertisementTransaction)
            .FirstOrDefaultAsync(t => t.ProviderOrderCode == providerOrderCode, ct);

        if (transaction == null || transaction.AdvertisementTransaction == null) return;

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == transaction.AdvertisementTransaction.CampaignId, ct);

        if (campaign == null) return;

        var now = DateTimeOffset.UtcNow;
        if (isSuccess)
        {
            transaction.Status = PaymentStatus.Success;

            // THANH TOÁN THÀNH CÔNG -> KÍCH HOẠT AI REVIEW
            bool isSuitable = await aiModeration.ReviewContentAsync(campaign.Title, campaign.Content, ct);

            if (isSuitable)
            {
                // AI Duyệt Pass -> Cho chạy quảng cáo luôn
                campaign.Status = AdCampaignStatus.Active;
                campaign.StartedAt = now;
                if (campaign.Package.DurationInDays.HasValue)
                {
                    campaign.ExpiredAt = now.AddDays(campaign.Package.DurationInDays.Value);
                }
            }
            else
            {
                // AI Nghi vấn -> Chuyển sang chờ Admin duyệt (Tiền đã thu)
                campaign.Status = AdCampaignStatus.PendingModeration;
                campaign.RejectionReason = "AI phát hiện nội dung có chứa từ khóa nhạy cảm, chuyển sang bộ phận kiểm duyệt thủ công.";
            }
        }
        else
        {
            transaction.Status = PaymentStatus.Failed;
            // Thanh toán thất bại -> Giữ nguyên PendingPayment
        }

        transaction.UpdatedAt = now;
        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }
    // Xử lý khi Partner sửa bài viết (Đặc biệt là khi bài đã thu tiền nhưng bị Admin Reject)
    public async Task<CampaignResponse> UpdateCampaignAsync(Guid accountId, Guid campaignId, UpdateCampaignRequest request, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.Status is AdCampaignStatus.Draft or AdCampaignStatus.Rejected,
            "INVALID_CAMPAIGN_STATE", "Only Draft or Rejected campaigns can be updated.", 409);

        campaign.Title = request.Title.Trim();
        campaign.Content = request.Content.Trim();
        campaign.DestinationUrl = request.DestinationUrl.Trim();

        if (request.MediaUrl != null)
        {
            campaign.MediaUrl = request.MediaUrl.Trim();
        }

        if (campaign.Status == AdCampaignStatus.Draft)
        {
            // Nếu vẫn đang nháp thì cứ cho nháp tiếp
            campaign.Status = AdCampaignStatus.Draft;
        }
        else if (campaign.Status == AdCampaignStatus.Rejected)
        {
            // LUỒNG MỚI: Nếu bài bị từ chối (tức là đã thanh toán), sửa xong chạy AI Review lại ngay lập tức
            bool isSuitable = await aiModeration.ReviewContentAsync(campaign.Title, campaign.Content, ct);
            if (isSuitable)
            {
                campaign.Status = AdCampaignStatus.Active;
                campaign.RejectionReason = null;
                campaign.StartedAt = DateTimeOffset.UtcNow;
                if (campaign.Package.DurationInDays.HasValue)
                    campaign.ExpiredAt = DateTimeOffset.UtcNow.AddDays(campaign.Package.DurationInDays.Value);
            }
            else
            {
                campaign.Status = AdCampaignStatus.PendingModeration;
                campaign.RejectionReason = "AI phát hiện nội dung có chứa từ khóa nhạy cảm, chuyển sang bộ phận kiểm duyệt.";
            }
        }

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

        var responses = items.Select(c => MapToResponse(
    c,
    partnerProfile.FullName ?? "Doanh nghiệp",
    c.Package != null ? c.Package.Name : null // Hoặc c.Package?.Name
)).ToList();
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
            .OrderBy(c => Guid.NewGuid())
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

    private static CampaignResponse MapToResponse(AdvertisementCampaign c, string companyName, string? packageName) => new(
        c.Id,
        c.CompanyUserId,
        companyName,
        c.PackageId,
        packageName, // Cho phép truyền null nếu chưa chọn gói
        c.Title,
        c.Content,
        c.DestinationUrl,
        c.MediaUrl,
        c.Status,
        c.BudgetCap,
        c.RejectionReason,
        c.ImpressionCount,
        c.ClickCount,
        c.StartedAt,
        c.ExpiredAt,
        c.CreatedAt,
        c.UpdatedAt);

    private static AdvertisementException NotFound() => new("CAMPAIGN_NOT_FOUND", "Advertisement campaign was not found.", 404);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int statusCode = 400)
    {
        if (!condition) throw new AdvertisementException(code, message, statusCode);
    }
}