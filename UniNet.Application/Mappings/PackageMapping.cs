using UniNet.Application.DTOs.Packages;
using UniNet.Domain.Entities;

namespace UniNet.Application.Mappings;

public static class PackageMapping
{
    public static PackageResponse ToResponse(this Package package) => new(
        package.Id, package.Code, package.Name, package.Type, package.Role,
        package.Price, package.DurationInDays, package.IsActive,
        package.CreatedAt, package.UpdatedAt);
}
