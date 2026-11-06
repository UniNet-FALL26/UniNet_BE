using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PartnerIntroductionTests
{
    private static PartnerProfileRequest Request(PartnerIntroductionDetails? intro = null) => new("Partner", PartnerType.Company, null, null, "Biography", "Software", null, null, null, "HCM", [], intro);

    [Fact]
    public async Task IntroductionRoundTripsAndOldClientsPreserveMetadata()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = "partner@example.test", Role = AccountRole.Partner, Profile = new() { FullName = "Partner", Nickname = "Partner", Address = "HCM", IsVerified = true, PartnerJson = new() { OrganizationName = "Partner", PartnerType = PartnerType.Company, Industry = "Software" } } };
        db.Add(owner); await db.SaveChangesAsync();
        var legacyJson = """{"OrganizationName":"Partner","PartnerType":0,"Industry":"Software","Website":null}""";
        await db.Database.ExecuteSqlRawAsync("""UPDATE "Users" SET "PartnerJson" = {0}""", legacyJson);
        db.ChangeTracker.Clear();
        var legacy = AuthMapping.ToResponse(await db.Accounts.Include(a => a.Profile).SingleAsync());
        Assert.Empty(legacy.Profile!.Introduction!.GalleryUrls!);
        var details = new PartnerIntroductionDetails("https://example.test/intro.png", "2019-03-15", "51–200", ["HCM", "Ha Noi"], ["https://example.test/office.png"]);
        var service = new ProfileService(db);
        await service.Partner(owner.Id, Request(details), default);
        db.ChangeTracker.Clear();
        var result = await service.Partner(owner.Id, Request() with { Bio = "Updated" }, default);
        Assert.Equivalent(details, result.Profile!.Introduction);
        Assert.Equal("Updated", result.Profile.Bio);
        Assert.True(result.Profile.IsVerified);
        await service.Partner(owner.Id, Request(new(null, null, null, [], [])), default);
        db.ChangeTracker.Clear();
        var cleared = AuthMapping.ToResponse(await db.Accounts.Include(a => a.Profile).SingleAsync());
        Assert.Null(cleared.Profile!.Introduction!.ImageUrl); Assert.Empty(cleared.Profile.Introduction.GalleryUrls!);
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:image/png;base64,abc", null)]
    [InlineData("https://user:pass@example.test/image.png", null)]
    [InlineData("https://example.test/image.png", "2026-02-30")]
    [InlineData("https://example.test/image.png", "2999-01-01")]
    public async Task InvalidIntroductionIsRejectedBeforePersistence(string url, string? date)
    {
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite("Data Source=:memory:").Options);
        await Assert.ThrowsAsync<AuthException>(() => new ProfileService(db).Partner(Guid.NewGuid(), Request(new(url, date, null, [], [])), default));
    }
}
