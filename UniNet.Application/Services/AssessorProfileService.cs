using Microsoft.EntityFrameworkCore;
using UniNet.Domain;

namespace UniNet.Application.Services;

public sealed partial class ProfileService
{
    private static string? ProfessionalLink(string? value)
    {
        var text = Optional(value); Max(text, 2048, "Liên kết");
        if (text != null && (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new AuthException("INVALID_PROFILE", "Liên kết phải là URL http hoặc https hợp lệ.");
        return text;
    }

    private static AssessorProfileData? AssessorData(AssessorProfileDetails? details)
    {
        if (details == null) return null;
        Max(details.JobTitle, 150, "Chức danh"); Max(details.CompanyName, 255, "Công ty");
        Max(details.Education, 255, "Trình độ học vấn"); Max(details.University, 255, "Trường đại học");
        if (details.YearsOfExperience is < 0 or > 80)
            throw new AuthException("INVALID_PROFILE", "Số năm kinh nghiệm phải từ 0 đến 80.");
        if (details.Expertise?.Length > 30 || details.Expertise?.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 100) == true)
            throw new AuthException("INVALID_PROFILE", "Tối đa 30 lĩnh vực chuyên môn, mỗi lĩnh vực từ 1 đến 100 ký tự.");
        return new()
        {
            JobTitle = Optional(details.JobTitle), CompanyName = Optional(details.CompanyName), YearsOfExperience = details.YearsOfExperience,
            Expertise = (details.Expertise ?? []).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Education = Optional(details.Education), University = Optional(details.University), LinkedInUrl = ProfessionalLink(details.LinkedInUrl),
            GitHubUrl = ProfessionalLink(details.GitHubUrl), PortfolioUrl = ProfessionalLink(details.PortfolioUrl)
        };
    }

    public async Task<AccountResponse> Assessor(Guid accountId, AssessorProfileRequest request, CancellationToken ct)
    {
        var account = await Load(accountId, AccountRole.Assessor, ct);
        ValidateName(request.FullName); Max(request.Bio, 4000, "Giới thiệu"); Max(request.ContactEmail, 255, "Email liên hệ");
        Max(request.Phone, 30, "Điện thoại"); Max(request.Address, 2000, "Địa chỉ"); ValidateContact(request.ContactEmail, request.Phone);
        var details = AssessorData(request.AssessorJson); var p = account.Profile!;
        var nickname = Nickname(request.Nickname, p.Nickname);
        await PortraitAssetPolicy.RetainReference(db, accountId, request.AvatarUrl, p.AvatarUrl, ct);
        await PortraitAssetPolicy.RetainReference(db, accountId, request.CoverUrl, p.CoverUrl, ct);
        p.FullName = request.FullName.Trim(); p.Nickname = nickname; p.AvatarUrl = Optional(request.AvatarUrl); p.CoverUrl = Optional(request.CoverUrl);
        p.Bio = Optional(request.Bio); p.ContactEmail = Optional(request.ContactEmail); p.Phone = Optional(request.Phone); p.Address = Optional(request.Address);
        p.AssessorJson = details; p.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); return AuthMapping.ToResponse(account);
    }

    public async Task<AccountResponse> ClearAssessor(Guid accountId, CancellationToken ct)
    {
        var account = await Load(accountId, AccountRole.Assessor, ct);
        account.Profile!.AssessorJson = null; account.Profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); return AuthMapping.ToResponse(account);
    }
}
