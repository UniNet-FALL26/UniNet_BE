using UniNet.Domain;

namespace UniNet.Application;

public record RegisterStudentRequest(string FullName, string Email, string Password, string ConfirmPassword, string? UniversityName, string? Major, string? Nickname = null);
public record RegisterAdminRequest(string FullName, string Email, string Password, string ConfirmPassword, string? Nickname = null);
public record RegisterPartnerRequest(string OrganizationName, string Email, string Password, string ConfirmPassword, PartnerType? PartnerType, string? FullName = null, string? Nickname = null);
public record CreateStaffAccountRequest(string FullName, string Email, string Password, string ConfirmPassword, AccountRole Role, string? Nickname = null);
public record LoginRequest(string Email, string Password, bool RememberMe = false, string? DeviceId = null, string? DeviceName = null);
public record GoogleLoginRequest(string IdToken, AccountRole Role = AccountRole.Student, PartnerType? PartnerType = null);
public record RefreshRequest(string RefreshToken);
public record StudentProfileRequest(string FullName, string? AvatarUrl, string? CoverUrl, string? Bio, string? UniversityName, string? Major, string? StudentCode, string? Nickname = null);
public record PartnerProfileRequest(string FullName, string? AvatarUrl, string? CoverUrl, string? Bio, PartnerType? PartnerType, string? Industry, string? Website, string? ContactEmail, string? Phone, string? Address, string OrganizationName, string? Nickname = null);
public record ProfileResponse(Guid Id, string FullName, string Nickname, string? OrganizationName, string? AvatarUrl, string? CoverUrl, string? Bio, string? UniversityName, string? Major, string? StudentCode, PartnerType? PartnerType, string? Industry, string? Website, string? ContactEmail, string? Phone, string? Address, bool IsVerified)
{
    public string DisplayName => Nickname;
}
public record AccountResponse(Guid Id, string Email, AccountRole Role, AccountStatus Status, bool EmailVerified, ProfileResponse? Profile, bool ProfileComplete);
public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, AccountResponse Account, bool RequiresProfileCompletion);
