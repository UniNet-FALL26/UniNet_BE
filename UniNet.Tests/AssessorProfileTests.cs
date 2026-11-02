using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class AssessorProfileTests
{
    private static AssessorProfileRequest Request() => new("Expert", "expert", null, null, "Biography", "expert@example.test", "0968110347", "HCMC",
        new("Engineer", "Company", 7, ["Backend", "Cloud"], "Master", "University", "https://www.linkedin.com/in/expert", "https://github.com/expert", "https://expert.test"));

    [Fact]
    public async Task AllFieldsRoundTripAndClearingPreservesCommonIdentityAndOtherUsers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = "expert@example.test", Role = AccountRole.Assessor, Profile = new() { FullName = "Before", Nickname = "before", IsVerified = true } };
        var other = new Account { Email = "other@example.test", Role = AccountRole.Assessor, Profile = new() { FullName = "Other", Nickname = "other" } };
        db.AddRange(owner, other); await db.SaveChangesAsync();
        var service = new ProfileService(db);
        var saved = await service.Assessor(owner.Id, Request(), default);
        Assert.Equal("Engineer", saved.Profile!.AssessorJson!.JobTitle);
        db.ChangeTracker.Clear();
        var reloaded = AuthMapping.ToResponse(await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == owner.Id));
        Assert.Equivalent(Request().AssessorJson, reloaded.Profile!.AssessorJson);
        Assert.Equal(new[] { "Backend", "Cloud" }, reloaded.Profile.AssessorJson!.Expertise);
        Assert.Equal("expert@example.test", reloaded.Profile.ContactEmail);
        Assert.True(reloaded.Profile.IsVerified);
        Assert.Null((await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == other.Id)).Profile!.AssessorJson);
        var cleared = await service.ClearAssessor(owner.Id, default);
        Assert.Null(cleared.Profile!.AssessorJson);
        Assert.Equal("Expert", cleared.Profile.FullName); Assert.Equal("Biography", cleared.Profile.Bio);
        db.ChangeTracker.Clear(); Assert.Null((await db.UserProfiles.SingleAsync(p => p.AccountId == owner.Id)).AssessorJson);
    }

    [Theory]
    [InlineData(AccountRole.Student)] [InlineData(AccountRole.Partner)] [InlineData(AccountRole.Verifier)]
    [InlineData(AccountRole.Moderator)] [InlineData(AccountRole.Accountant)] [InlineData(AccountRole.Admin)]
    public async Task OtherRolesCannotWriteOrClearAssessorData(AccountRole role)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "role@example.test", Role = role, Profile = new() { FullName = "User", Nickname = "user" } };
        db.Add(account); await db.SaveChangesAsync(); var service = new ProfileService(db);
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() => service.Assessor(account.Id, Request(), default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() => service.ClearAssessor(account.Id, default))).Status);
    }

    [Theory]
    [InlineData(-1, "https://github.com/expert")]
    [InlineData(81, "https://github.com/expert")]
    [InlineData(5, "javascript:alert(1)")]
    public async Task InvalidProfessionalDataDoesNotPartiallySave(int years, string url)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "expert@example.test", Role = AccountRole.Assessor, Profile = new() { FullName = "Original", Nickname = "original" } };
        db.Add(account); await db.SaveChangesAsync(); var request = Request();
        await Assert.ThrowsAsync<AuthException>(() => new ProfileService(db).Assessor(account.Id, request with { AssessorJson = request.AssessorJson! with { YearsOfExperience = years, GitHubUrl = url } }, default));
        db.ChangeTracker.Clear(); var profile = await db.UserProfiles.SingleAsync();
        Assert.Equal("Original", profile.FullName); Assert.Null(profile.AssessorJson);
    }
}
