using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Infrastructure.Data;
using UniNet.Domain;

namespace UniNet.Application.Services;

public sealed class ProfileService(UniNetDbContext db)
{
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 255)
            throw new AuthException("INVALID_NAME", "Tên phải có từ 1 đến 255 ký tự.");
    }
    private static void Max(string? value, int limit, string field)
    {
        if (value?.Length > limit) throw new AuthException("INVALID_PROFILE", $"{field} quá dài.");
    }
    private static void ValidatePartnerContact(PartnerProfileRequest request)
    {
        var email = request.ContactEmail?.Trim();
        if (!string.IsNullOrEmpty(email) &&
            (!System.Net.Mail.MailAddress.TryCreate(email, out var address) ||
             !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase) ||
             !email.Contains('.')))
            throw new AuthException("INVALID_PROFILE", "Email liên hệ không đúng định dạng.");

        var website = request.Website?.Trim();
        if (!string.IsNullOrEmpty(website) &&
            (!Uri.TryCreate(website, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
             string.IsNullOrWhiteSpace(uri.Host)))
            throw new AuthException("INVALID_PROFILE", "Website phải là URL http hoặc https hợp lệ.");

        var phone = request.Phone?.Trim();
        if (!string.IsNullOrEmpty(phone))
        {
            var digitCount = phone.Count(char.IsDigit);
            if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[0-9().\-\s]+$") ||
                digitCount is < 7 or > 15 ||
                phone.Count(value => value == '+') > 1)
                throw new AuthException("INVALID_PROFILE", "Số điện thoại chỉ được chứa chữ số và ký hiệu + ( ) . -, dài từ 7 đến 15 chữ số.");
        }
    }
    private static string Nickname(string? value, string current)
    {
        if (value is null) return current;
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100)
            throw new AuthException("INVALID_NICKNAME", "Biệt danh phải có từ 1 đến 100 ký tự.");
        return value.Trim();
    }
    private async Task<Account> Load(Guid accountId, AccountRole role, CancellationToken ct)
    {
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new AuthException("ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại.", 404);
        if (account.Role != role) throw new AuthException("FORBIDDEN_ROLE", "Không có quyền cập nhật hồ sơ này.", 403);
        if (account.Status != AccountStatus.Active) throw new AuthException("ACCOUNT_SUSPENDED", "Tài khoản không hoạt động.", 403);
        if (account.Profile is null) throw new AuthException("PROFILE_NOT_FOUND", "Không tìm thấy hồ sơ.", 404);
        return account;
    }
    public async Task<AccountResponse> Student(Guid accountId, StudentProfileRequest request, CancellationToken ct)
    {
        ValidateName(request.FullName);
        Max(request.UniversityName, 255, "Trường đại học"); Max(request.Major, 255, "Ngành học"); Max(request.StudentCode, 50, "Mã sinh viên");
        var account = await Load(accountId, AccountRole.Student, ct);
        var p = account.Profile!;
        await PortraitAssetPolicy.Retain(db, accountId, request.AvatarUrl, p.AvatarUrl, ct);
        await PortraitAssetPolicy.RetainReference(db, accountId, request.CoverUrl, p.CoverUrl, ct);
        if (p.FullName != request.FullName.Trim() || p.UniversityName != Optional(request.UniversityName) || p.Major != Optional(request.Major) || p.StudentCode != Optional(request.StudentCode)) p.IsVerified = false;
        p.FullName = request.FullName.Trim(); p.Nickname = Nickname(request.Nickname, p.Nickname); p.AvatarUrl = Optional(request.AvatarUrl); p.CoverUrl = Optional(request.CoverUrl);
        p.Bio = Optional(request.Bio); p.UniversityName = Optional(request.UniversityName); p.Major = Optional(request.Major);
        p.StudentCode = Optional(request.StudentCode); p.PartnerType = null; p.Industry = null; p.Website = null;
        p.ContactEmail = null; p.OrganizationName = null;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return AuthMapping.ToResponse(account);
    }
    public async Task<AccountResponse> Partner(Guid accountId, PartnerProfileRequest request, CancellationToken ct)
    {
        ValidateName(request.OrganizationName);
        if (request.PartnerType is not { } partnerType || !Enum.IsDefined(partnerType)) throw new AuthException("INVALID_PARTNER_TYPE", "Loại đối tác không hợp lệ.");
        Max(request.Industry, 150, "Lĩnh vực"); Max(request.ContactEmail, 255, "Email liên hệ"); Max(request.Phone, 30, "Số điện thoại");
        Max(request.Website, 2048, "Website"); Max(request.Address, 2000, "Địa chỉ");
        ValidatePartnerContact(request);
        var account = await Load(accountId, AccountRole.Partner, ct);
        var p = account.Profile!;
        await PortraitAssetPolicy.RetainReference(db, accountId, request.AvatarUrl, p.AvatarUrl, ct);
        await PortraitAssetPolicy.RetainReference(db, accountId, request.CoverUrl, p.CoverUrl, ct);
        var organizationName = request.OrganizationName.Trim();
        var changed = p.FullName != organizationName || p.OrganizationName != organizationName || p.PartnerType != request.PartnerType ||
            p.Industry != Optional(request.Industry) || p.Website != Optional(request.Website) || p.ContactEmail != Optional(request.ContactEmail) ||
            p.Phone != Optional(request.Phone) || p.Address != Optional(request.Address);
        if (changed)
        {
            p.IsVerified = false;
            var pendingOrApproved = await db.UserVerifications
                .Where(x => x.UserId == p.Id && x.VerificationType == VerificationType.Partner &&
                    (x.Status == VerificationStatus.Pending || x.Status == VerificationStatus.Approved))
                .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            if (pendingOrApproved is not null)
            {
                pendingOrApproved.Status = VerificationStatus.Cancelled;
                pendingOrApproved.RejectReason = "Thông tin hồ sơ đã thay đổi sau khi gửi xác minh; vui lòng gửi yêu cầu mới.";
                pendingOrApproved.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        p.FullName = organizationName; p.Nickname = organizationName[..Math.Min(organizationName.Length, 100)]; p.OrganizationName = organizationName;
        p.AvatarUrl = Optional(request.AvatarUrl); p.CoverUrl = Optional(request.CoverUrl); p.Bio = Optional(request.Bio);
        p.PartnerType = request.PartnerType; p.Industry = Optional(request.Industry); p.Website = Optional(request.Website);
        p.ContactEmail = Optional(request.ContactEmail); p.Phone = Optional(request.Phone); p.Address = Optional(request.Address);
        p.UniversityName = null; p.Major = null; p.StudentCode = null;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return AuthMapping.ToResponse(account);
    }
}
