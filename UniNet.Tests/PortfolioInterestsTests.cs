using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PortfolioInterestsTests
{
    [Fact]
    public async Task OwnerSelectionsSurviveReloadAndPublishWithoutChangingOtherMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "interests@example.test", Role = AccountRole.Student,
            Profile = new UserProfile { FullName = "Owner", Nickname = "Owner", CareerProfile = new CareerProfile { BasicInfoJson = "{\"customMetadata\":true}" } } };
        db.Accounts.Add(account); await db.SaveChangesAsync();
        var ownerId = account.Id; var profileId = account.Profile.Id;
        var service = new PortfolioService(db);
        Assert.Empty((await service.Me(ownerId, default)).Portfolio.Interests!);
        string[] interests = ["Đọc sách", "Bơi lội", "Lập trình"];
        var initial = await service.Me(ownerId, default);
        await service.SaveEditor(ownerId, new(new("Owner", "Owner", null, null, null), initial.Portfolio with { Interests = interests }), default);
        db.ChangeTracker.Clear();
        Assert.Equal(interests, (await service.Me(ownerId, default)).Portfolio.Interests);
        Assert.Equal(interests, (await service.Read(profileId, Guid.NewGuid(), default)).Portfolio.Interests);
        Assert.Contains("customMetadata", (await db.CareerProfiles.SingleAsync()).BasicInfoJson);

        // Older clients omit this field; unrelated saves must not clear it.
        await service.Save(ownerId, new PortfolioContent { Headline = "Updated" }, default);
        Assert.Equal(interests, (await service.Me(ownerId, default)).Portfolio.Interests);

        var hidden = (await service.Me(ownerId, default)).Portfolio;
        hidden = hidden with { Appearance = new() { Profile = hidden.Appearance.Profile with { HiddenSections = ["interests"] } } };
        await service.SaveEditor(ownerId, new(new("Owner", "Owner", null, null, null), hidden), default);
        Assert.Empty((await service.Read(profileId, Guid.NewGuid(), default)).Portfolio.Interests!);
        Assert.Empty((await service.ApplicationSnapshot(ownerId, default)).Portfolio.Interests!);
        Assert.Equal(interests, (await service.Me(ownerId, default)).Portfolio.Interests);

        await service.Save(ownerId, new PortfolioContent { Interests = [] }, default);
        db.ChangeTracker.Clear();
        Assert.Empty((await service.Me(ownerId, default)).Portfolio.Interests!);
    }

    [Fact]
    public void CatalogSelectionsAreBoundedAndValidated()
    {
        Assert.Equal(48, PortfolioInterestCatalog.Names.Count);
        PortfolioService.Validate(new PortfolioContent { Interests = PortfolioInterestCatalog.Names.ToArray() });
        foreach (var invalid in new string[][] { ["Unknown hobby"], [" "], [null!], ["Đọc sách", "Đọc sách"], Enumerable.Repeat("Đọc sách", 49).ToArray() })
            Assert.Throws<AuthException>(() => PortfolioService.Validate(new PortfolioContent { Interests = invalid }));
        PortfolioService.Validate(new PortfolioContent { Interests = null });
        PortfolioService.Validate(new PortfolioContent { Interests = [] });
    }
}
