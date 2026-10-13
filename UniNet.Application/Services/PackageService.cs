using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Packages;
using UniNet.Application.Interfaces;
using UniNet.Application.Mappings;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class PackageService(UniNetDbContext db) : IPackageService
{
    public async Task<PackagesResponse> ListPackagesAsync(Guid accountId, PackageType? type, TargetRole? role,
        bool? isActive, string? keyword, int page, int pageSize, CancellationToken ct)
    {
        await EnsureAccessAsync(accountId, false, ct);
        Require(page >= 1, "INVALID_PAGE", "Page must be at least 1.");
        Require(pageSize is >= 1 and <= 100, "INVALID_PAGE_SIZE", "Page size must be between 1 and 100.");
        Require(type is null || Enum.IsDefined(type.Value), "INVALID_PACKAGE_TYPE", "Package type is invalid.");
        Require(role is null || Enum.IsDefined(role.Value), "INVALID_TARGET_ROLE", "Target role is invalid.");
        Require(keyword is null || keyword.Length <= 255, "INVALID_KEYWORD", "Keyword must be at most 255 characters.");
        var query = db.Packages.AsNoTracking();
        if (type.HasValue) query = query.Where(x => x.Type == type.Value);
        if (role.HasValue) query = query.Where(x => x.Role == role.Value);
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => x.Code.Contains(term) || x.Name.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var packages = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize),
            packages.Select(x => x.ToResponse()).ToList());
    }

    public async Task<PackageResponse> GetPackageAsync(Guid accountId, Guid packageId, CancellationToken ct)
    {
        await EnsureAccessAsync(accountId, false, ct);
        var package = await db.Packages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == packageId, ct)
            ?? throw NotFound();
        return package.ToResponse();
    }

    public async Task<PackageResponse> CreatePackageAsync(Guid accountId, SavePackageRequest request, CancellationToken ct)
    {
        await EnsureAccessAsync(accountId, true, ct);
        Validate(request);
        await EnsureUniqueCodeAsync(request.Code.Trim(), null, ct);
        var now = DateTimeOffset.UtcNow;
        var package = new Package { Id = Guid.NewGuid(), CreatedAt = now };
        Apply(package, request, now);
        db.Packages.Add(package);
        await SaveAsync(ct);
        return package.ToResponse();
    }

    public async Task<PackageResponse> UpdatePackageAsync(Guid accountId, Guid packageId, SavePackageRequest request, CancellationToken ct)
    {
        await EnsureAccessAsync(accountId, true, ct);
        Validate(request);
        var package = await db.Packages.SingleOrDefaultAsync(x => x.Id == packageId, ct) ?? throw NotFound();
        await EnsureUniqueCodeAsync(request.Code.Trim(), packageId, ct);
        Apply(package, request, DateTimeOffset.UtcNow);
        await SaveAsync(ct);
        return package.ToResponse();
    }

    public async Task DeletePackageAsync(Guid accountId, Guid packageId, CancellationToken ct)
    {
        await EnsureAccessAsync(accountId, true, ct);
        var package = await db.Packages.SingleOrDefaultAsync(x => x.Id == packageId, ct) ?? throw NotFound();
        if (await db.Transactions.AnyAsync(x => x.PackageId == packageId, ct))
            throw HasTransactions();
        db.Packages.Remove(package);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation, ConstraintName: "FK_Transactions_Packages_PackageId" })
        { throw HasTransactions(); }
    }

    private async Task EnsureAccessAsync(Guid accountId, bool adminOnly, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new PackageException("UNAUTHORIZED", "Account was not found.", 401);
        Require(account.Status == AccountStatus.Active && (adminOnly
                ? account.Role == AccountRole.Admin
                : account.Role is AccountRole.Student or AccountRole.Partner or AccountRole.Admin),
            "FORBIDDEN", "You do not have permission to perform this package operation.", 403);
    }

    private async Task EnsureUniqueCodeAsync(string code, Guid? excludedId, CancellationToken ct)
    {
        if (await db.Packages.AnyAsync(x => x.Code == code && (!excludedId.HasValue || x.Id != excludedId.Value), ct))
            throw DuplicateCode();
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Packages_Code" })
        { throw DuplicateCode(); }
    }

    private static void Validate(SavePackageRequest request)
    {
        Require(!string.IsNullOrWhiteSpace(request.Code) && request.Code.Trim().Length <= 100,
            "INVALID_PACKAGE_CODE", "Code is required and must be at most 100 characters.");
        Require(!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 255,
            "INVALID_PACKAGE_NAME", "Name is required and must be at most 255 characters.");
        Require(Enum.IsDefined(request.Type), "INVALID_PACKAGE_TYPE", "Package type is invalid.");
        Require(Enum.IsDefined(request.Role), "INVALID_TARGET_ROLE", "Target role is invalid.");
        Require(request.Price is >= 0 and <= 9_999_999_999_999_999.99m && decimal.Round(request.Price, 2) == request.Price,
            "INVALID_PACKAGE_PRICE", "Price must be nonnegative, fit decimal(18,2), and have at most two decimal places.");
        Require(request.DurationInDays is null or > 0, "INVALID_PACKAGE_DURATION", "Duration must be null or a positive number of days.");
    }

    private static void Apply(Package package, SavePackageRequest request, DateTimeOffset now)
    {
        package.Code = request.Code.Trim();
        package.Name = request.Name.Trim();
        package.Type = request.Type;
        package.Role = request.Role;
        package.Price = request.Price;
        package.DurationInDays = request.DurationInDays;
        package.IsActive = request.IsActive;
        package.UpdatedAt = now;
    }

    private static PackageException NotFound() => new("PACKAGE_NOT_FOUND", "Package was not found.", 404);
    private static PackageException DuplicateCode() => new("PACKAGE_CODE_EXISTS", "Package code already exists.", 409);
    private static PackageException HasTransactions() => new("PACKAGE_HAS_TRANSACTIONS", "A package with transactions cannot be deleted. Deactivate it instead.", 409);
    private static void Require(bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new PackageException(code, message, status);
    }
}
