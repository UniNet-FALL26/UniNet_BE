using System.Globalization;

namespace UniNet.Application.Services;

public sealed partial class ProfileService
{
    private static PartnerIntroductionDetails ValidateIntroduction(PartnerIntroductionDetails value)
    {
        static string? ImageUrl(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var url = input.Trim();
            if (url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
                parsed.Scheme is not ("https" or "http") || string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
                throw new AuthException("INVALID_PROFILE", "Ảnh phải là URL http/https hợp lệ, tối đa 2.048 ký tự.");
            return url;
        }
        var foundedOn = Optional(value.FoundedOn);
        if (foundedOn is not null && (!DateOnly.TryParseExact(foundedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date > DateOnly.FromDateTime(DateTime.UtcNow)))
            throw new AuthException("INVALID_PROFILE", "Ngày thành lập phải hợp lệ và không ở tương lai.");
        Max(value.EmployeeRange, 100, "Quy mô nhân sự");
        var branches = value.Branches ?? [];
        var gallery = value.GalleryUrls ?? [];
        if (branches.Length > 20 || branches.Any(x => x is null || x.Length > 255) || gallery.Length > 20 || gallery.Any(string.IsNullOrWhiteSpace))
            throw new AuthException("INVALID_PROFILE", "Tối đa 20 chi nhánh và 20 ảnh; mỗi chi nhánh tối đa 255 ký tự.");
        return new(ImageUrl(value.ImageUrl), foundedOn, Optional(value.EmployeeRange),
            branches.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray(), gallery.Select(x => ImageUrl(x)!).Distinct().ToArray());
    }
}
