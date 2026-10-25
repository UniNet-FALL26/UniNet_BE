using UniNet.Domain;

namespace UniNet.Application.Services;

public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record AdminUserQuery(int Page = 1, int PageSize = 20, string? Search = null, AccountRole? Role = null,
    AccountStatus? Status = null, string? Verification = null, string Sort = "createdAt", bool Desc = true, bool StaffOnly = false);
public sealed record AdminUserItem(Guid Id, Guid? ProfileId, string FullName, string Nickname, string Email, string? AvatarUrl,
    AccountRole Role, AccountStatus Status, DateTimeOffset CreatedAt, bool IsVerified, VerificationStatus? VerificationStatus);
public sealed record AdminUserStats(int Total, int Active, int Locked, int Verified);
public sealed record AdminPublicProfile(string? CoverUrl, string? Bio, string? Phone, string? UniversityName, string? Major,
    string? StudentCode, string? OrganizationName, PartnerType? PartnerType, string? Industry, string? Website, string? ContactEmail, string? Address);
public sealed record AdminVerificationHistory(Guid Id, VerificationType Type, VerificationStatus Status, DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt, string? Reason, Guid? ReviewedBy, string? ReviewerName);
public sealed record AdminStatusHistory(Guid Id, AccountStatus PreviousStatus, AccountStatus Status, string Reason, Guid ActorAccountId, string ActorName, DateTimeOffset CreatedAt);
public sealed record AdminUserDetail(AdminUserItem Account, AdminPublicProfile? Profile, IReadOnlyList<AdminVerificationHistory> Verifications, IReadOnlyList<AdminStatusHistory> StatusHistory);
public sealed record ChangeAccountStatusRequest(string Reason);
public sealed record PermissionAssignment(string Permission, bool Granted);
public sealed record AdminPermissionEntry(string Permission, bool Granted, bool? Override);
