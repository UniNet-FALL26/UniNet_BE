using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.DTOs.Verification;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class PartnerVerificationService(UniNetDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PartnerVerificationStatusResponse> GetMyStatusAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await LoadPartnerProfileAsync(accountId, ct);
        var latest = await LatestPartnerVerification(profile.Id).AsNoTracking().FirstOrDefaultAsync(ct);
        return latest is null
            ? new(null, null, null, null)
            : new(latest.Id, latest.Status, latest.RejectReason, latest.SubmittedAt);
    }

    public async Task<PartnerVerificationStatusResponse> SubmitAsync(Guid accountId, SubmitPartnerVerificationRequest request, CancellationToken ct)
    {
        var profile = await LoadPartnerProfileAsync(accountId, ct);
        ValidateSubmission(request);
        var latest = await LatestPartnerVerification(profile.Id).FirstOrDefaultAsync(ct);
        if (latest?.Status == VerificationStatus.Pending)
            throw new AuthException("VERIFICATION_ALREADY_PENDING", "Yêu cầu xác minh đang chờ admin duyệt.", 409);
        if (latest?.Status == VerificationStatus.Approved && profile.IsVerified)
            throw new AuthException("PARTNER_ALREADY_VERIFIED", "Hồ sơ Partner đã được xác minh.", 409);

        var now = DateTimeOffset.UtcNow;
        var verification = new UserVerification
        {
            UserId = profile.Id,
            VerificationType = VerificationType.Partner,
            Status = VerificationStatus.Pending,
            VerificationData = JsonSerializer.Serialize(new
            {
                organizationName = profile.OrganizationName,
                partnerType = profile.PartnerType?.ToString(),
                taxCode = Clean(request.TaxCode),
                representativeName = Clean(request.RepresentativeName),
                industry = profile.Industry,
                website = profile.Website,
                contactEmail = profile.ContactEmail,
                phone = profile.Phone,
                address = profile.Address
            }, JsonOptions),
            DocumentUrls = JsonSerializer.Serialize(request.Documents, JsonOptions),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        profile.IsVerified = false;
        profile.UpdatedAt = now;
        db.UserVerifications.Add(verification);
        await db.SaveChangesAsync(ct);
        return new(verification.Id, verification.Status, null, verification.SubmittedAt);
    }

    public async Task<PartnerVerificationQueueResponse> ListPendingAsync(int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var query = db.UserVerifications.AsNoTracking()
            .Where(x => x.VerificationType == VerificationType.Partner && x.Status == VerificationStatus.Pending);
        var total = await query.CountAsync(ct);
        var rows = await query.Include(x => x.User).ThenInclude(x => x.Account)
            .OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, rows.Select(ToQueueItem).ToList());
    }

    public async Task<PartnerVerificationDetailResponse> GetPendingAsync(Guid verificationId, CancellationToken ct)
    {
        var verification = await LoadPendingAsync(verificationId, ct);
        return ToDetail(verification);
    }

    public async Task<PartnerVerificationDetailResponse> ApproveAsync(Guid reviewerAccountId, Guid verificationId, CancellationToken ct)
    {
        await EnsureActiveAdminAsync(reviewerAccountId, ct);
        var verification = await LoadPendingAsync(verificationId, ct);
        var now = DateTimeOffset.UtcNow;
        verification.Status = VerificationStatus.Approved;
        verification.RejectReason = null;
        verification.ReviewedBy = reviewerAccountId;
        verification.ReviewedAt = now;
        verification.UpdatedAt = now;
        verification.User.IsVerified = true;
        verification.User.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToDetail(verification);
    }

    public async Task<PartnerVerificationDetailResponse> RejectAsync(Guid reviewerAccountId, Guid verificationId, string reason, CancellationToken ct)
    {
        await EnsureActiveAdminAsync(reviewerAccountId, ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new AuthException("INVALID_REJECT_REASON", "Lý do từ chối bắt buộc và tối đa 2000 ký tự.");
        var verification = await LoadPendingAsync(verificationId, ct);
        var now = DateTimeOffset.UtcNow;
        verification.Status = VerificationStatus.Rejected;
        verification.RejectReason = reason.Trim();
        verification.ReviewedBy = reviewerAccountId;
        verification.ReviewedAt = now;
        verification.UpdatedAt = now;
        verification.User.IsVerified = false;
        verification.User.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToDetail(verification);
    }

    private IQueryable<UserVerification> LatestPartnerVerification(Guid userId) => db.UserVerifications
        .Where(x => x.UserId == userId && x.VerificationType == VerificationType.Partner)
        .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.CreatedAt);

    private async Task<UserProfile> LoadPartnerProfileAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new AuthException("ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại.", 404);
        if (account.Role != AccountRole.Partner || account.Status != AccountStatus.Active || account.Profile is null)
            throw new AuthException("FORBIDDEN", "Chỉ Partner đang hoạt động mới dùng được chức năng xác minh.", 403);
        return account.Profile;
    }

    private async Task EnsureActiveAdminAsync(Guid accountId, CancellationToken ct)
    {
        var isAdmin = await db.Accounts.AnyAsync(x => x.Id == accountId && x.Role == AccountRole.Admin && x.Status == AccountStatus.Active, ct);
        if (!isAdmin) throw new AuthException("FORBIDDEN", "Chỉ admin đang hoạt động mới được duyệt xác minh.", 403);
    }

    private async Task<UserVerification> LoadPendingAsync(Guid id, CancellationToken ct)
        => await db.UserVerifications.Include(x => x.User).ThenInclude(x => x.Account)
            .SingleOrDefaultAsync(x => x.Id == id && x.VerificationType == VerificationType.Partner && x.Status == VerificationStatus.Pending, ct)
            ?? throw new AuthException("VERIFICATION_NOT_FOUND", "Không tìm thấy yêu cầu đang chờ duyệt.", 404);

    private static void ValidateSubmission(SubmitPartnerVerificationRequest request)
    {
        if (request.TaxCode?.Length > 50) throw new AuthException("INVALID_VERIFICATION_DATA", "Mã số thuế tối đa 50 ký tự.");
        if (request.RepresentativeName?.Length > 255) throw new AuthException("INVALID_VERIFICATION_DATA", "Tên người đại diện tối đa 255 ký tự.");
        if (request.Documents is null || request.Documents.Count is < 1 or > 10)
            throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Cần gửi từ 1 đến 10 tài liệu xác minh.");
        if (request.Documents.Any(x => x is null || !Enum.IsDefined(x.Type) || string.IsNullOrWhiteSpace(x.FileName) || x.FileName.Trim().Length > 255 || string.IsNullOrWhiteSpace(x.Url) || !IsHttpsUrl(x.Url)))
            throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Tài liệu phải có loại hợp lệ, tên tối đa 255 ký tự và URL HTTPS hợp lệ.");
    }

    private static bool IsHttpsUrl(string value)
        => value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1) throw new AuthException("INVALID_PAGE", "Page phải lớn hơn hoặc bằng 1.");
        if (pageSize is < 1 or > 100) throw new AuthException("INVALID_PAGE_SIZE", "Page size phải từ 1 đến 100.");
    }

    private static PartnerVerificationQueueItem ToQueueItem(UserVerification x)
        => new(x.Id, x.UserId, x.User.Account.Email, x.User.OrganizationName ?? x.User.FullName, x.User.PartnerType, x.Status, x.SubmittedAt);

    private static PartnerVerificationDetailResponse ToDetail(UserVerification x)
        => new(x.Id, x.UserId, x.User.Account.Email, x.User.OrganizationName ?? x.User.FullName, x.User.PartnerType,
            x.Status, x.VerificationData, x.DocumentUrls, x.RejectReason, x.SubmittedAt, x.ReviewedAt);
}
