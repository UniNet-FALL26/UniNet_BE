using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Packages;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
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
        var toggle = await Assert.ThrowsAsync<PackageException>(() => service.TogglePackageStatusAsync(account.Id, id, default));
        var listAll = await Assert.ThrowsAsync<PackageException>(() => service.ListAllPackagesAsync(account.Id, null, null, null, null, 1, 20, default));
        Assert.All(new[] { create, update, delete, toggle, listAll }, error => Assert.Equal(403, error.Status));
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
        var updated = await service.UpdatePackageAsync(adminId, created.Id, Request() with { Name = "New name" }, default);
        Assert.Equal("New name", updated.Name);
        Assert.True(updated.IsActive);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        var profile = new UserProfile { AccountId = accounts[1].Id, FullName = "Student" };
        db.UserProfiles.Add(profile);
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), UserId = profile.Id, PackageId = created.Id,
            Amount = created.Price, Status = PaymentStatus.Success,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        await service.DeletePackageAsync(adminId, created.Id, default);
        db.ChangeTracker.Clear();
        var deleted = await db.Packages.SingleAsync(x => x.Id == created.Id);
        Assert.False(deleted.IsActive);
        Assert.Equal(created.CreatedAt, deleted.CreatedAt);
        Assert.True(deleted.UpdatedAt >= updated.UpdatedAt);
        Assert.Equal(created.Id, (await db.Transactions.SingleAsync(x => x.Id == transaction.Id)).PackageId);
        foreach (var account in accounts)
        {
            foreach (bool? activeFilter in new bool?[] { null, true, false })
            {
                var list = await service.ListPackagesAsync(account.Id, null, null, activeFilter, null, 1, 20, default);
                Assert.Empty(list.Packages);
                Assert.Equal(0, list.TotalItems);
                Assert.Equal(0, list.TotalPages);
            }
        }
        await service.DeletePackageAsync(adminId, created.Id, default);
        Assert.Equal(1, await db.Packages.CountAsync());
        var activePackage = await service.CreatePackageAsync(adminId, Request("ACTIVE"), default);
        var all = await service.ListAllPackagesAsync(adminId, null, null, null, null, 1, 20, default);
        Assert.Equal(2, all.TotalItems);
        Assert.Contains(all.Packages, x => x.Id == created.Id && !x.IsActive);
        Assert.Contains(all.Packages, x => x.Id == activePackage.Id && x.IsActive);
        var inactive = await service.ListAllPackagesAsync(adminId, null, null, false, null, 1, 20, default);
        Assert.Equal(created.Id, Assert.Single(inactive.Packages).Id);
        var reactivated = await service.TogglePackageStatusAsync(adminId, created.Id, default);
        Assert.True(reactivated.IsActive);
        Assert.Equal(created.CreatedAt, reactivated.CreatedAt);
        Assert.True(reactivated.UpdatedAt >= deleted.UpdatedAt);
        var visible = await service.ListPackagesAsync(accounts[1].Id, null, null, null, null, 1, 20, default);
        Assert.Contains(visible.Packages, x => x.Id == created.Id);
        var deactivated = await service.TogglePackageStatusAsync(adminId, created.Id, default);
        Assert.False(deactivated.IsActive);
        var missing = await Assert.ThrowsAsync<PackageException>(() => service.TogglePackageStatusAsync(adminId, Guid.NewGuid(), default));
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
