using UniNet.Domain;

namespace UniNet.Application;

public static class AuthMapping
{
    public static bool ProfileComplete(Account account) => account.Profile is { } p &&
        (account.Role switch
        {
            AccountRole.Student => !string.IsNullOrWhiteSpace(p.Bio) && !string.IsNullOrWhiteSpace(p.UniversityName),
            AccountRole.Partner => p.PartnerType.HasValue && !string.IsNullOrWhiteSpace(p.Bio),
            _ => true
        });

    public static AccountResponse ToResponse(Account account)
    {
        var p = account.Profile;
        return new(account.Id, account.Email, account.Role, account.Status, account.EmailVerified,
            p is null ? null : new(p.Id, p.DisplayName, p.AvatarUrl, p.CoverUrl, p.Bio,
                p.UniversityName, p.Major, p.StudentCode, p.PartnerType, p.Industry, p.Website,
                p.ContactEmail, p.Phone, p.Address, p.TaxCode, p.IsVerified), ProfileComplete(account));
    }
}
