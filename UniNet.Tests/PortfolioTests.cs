using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PortfolioTests
{
    [Fact]
    public async Task FirstSaveCreatesCareerProfileWithoutExistingRow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "new@example.com", Profile = new UserProfile { FullName = "New", Nickname = "New" } };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        var result = await new PortfolioService(db).Save(account.Id, new PortfolioContent(), default);
        Assert.False(result.Portfolio.IsPublic);
        Assert.Single(await db.CareerProfiles.ToListAsync());
    }
    [Fact]
    public async Task PortfolioRoundTripHonorsPrivacyAndOwnerIdentity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "owner@example.com", Profile = new UserProfile { FullName = "Owner", Nickname = "Owner", StudentCode = "private" } };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        var service = new PortfolioService(db);
        var empty = await service.Me(account.Id, default);
        Assert.Empty(empty.Portfolio.Projects);
        Assert.True(empty.IsOwner);
        account.Profile.CareerProfile = new CareerProfile { UserId = account.Profile.Id, BasicInfoJson = "{\"customMetadata\":\"keep\"}", ActivitiesJson = "[{\"title\":\"Volunteer\"}]" };
        db.CareerProfiles.Add(account.Profile.CareerProfile);
        await db.SaveChangesAsync();
        var payload = new PortfolioContent { Headline = "Developer", Projects = [new() { Title = "Real project", Technologies = ["React"] }], Articles = [new() { Title = "Real article", Url = "https://example.com/article" }] };
        await service.Save(account.Id, payload, default);
        var denial = await Assert.ThrowsAsync<AuthException>(() => service.Read(account.Profile.Id, Guid.NewGuid(), default));
        Assert.Equal(404, denial.Status);
        var published = await service.Save(account.Id, payload with { IsPublic = true }, default);
        Assert.Single(published.Portfolio.Projects);
        var visitor = await service.Read(account.Profile.Id, Guid.NewGuid(), default);
        Assert.False(visitor.IsOwner);
        Assert.Equal("Developer", visitor.Portfolio.Headline);
        Assert.Single(visitor.Portfolio.Articles);
        Assert.Single(await db.CareerProfiles.ToListAsync());
        Assert.Contains("customMetadata", account.Profile.CareerProfile.BasicInfoJson);
        Assert.Contains("Volunteer", account.Profile.CareerProfile.ActivitiesJson);
        account.Status = AccountStatus.Suspended;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(() => service.Read(account.Profile.Id, Guid.NewGuid(), default));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///secret")]
    [InlineData("https://user:password@example.com")]
    public void UnsafeUrlsAreRejected(string url)
    {
        Assert.Throws<AuthException>(() => PortfolioService.Validate(new PortfolioContent { CvUrl = url }));
    }

    [Fact]
    public void MissingTitlesAndUnboundedCollectionsAreRejected()
    {
        Assert.Throws<AuthException>(() => PortfolioService.Validate(new PortfolioContent { Projects = [new()] }));
        Assert.Throws<AuthException>(() => PortfolioService.Validate(new PortfolioContent { Skills = Enumerable.Range(0, 101).Select(_ => new PortfolioSkill { Name = "React", Category = "Frontend" }).ToArray() }));
    }
}
