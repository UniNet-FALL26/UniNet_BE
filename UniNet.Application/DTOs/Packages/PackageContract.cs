using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Packages;

public sealed record SavePackageRequest(
    string Code, string Name, PackageType Type, TargetRole Role,
    decimal Price, int? DurationInDays, bool IsActive);

public sealed record PackageResponse(
    Guid Id, string Code, string Name, PackageType Type, TargetRole Role,
    decimal Price, int? DurationInDays, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record PackagesResponse(
    int Page, int PageSize, int TotalItems, int TotalPages,
    IReadOnlyList<PackageResponse> Packages);
