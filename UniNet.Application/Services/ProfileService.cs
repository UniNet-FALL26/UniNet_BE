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
        ValidateName(request.DisplayName);
        Max(request.UniversityName, 255, "Trường đại học"); Max(request.Major, 255, "Ngành học"); Max(request.StudentCode, 50, "Mã sinh viên");
        var account = await Load(accountId, AccountRole.Student, ct);
        var p = account.Profile!;
        p.DisplayName = request.DisplayName.Trim(); p.AvatarUrl = Optional(request.AvatarUrl); p.CoverUrl = Optional(request.CoverUrl);
        p.Bio = Optional(request.Bio); p.UniversityName = Optional(request.UniversityName); p.Major = Optional(request.Major);
        p.StudentCode = Optional(request.StudentCode); p.PartnerType = null; p.Industry = null; p.Website = null;
        p.ContactEmail = null; p.Phone = null; p.Address = null; p.TaxCode = null;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return AuthMapping.ToResponse(account);
    }
    public async Task<AccountResponse> Partner(Guid accountId, PartnerProfileRequest request, CancellationToken ct)
    {
        ValidateName(request.DisplayName);
        if (request.PartnerType is not { } partnerType || !Enum.IsDefined(partnerType)) throw new AuthException("INVALID_PARTNER_TYPE", "Loại đối tác không hợp lệ.");
        Max(request.Industry, 150, "Lĩnh vực"); Max(request.ContactEmail, 255, "Email liên hệ"); Max(request.Phone, 30, "Số điện thoại"); Max(request.TaxCode, 50, "Mã số thuế");
        var account = await Load(accountId, AccountRole.Partner, ct);
        var p = account.Profile!;
        p.DisplayName = request.DisplayName.Trim(); p.AvatarUrl = Optional(request.AvatarUrl); p.CoverUrl = Optional(request.CoverUrl);
        p.Bio = Optional(request.Bio); p.PartnerType = request.PartnerType; p.Industry = Optional(request.Industry);
        p.Website = Optional(request.Website); p.ContactEmail = Optional(request.ContactEmail); p.Phone = Optional(request.Phone);
        p.Address = Optional(request.Address); p.TaxCode = Optional(request.TaxCode);
        p.UniversityName = null; p.Major = null; p.StudentCode = null;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return AuthMapping.ToResponse(account);
    }
}
