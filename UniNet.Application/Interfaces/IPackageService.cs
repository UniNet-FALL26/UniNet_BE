using UniNet.Application.DTOs.Packages;
using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IPackageService
{
    Task<PackagesResponse> ListPackagesAsync(Guid accountId, PackageType? type, TargetRole? role,
        bool? isActive, string? keyword, int page, int pageSize, CancellationToken ct);
    Task<PackageResponse> GetPackageAsync(Guid accountId, Guid packageId, CancellationToken ct);
    Task<PackagesResponse> ListAllPackagesAsync(Guid accountId, PackageType? type, TargetRole? role,
        bool? isActive, string? keyword, int page, int pageSize, CancellationToken ct);
    Task<PackageResponse> TogglePackageStatusAsync(Guid accountId, Guid packageId, CancellationToken ct);
    Task<PackageResponse> CreatePackageAsync(Guid accountId, SavePackageRequest request, CancellationToken ct);
    Task<PackageResponse> UpdatePackageAsync(Guid accountId, Guid packageId, SavePackageRequest request, CancellationToken ct);
    Task DeletePackageAsync(Guid accountId, Guid packageId, CancellationToken ct);
}
