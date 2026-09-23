using UniNet.Domain;

namespace UniNet.Application;

public record RegisterStudentRequest(string FullName, string Email, string Password, string ConfirmPassword, string? UniversityName, string? Major);
public record RegisterPartnerRequest(string DisplayName, string Email, string Password, string ConfirmPassword, PartnerType? PartnerType);
public record LoginRequest(string Email, string Password, bool RememberMe = false, string? DeviceId = null, string? DeviceName = null);
public record GoogleLoginRequest(string IdToken, AccountRole Role = AccountRole.Student, PartnerType? PartnerType = null);
public record RefreshRequest(string RefreshToken);
public record StudentProfileRequest(string DisplayName, string? AvatarUrl, string? CoverUrl, string? Bio, string? UniversityName, string? Major, string? StudentCode);
public record PartnerProfileRequest(string DisplayName, string? AvatarUrl, string? CoverUrl, string? Bio, PartnerType? PartnerType, string? Industry, string? Website, string? ContactEmail, string? Phone, string? Address, string? TaxCode);
public record ProfileResponse(Guid Id, string DisplayName, string? AvatarUrl, string? CoverUrl, string? Bio, string? UniversityName, string? Major, string? StudentCode, PartnerType? PartnerType, string? Industry, string? Website, string? ContactEmail, string? Phone, string? Address, string? TaxCode, bool IsVerified);
public record AccountResponse(Guid Id, string Email, AccountRole Role, AccountStatus Status, bool EmailVerified, ProfileResponse? Profile, bool ProfileComplete);
public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, AccountResponse Account, bool RequiresProfileCompletion);
