using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Packages;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student,Partner,Admin"), Route("api/packages")]
public sealed class PackagesController(IPackageService packages) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new PackageException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpGet]
    public Task<PackagesResponse> ListPackages([FromQuery] PackageType? type = null,
        [FromQuery] TargetRole? role = null, [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => packages.ListPackagesAsync(AccountId, type, role, isActive, keyword, page, pageSize, ct);

    [HttpGet("{packageId:guid}")]
    public Task<PackageResponse> GetPackage(Guid packageId, CancellationToken ct)
        => packages.GetPackageAsync(AccountId, packageId, ct);

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<ActionResult<PackageResponse>> CreatePackage(SavePackageRequest request, CancellationToken ct)
    {
        var package = await packages.CreatePackageAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(GetPackage), new { packageId = package.Id }, package);
    }

    [HttpPut("{packageId:guid}"), Authorize(Roles = "Admin")]
    public Task<PackageResponse> UpdatePackage(Guid packageId, SavePackageRequest request, CancellationToken ct)
        => packages.UpdatePackageAsync(AccountId, packageId, request, ct);

    [HttpDelete("{packageId:guid}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeletePackage(Guid packageId, CancellationToken ct)
    {
        await packages.DeletePackageAsync(AccountId, packageId, ct);
        return NoContent();
    }
}
