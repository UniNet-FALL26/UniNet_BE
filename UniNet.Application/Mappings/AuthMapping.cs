using UniNet.Domain;

namespace UniNet.Application;

public static class AuthMapping
{
    public static bool ProfileComplete(Account account) => account.Profile is { } p &&
        (account.Role switch
        {
            AccountRole.Student => !string.IsNullOrWhiteSpace(p.Bio) && !string.IsNullOrWhiteSpace(p.UniversityName),
            AccountRole.Partner => p.PartnerType.HasValue && !string.IsNullOrWhiteSpace(p.OrganizationName) && !string.IsNullOrWhiteSpace(p.Bio),
            _ => true
        });

    public static AccountResponse ToResponse(Account account)
    {
        var p = account.Profile;
        ProfileResponse? profile = null;
        if (p is not null)
        {
            var websites = PartnerWebsiteLinks.ParseStored(p.Website);
            profile = new(p.Id, p.FullName, p.Nickname, p.OrganizationName, p.AvatarUrl, p.CoverUrl, p.Bio,
                p.UniversityName, p.Major, p.StudentCode, p.PartnerType, p.Industry, websites.FirstOrDefault(),
                p.ContactEmail, p.Phone, p.Address, p.IsVerified) { Websites = websites };
        }
        return new(account.Id, account.Email, account.Role, account.Status, account.EmailVerified,
            profile, ProfileComplete(account)) { CreatedAt = account.CreatedAt };
    }
}
