using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Packages;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PackageServiceTests
{
    private static SavePackageRequest Request(string code = "VERIFY") =>
        new(code, "Verification", PackageType.Verification, TargetRole.Student, 100000m, 30, true);

    [Theory]
    [InlineData(AccountRole.Student)]
    [InlineData(AccountRole.Partner)]
    [InlineData(AccountRole.Moderator)]
    [InlineData(AccountRole.Verificator)]
    public async Task NonAdminsCannotMutatePackages(AccountRole role)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "user@example.com", Role = role };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        var service = new PackageService(db);
        var id = Guid.NewGuid();
        var create = await Assert.ThrowsAsync<PackageException>(() => service.CreatePackageAsync(account.Id, Request(), default));
        var update = await Assert.ThrowsAsync<PackageException>(() => service.UpdatePackageAsync(account.Id, id, Request(), default));
        var delete = await Assert.ThrowsAsync<PackageException>(() => service.DeletePackageAsync(account.Id, id, default));
        Assert.All(new[] { create, update, delete }, error => Assert.Equal(403, error.Status));
    }

    [Fact]
    public async Task AdminCrudAllowsAllThreeRolesToReadAndRejectsDuplicateCodes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var accounts = new[] { AccountRole.Admin, AccountRole.Student, AccountRole.Partner }
            .Select(role => new Account { Email = $"{role}@example.com", Role = role }).ToArray();
        db.Accounts.AddRange(accounts);
        await db.SaveChangesAsync();
        var service = new PackageService(db);
        var adminId = accounts[0].Id;
        var created = await service.CreatePackageAsync(adminId, Request(), default);
        foreach (var account in accounts)
        {
            Assert.Equal(created, await service.GetPackageAsync(account.Id, created.Id, default));
            var list = await service.ListPackagesAsync(account.Id, PackageType.Verification, TargetRole.Student, true, "VERIFY", 1, 20, default);
            Assert.Equal(1, list.TotalItems);
            Assert.Equal(created.Id, Assert.Single(list.Packages).Id);
        }
        var duplicate = await Assert.ThrowsAsync<PackageException>(() => service.CreatePackageAsync(adminId, Request(" VERIFY "), default));
        Assert.Equal(409, duplicate.Status);
        var updated = await service.UpdatePackageAsync(adminId, created.Id, Request() with { Name = "New name", IsActive = false }, default);
        Assert.Equal("New name", updated.Name);
        Assert.False(updated.IsActive);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        await service.DeletePackageAsync(adminId, created.Id, default);
        var missing = await Assert.ThrowsAsync<PackageException>(() => service.GetPackageAsync(adminId, created.Id, default));
        Assert.Equal(404, missing.Status);
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public async Task InvalidPriceOrDurationIsRejected(int price, int duration)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var admin = new Account { Email = "admin@example.com", Role = AccountRole.Admin };
        db.Accounts.Add(admin);
        await db.SaveChangesAsync();
        var service = new PackageService(db);
        var error = await Assert.ThrowsAsync<PackageException>(() => service.CreatePackageAsync(admin.Id,
            Request() with { Price = price, DurationInDays = duration }, default));
        Assert.Equal(400, error.Status);
        Assert.False(await db.Packages.AnyAsync());
    }
}
