using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
    private const int ModerationSlaHours = 24;

    // 1. Tạo nháp chiến dịch (Status = Draft)
    public async Task<CampaignResponse> CreateCampaignAsync(Guid accountId, CreateCampaignRequest request, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        ValidateCampaignFields(request.Title, request.Content, request.DestinationUrl);

        var now = DateTimeOffset.UtcNow;
        var campaign = new AdvertisementCampaign
        {
            Id = Guid.NewGuid(),
            CompanyUserId = partnerProfile.Id,
            PackageId = null,
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            DestinationUrl = request.DestinationUrl.Trim(),
            BudgetCap = 0,
            Status = AdCampaignStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (request.MediaFile is { Length: > 0 })
        {
            campaign.MediaUrl = await UploadCampaignImageAsync(campaign.Id, request.MediaFile, ct);
        }

        db.AdvertisementCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", null);
    }

    // 2. Chọn gói quảng cáo -> Chuyển sang chờ thanh toán (Draft -> PendingPayment)
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

        campaign.PackageId = package.Id;
        campaign.BudgetCap = package.Price;
        campaign.Status = AdCampaignStatus.PendingPayment;
        campaign.RejectionReason = null;
        campaign.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", package.Name);
    }

    // 3. Tạo giao dịch & link thanh toán PayOS
    public async Task<TransactionResponse> CreateCampaignPaymentAsync(Guid accountId, Guid campaignId, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var options = PaymentOptions.Load(configuration);

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.Status == AdCampaignStatus.PendingPayment,
            "INVALID_CAMPAIGN_STATE", "Campaign is not eligible for payment. It must have a package assigned first.", 409);

        var existingTransaction = await db.Transactions.AsNoTracking()
            .Include(t => t.AdvertisementTransaction)
            .Where(t => t.AdvertisementTransaction != null && t.AdvertisementTransaction.CampaignId == campaignId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (existingTransaction is { Status: PaymentStatus.Success })
            throw new AdvertisementException("CAMPAIGN_ALREADY_PAID", "This campaign is already paid.", 409);

        if (existingTransaction is { Status: PaymentStatus.Pending })
        {
            if (existingTransaction.ExpiredAt > DateTimeOffset.UtcNow)
            {
                return TransactionMapping.ToResponse(existingTransaction, campaignId);
            }

            var oldTx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == existingTransaction.Id, ct);
            if (oldTx != null)
            {
                oldTx.Status = PaymentStatus.Failed;
                oldTx.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        options.RequireCheckoutConfiguration();

        var now = DateTimeOffset.UtcNow;
        var orderCode = ((BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & ((1UL << 52) - 1)) + 1)
            .ToString(CultureInfo.InvariantCulture);

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = partnerProfile.Id,
            PackageId = campaign.PackageId ?? throw new AdvertisementException("PACKAGE_REQUIRED", "Campaign must have a package before payment.", 400),
            Amount = campaign.BudgetCap,
            Status = PaymentStatus.Pending,
            PaymentMethod = "PayOS",
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

        string rawDescription = $"QC {orderCode}";
        string safeDescription = rawDescription.Length > 25 ? rawDescription[..25] : rawDescription;

        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest(
            OrderCode: transaction.ProviderOrderCode,
            AmountVnd: (long)transaction.Amount,
            Description: safeDescription,
            ReturnUrl: options.ReturnUrl!,
            CancelUrl: options.CancelUrl!,
            ExpiresAt: transaction.ExpiredAt.Value
        ), ct);

        transaction.ProviderPaymentLinkId = result.PaymentLinkId;
        transaction.PaymentQrCode = result.QrCode;
        await db.SaveChangesAsync(ct);

        return TransactionMapping.ToResponse(transaction, campaignId, result.CheckoutUrl);
    }

    // 4A. Đối soát trạng thái thanh toán chủ động từ ReturnUrl
    public async Task<CampaignResponse> SyncCampaignPaymentStatusAsync(Guid accountId, string orderCode, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);

        var transaction = await db.Transactions
            .Include(t => t.AdvertisementTransaction)
            .FirstOrDefaultAsync(t => t.ProviderOrderCode == orderCode && t.UserId == partnerProfile.Id, ct)
            ?? throw new AdvertisementException("TRANSACTION_NOT_FOUND", "Không tìm thấy giao dịch.", 404);

        if (transaction.AdvertisementTransaction == null)
            throw new AdvertisementException("INVALID_TRANSACTION", "Giao dịch này không thuộc chiến dịch quảng cáo.", 400);

        var campaignId = transaction.AdvertisementTransaction.CampaignId;

        if (transaction.Status != PaymentStatus.Success)
        {
            var providerResult = await provider.GetPaymentStatusAsync(orderCode, ct);

            if (providerResult.Status == PaymentStatus.Success)
            {
                await ProcessPaymentCallbackAsync(orderCode, isSuccess: true, ct);
            }
            else if (providerResult.Status == PaymentStatus.Failed)
            {
                await ProcessPaymentCallbackAsync(orderCode, isSuccess: false, ct);
            }
        }

        var updatedCampaign = await db.AdvertisementCampaigns.AsNoTracking()
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct)
            ?? throw NotFound();

        return MapToResponse(updatedCampaign, partnerProfile.FullName ?? "Doanh nghiệp", updatedCampaign.Package?.Name);
    }

    // 4B. Callback thanh toán & kích hoạt AI Review (Pass -> Active | Fail -> PendingModeration)
    public async Task ProcessPaymentCallbackAsync(string providerOrderCode, bool isSuccess, CancellationToken ct = default)
    {
        var transaction = await db.Transactions
            .Include(t => t.AdvertisementTransaction)
            .FirstOrDefaultAsync(t => t.ProviderOrderCode == providerOrderCode, ct);

        if (transaction == null || transaction.AdvertisementTransaction == null) return;
        if (transaction.Status == PaymentStatus.Success) return;

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == transaction.AdvertisementTransaction.CampaignId, ct);

        if (campaign == null) return;

        var now = DateTimeOffset.UtcNow;
        if (isSuccess)
        {
            transaction.Status = PaymentStatus.Success;
            transaction.PaidAt = now;

            bool isSuitable = false;
            try
            {
                isSuitable = await aiModeration.ReviewContentAsync(campaign.Title, campaign.Content, ct);
            }
            catch
            {
                isSuitable = false;
            }

            if (isSuitable)
            {
                campaign.Status = AdCampaignStatus.Active;
                campaign.RejectionReason = null;
                campaign.StartedAt = now;
                if (campaign.Package?.DurationInDays.HasValue == true)
                {
                    campaign.ExpiredAt = now.AddDays(campaign.Package.DurationInDays.Value);
                }
            }
            else
            {
                // AI không duyệt -> Chuyển sang PendingModeration, không gán RejectionReason
                campaign.Status = AdCampaignStatus.PendingModeration;
                campaign.RejectionReason = "AI phát hiện nội dung có chứa từ khóa nhạy cảm, chuyển sang bộ phận kiểm duyệt thủ công.";
            }
        }
        else
        {
            transaction.Status = PaymentStatus.Failed;
        }

        transaction.UpdatedAt = now;
        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    // 5. Admin duyệt tay (Dành cho các bài đã thanh toán nhưng rớt AI)
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
            // Chặn duyệt xuất bản nếu chiến dịch đã quá hạn SLA 24h và Partner đã gửi yêu cầu hoàn tiền
            var hasLockedRefund = await db.RefundTransactions.AsNoTracking()
                .AnyAsync(r => r.Transaction.AdvertisementTransaction != null
                            && r.Transaction.AdvertisementTransaction.CampaignId == campaignId
                            && (r.Approval == RefundApproval.Pending || r.Approval == RefundApproval.Approved), ct);

            Require(!hasLockedRefund,
                "CAMPAIGN_REFUND_LOCKED",
                "Chiến dịch này đã quá hạn kiểm duyệt và Partner đã gửi yêu cầu hoàn tiền, không thể duyệt xuất bản.", 409);

            campaign.Status = AdCampaignStatus.Active;
            campaign.RejectionReason = null;
            campaign.StartedAt = now;
            if (campaign.Package?.DurationInDays.HasValue == true)
            {
                campaign.ExpiredAt = now.AddDays(campaign.Package.DurationInDays.Value);
            }
        }
        else
        {
            campaign.Status = AdCampaignStatus.Rejected;
            campaign.RejectionReason = string.IsNullOrWhiteSpace(request.Reason)
                ? "Nội dung quảng cáo không phù hợp với tiêu chuẩn cộng đồng."
                : request.Reason.Trim();
        }

        campaign.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return MapToResponse(campaign, campaign.CompanyUser.FullName ?? "Doanh nghiệp", campaign.Package?.Name);
    }

    // 6. Partner chỉnh sửa & duyệt lại (Hỗ trợ upload ảnh mới qua MediaFile)
    public async Task<CampaignResponse> UpdateCampaignAsync(Guid accountId, Guid campaignId, UpdateCampaignRequest request, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        ValidateCampaignFields(request.Title, request.Content, request.DestinationUrl);

        var campaign = await db.AdvertisementCampaigns
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.Status is AdCampaignStatus.Draft or AdCampaignStatus.PendingModeration or AdCampaignStatus.Rejected,
            "INVALID_CAMPAIGN_STATE", "Only Draft, PendingModeration, or Rejected campaigns can be updated.", 409);

        if (campaign.Status is AdCampaignStatus.PendingModeration or AdCampaignStatus.Rejected)
        {
            var hasLockedRefund = await db.RefundTransactions.AsNoTracking()
                .AnyAsync(r => r.Transaction.AdvertisementTransaction != null
                            && r.Transaction.AdvertisementTransaction.CampaignId == campaignId
                            && (r.Approval == RefundApproval.Pending || r.Approval == RefundApproval.Approved), ct);

            Require(!hasLockedRefund,
                "CAMPAIGN_REFUND_LOCKED",
                "Chiến dịch đang trong quá trình yêu cầu hoàn tiền hoặc đã hoàn tiền, không thể chỉnh sửa.", 409);
        }

        campaign.Title = request.Title.Trim();
        campaign.Content = request.Content.Trim();
        campaign.DestinationUrl = request.DestinationUrl.Trim();

        if (request.MediaFile is { Length: > 0 })
        {
            campaign.MediaUrl = await UploadCampaignImageAsync(campaign.Id, request.MediaFile, ct);
        }

        if (campaign.Status is AdCampaignStatus.PendingModeration or AdCampaignStatus.Rejected)
        {
            bool isSuitable = false;
            try
            {
                isSuitable = await aiModeration.ReviewContentAsync(campaign.Title, campaign.Content, ct);
            }
            catch
            {
                isSuitable = false;
            }

            if (isSuitable)
            {
                campaign.Status = AdCampaignStatus.Active;
                campaign.RejectionReason = null;
                campaign.StartedAt = DateTimeOffset.UtcNow;
                if (campaign.Package?.DurationInDays.HasValue == true)
                {
                    campaign.ExpiredAt = DateTimeOffset.UtcNow.AddDays(campaign.Package.DurationInDays.Value);
                }
            }
            else
            {
                campaign.Status = AdCampaignStatus.PendingModeration;
                campaign.RejectionReason = "AI phát hiện nội dung có chứa từ khóa nhạy cảm, chuyển sang bộ phận kiểm duyệt thủ công.";
            }
        }

        campaign.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", campaign.Package?.Name);
    }

    // 7A. Partner gửi yêu cầu hoàn tiền (Chỉ mở khi Rejected HOẶC PendingModeration quá 24h)
    public async Task<RefundTransactionResponse> RequestCampaignRefundAsync(
        Guid accountId,
        Guid campaignId,
        RequestRefundRequest request,
        CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);

        Require(!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Trim().Length <= 500,
            "INVALID_REFUND_REASON", "Vui lòng nhập lý do hoàn tiền (tối đa 500 ký tự).", 400);
        Require(!string.IsNullOrWhiteSpace(request.ManualPayingAccount) && request.ManualPayingAccount.Trim().Length <= 255,
            "INVALID_BANK_ACCOUNT", "Vui lòng cung cấp thông tin tài khoản ngân hàng nhận tiền hoàn.", 400);

        if (request.RequestKey.HasValue)
        {
            var existingByKey = await db.RefundTransactions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RequestKey == request.RequestKey.Value, ct);
            if (existingByKey != null)
            {
                return MapToRefundResponse(existingByKey);
            }
        }

        var campaign = await db.AdvertisementCampaigns.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        Require(campaign.StartedAt == null,
            "CAMPAIGN_ALREADY_STARTED",
            "Chiến dịch đã từng phân phối nên không đủ điều kiện hoàn tiền.", 409);

        var transaction = await db.Transactions.AsNoTracking()
            .Where(t => t.AdvertisementTransaction != null
                     && t.AdvertisementTransaction.CampaignId == campaignId
                     && t.UserId == partnerProfile.Id
                     && t.Status == PaymentStatus.Success)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new AdvertisementException("TRANSACTION_NOT_PAID",
                "Không tìm thấy giao dịch thanh toán thành công cho chiến dịch này.", 409);

        if (campaign.Status == AdCampaignStatus.PendingModeration)
        {
            var paidAt = transaction.PaidAt ?? transaction.UpdatedAt;
            var unlockRefundAt = paidAt.AddHours(ModerationSlaHours);
            var unlockLocalTime = unlockRefundAt.ToOffset(TimeSpan.FromHours(7));
            Require(DateTimeOffset.UtcNow >= unlockRefundAt,
                "MODERATION_SLA_NOT_EXPIRED",
                $"Chiến dịch đang được Ban quản trị kiểm duyệt (tối đa {ModerationSlaHours} giờ). Bạn chỉ có thể yêu cầu hoàn tiền nếu bị từ chối hoặc sau {unlockRefundAt:dd/MM/yyyy HH:mm} UTC.", 409);
        }
        else
        {
            Require(campaign.Status == AdCampaignStatus.Rejected,
                "CAMPAIGN_NOT_ELIGIBLE_FOR_REFUND",
                $"Chiến dịch ở trạng thái {campaign.Status} không đủ điều kiện yêu cầu hoàn tiền.", 409);
        }

        var hasActiveRefund = await db.RefundTransactions.AsNoTracking()
            .AnyAsync(r => r.TransactionId == transaction.Id &&
                           (r.Approval == RefundApproval.Pending ||
                            r.Approval == RefundApproval.Approved ||
                            r.Status == PaymentStatus.Success), ct);

        Require(!hasActiveRefund,
            "REFUND_ALREADY_REQUESTED", "Giao dịch này đã có yêu cầu hoàn tiền đang được xử lý hoặc đã hoàn tất.", 409);

        var now = DateTimeOffset.UtcNow;
        var refund = new RefundTransaction
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            RequestKey = request.RequestKey ?? Guid.NewGuid(),
            RequestedByUserId = partnerProfile.Id,
            RequestedAt = now,
            RequestSource = RefundRequestSource.System,
            Approval = RefundApproval.Pending,
            Status = PaymentStatus.Pending,
            Amount = transaction.Amount,
            PaymentProvider = transaction.PaymentMethod ?? "PayOS",
            Reason = request.Reason.Trim(),
            ManualPayingAccount = request.ManualPayingAccount.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.RefundTransactions.Add(refund);
        await db.SaveChangesAsync(ct);

        return MapToRefundResponse(refund);
    }

    // 7B. Lấy danh sách yêu cầu hoàn tiền cho Admin
    public async Task<RefundTransactionsResponse> ListRefundRequestsAsync(
        Guid accountId,
        RefundApproval? approval = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        await EnsureAdminAsync(accountId, ct);

        var query = db.RefundTransactions.AsNoTracking()
            .Include(r => r.Transaction)
                .ThenInclude(t => t.AdvertisementTransaction)
            .Where(r => r.Transaction.AdvertisementTransaction != null);

        if (approval.HasValue)
        {
            query = query.Where(r => r.Approval == approval.Value);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var responses = items.Select(MapToRefundResponse).ToList();
        return new(page, pageSize, total, (int)Math.Ceiling((double)total / pageSize), responses);
    }

    // 7C. Admin duyệt hoặc từ chối yêu cầu hoàn tiền
    public async Task<RefundTransactionResponse> ReviewCampaignRefundAsync(
        Guid accountId,
        Guid refundId,
        ReviewRefundRequest request,
        CancellationToken ct = default)
    {
        await EnsureAdminAsync(accountId, ct);

        var refund = await db.RefundTransactions
            .Include(r => r.Transaction)
                .ThenInclude(t => t.AdvertisementTransaction)
            .FirstOrDefaultAsync(r => r.Id == refundId && r.Transaction.AdvertisementTransaction != null, ct)
            ?? throw new AdvertisementException("REFUND_NOT_FOUND", "Không tìm thấy yêu cầu hoàn tiền.", 404);

        Require(refund.Approval == RefundApproval.Pending,
            "INVALID_REFUND_STATE",
            $"Chỉ có thể duyệt yêu cầu hoàn tiền đang ở trạng thái Pending. Trạng thái hiện tại: {refund.Approval}.", 409);

        var now = DateTimeOffset.UtcNow;
        refund.ReviewedByAccountId = accountId;
        refund.ReviewedAt = now;
        refund.UpdatedAt = now;

        if (request.IsApproved)
        {
            Require(!string.IsNullOrWhiteSpace(request.ManualBankReference) && request.ManualBankReference.Trim().Length <= 255,
                "BANK_REFERENCE_REQUIRED",
                "Vui lòng nhập mã giao dịch chuyển khoản ngân hàng (ManualBankReference) khi duyệt hoàn tiền.", 400);

            refund.Approval = RefundApproval.Approved;
            refund.Status = PaymentStatus.Success;
            refund.ManualBankReference = request.ManualBankReference.Trim();
            refund.ConfirmedByAccountId = accountId;
            refund.CompletedAt = now;
            refund.ReviewReason = request.ReviewReason?.Trim();
            refund.FailureReason = null;

            var campaignId = refund.Transaction.AdvertisementTransaction!.CampaignId;
            var campaign = await db.AdvertisementCampaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
            if (campaign != null)
            {
                campaign.Status = AdCampaignStatus.Refunded;
                campaign.UpdatedAt = now;
            }
        }
        else
        {
            Require(!string.IsNullOrWhiteSpace(request.ReviewReason) && request.ReviewReason.Trim().Length <= 500,
                "REVIEW_REASON_REQUIRED",
                "Vui lòng nhập lý do từ chối yêu cầu hoàn tiền.", 400);

            refund.Approval = RefundApproval.Rejected;
            refund.Status = PaymentStatus.Failed;
            refund.ReviewReason = request.ReviewReason.Trim();
            refund.FailureReason = request.ReviewReason.Trim();
        }

        await db.SaveChangesAsync(ct);

        return MapToRefundResponse(refund);
    }

    // 8. Các API truy vấn & tracking
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

        var responses = items.Select(c => MapToResponse(c, partnerProfile.FullName ?? "Doanh nghiệp", c.Package?.Name)).ToList();
        return new(page, pageSize, total, (int)Math.Ceiling((double)total / pageSize), responses);
    }

    public async Task<CampaignResponse> GetCampaignByIdAsync(Guid accountId, Guid campaignId, CancellationToken ct = default)
    {
        var partnerProfile = await GetPartnerProfileAsync(accountId, ct);
        var campaign = await db.AdvertisementCampaigns.AsNoTracking()
            .Include(c => c.Package)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.CompanyUserId == partnerProfile.Id, ct)
            ?? throw NotFound();

        return MapToResponse(campaign, partnerProfile.FullName ?? "Doanh nghiệp", campaign.Package?.Name);
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

        var responses = items.Select(c => MapToResponse(c, c.CompanyUser.FullName ?? "Doanh nghiệp", c.Package?.Name)).ToList();
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

        return activeAds.Select(c => MapToResponse(c, c.CompanyUser.FullName ?? "Doanh nghiệp", c.Package?.Name)).ToList();
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

    // --- PRIVATE HELPERS ---
    private async Task<string> UploadCampaignImageAsync(Guid campaignId, IFormFile mediaFile, CancellationToken ct)
    {
        var extension = Path.GetExtension(mediaFile.FileName).ToLowerInvariant().TrimStart('.');
        if (extension is not ("png" or "jpg" or "jpeg" or "webp"))
        {
            throw new AdvertisementException("INVALID_IMAGE_FORMAT", $"Định dạng ảnh .{extension} không hợp lệ.", 400);
        }

        using var memoryStream = new MemoryStream();
        await mediaFile.CopyToAsync(memoryStream, ct);

        var publicId = $"advertisements/campaigns/{campaignId}/{Guid.NewGuid():N}";
        var format = extension == "jpeg" ? "jpg" : extension;

        var uploadResult = await imageStorage.Upload(
            new ImageUploadRequest(memoryStream.ToArray(), publicId, format, "image"),
            ct
        );
        if (string.IsNullOrWhiteSpace(uploadResult.ImageUrl))
        {
            throw new AdvertisementException("IMAGE_UPLOAD_FAILED", "Upload ảnh thất bại, không nhận được URL từ hệ thống lưu trữ.", 500);
        }

        return uploadResult.ImageUrl;
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

    private static void ValidateCampaignFields(string title, string content, string destinationUrl)
    {
        Require(!string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 255, "INVALID_TITLE", "Title is required.");
        Require(!string.IsNullOrWhiteSpace(content), "INVALID_CONTENT", "Content is required.");
        Require(!string.IsNullOrWhiteSpace(destinationUrl), "INVALID_URL", "Destination URL is required.");
    }

    private static CampaignResponse MapToResponse(AdvertisementCampaign c, string companyName, string? packageName) => new(
        c.Id,
        c.CompanyUserId,
        companyName,
        c.PackageId,
        packageName,
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

    private static RefundTransactionResponse MapToRefundResponse(RefundTransaction r) => new(
        r.Id,
        r.TransactionId,
        r.RequestedByUserId,
        r.Amount,
        r.PaymentProvider,
        r.Approval,
        r.Status,
        r.Reason,
        r.ManualPayingAccount ?? string.Empty,
        r.ReviewReason,
        r.ManualBankReference,
        r.RequestedAt,
        r.ReviewedAt,
        r.CreatedAt);

    private static AdvertisementException NotFound() => new("CAMPAIGN_NOT_FOUND", "Advertisement campaign was not found.", 404);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int statusCode = 400)
    {
        if (!condition) throw new AdvertisementException(code, message, statusCode);
    }
}