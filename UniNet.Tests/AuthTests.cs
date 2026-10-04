using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;
using UniNet.Domain;
using Xunit;

namespace UniNet.Tests;

public sealed class AuthTests
{
    private static async Task<(SqliteConnection, UniNetDbContext, AuthService, ProfileService)> Setup()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "a-test-signing-key-with-at-least-32-bytes-12345",
            ["Jwt:Issuer"] = "UniNet", ["Jwt:Audience"] = "UniNetMobile"
        }).Build();
        return (connection, db, new AuthService(db, configuration), new ProfileService(db));
    }
    [Fact]
    public async Task StudentRegistrationLoginAndProfileUseOneAccount()
    {
        var (connection, db, auth, profiles) = await Setup();
        await using (connection) await using (db)
        {
            var registered = await auth.RegisterStudent(new("Nguyễn Văn A", " Student@Example.com ", "Password123", "Password123", null, null), default);
            Assert.Equal(AccountRole.Student, registered.Account.Role);
            Assert.Equal("student@example.com", registered.Account.Email);
            Assert.True(registered.RequiresProfileCompletion);
            Assert.Single(await db.UserProfiles.ToListAsync());
            var login = await auth.Login(new("student@example.com", "Password123"), default);
            Assert.Equal(registered.Account.Id, login.Account.Id);
            var updated = await profiles.Student(registered.Account.Id, new("Nguyễn Văn A", null, null, "Bio", "UIT", "Software", "S123"), default);
            Assert.True(updated.ProfileComplete);
            Assert.Single(await db.UserProfiles.ToListAsync());
        }
    }
    [Fact]
    public async Task InvalidRegistrationAndDuplicateEmailAreRejected()
    {
        var (connection, db, auth, _) = await Setup();
        await using (connection) await using (db)
        {
            await Assert.ThrowsAsync<AuthException>(() => auth.RegisterStudent(new("A", "a@example.com", "short", "short", null, null), default));
            await auth.RegisterStudent(new("A", "a@example.com", "Password123", "Password123", null, null), default);
            var duplicate = await Assert.ThrowsAsync<AuthException>(() => auth.RegisterPartner(new("P", "A@example.com", "Password123", "Password123", PartnerType.Company, "Representative"), default));
            Assert.Equal("EMAIL_ALREADY_EXISTS", duplicate.Code);
            Assert.Single(await db.Accounts.ToListAsync());
        }
    }
    [Fact]
    public async Task RefreshRotatesAndOldTokenCannotBeReused()
    {
        var (connection, db, auth, _) = await Setup();
        await using (connection) await using (db)
        {
            var original = await auth.RegisterStudent(new("A", "a@example.com", "Password123", "Password123", null, null), default);
            Assert.DoesNotContain(await db.RefreshTokens.Select(x => x.TokenHash).ToListAsync(), hash => hash == original.RefreshToken);
            var rotated = await auth.Refresh(new(original.RefreshToken), default);
            Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);
            var error = await Assert.ThrowsAsync<AuthException>(() => auth.Refresh(new(original.RefreshToken), default));
            Assert.Equal("INVALID_REFRESH_TOKEN", error.Code);
            await auth.Logout(original.Account.Id, rotated.RefreshToken, default);
            await Assert.ThrowsAsync<AuthException>(() => auth.Refresh(new(rotated.RefreshToken), default));
            var again = await auth.Login(new("a@example.com", "Password123"), default);
            await auth.LogoutAll(original.Account.Id, default);
            await Assert.ThrowsAsync<AuthException>(() => auth.Refresh(new(again.RefreshToken), default));
        }
    }
    [Fact]
    public async Task PartnerCannotUpdateStudentProfile()
    {
        var (connection, db, auth, profiles) = await Setup();
        await using (connection) await using (db)
        {
            var partner = await auth.RegisterPartner(new("Company", "p@example.com", "Password123", "Password123", PartnerType.Company, "Representative"), default);
            var error = await Assert.ThrowsAsync<AuthException>(() => profiles.Student(partner.Account.Id, new("P", null, null, "Bio", "UIT", null, null), default));
            Assert.Equal("FORBIDDEN_ROLE", error.Code);
        }
    }
    [Fact]
    public async Task SuspendedAndDeletedAccountsCannotLogin()
    {
        var (connection, db, auth, _) = await Setup();
        await using (connection) await using (db)
        {
            var registered = await auth.RegisterStudent(new("A", "a@example.com", "Password123", "Password123", null, null), default);
            var account = await db.Accounts.SingleAsync(x => x.Id == registered.Account.Id);
            account.Status = AccountStatus.Suspended;
            await db.SaveChangesAsync();
            var suspended = await Assert.ThrowsAsync<AuthException>(() => auth.Login(new("a@example.com", "Password123"), default));
            Assert.Equal("ACCOUNT_SUSPENDED", suspended.Code);
            account.Status = AccountStatus.Deleted;
            await db.SaveChangesAsync();
            var deleted = await Assert.ThrowsAsync<AuthException>(() => auth.Login(new("a@example.com", "Password123"), default));
            Assert.Equal("INVALID_CREDENTIALS", deleted.Code);
        }
    }
    [Fact]
    public async Task InvalidPartnerTypeIsRejected()
    {
        var (connection, db, auth, _) = await Setup();
        await using (connection) await using (db)
        {
            var error = await Assert.ThrowsAsync<AuthException>(() => auth.RegisterPartner(new("P", "p@example.com", "Password123", "Password123", (PartnerType)9, "Representative"), default));
            Assert.Equal("INVALID_PARTNER_TYPE", error.Code);
            Assert.Empty(await db.Accounts.ToListAsync());
        }
    }
    [Fact]
    public async Task PartnerRegistrationSeparatesOrganizationAndPersonalNames()
    {
        var (connection, db, auth, _) = await Setup();
        await using (connection) await using (db)
        {
            var registered = await auth.RegisterPartner(new(" ABC Company ", "partner@example.com", "Password123", "Password123", PartnerType.Company, " Nguyen Van A ", " Partner A "), default);
            Assert.Equal("ABC Company", registered.Account.Profile!.OrganizationName);
            Assert.Equal("Nguyen Van A", registered.Account.Profile.FullName);
            Assert.Equal("Partner A", registered.Account.Profile.Nickname);
            Assert.Equal("Partner A", registered.Account.Profile.DisplayName);
            Assert.False(registered.Account.Profile.IsVerified);
            Assert.Empty(await db.CareerProfiles.ToListAsync());
            var login = await auth.Login(new("partner@example.com", "Password123"), default);
            Assert.Equal(registered.Account.Profile, login.Account.Profile);
        }
    }
    [Theory]
    [InlineData("", "Representative", "Partner")]
    [InlineData("Company", "", "Partner")]
    [InlineData("Company", "Representative", " ")]
    public async Task InvalidPartnerNamesDoNotCreateAnAccount(string organization, string fullName, string nickname)
    {
        var (connection, db, auth, _) = await Setup();
        await using (connection) await using (db)
        {
            await Assert.ThrowsAsync<AuthException>(() => auth.RegisterPartner(new(organization, "partner@example.com", "Password123", "Password123", PartnerType.Company, fullName, nickname), default));
            Assert.Empty(await db.Accounts.ToListAsync());
        }
    }
    [Fact]
    public async Task IdentityChangesInvalidateVerificationButCosmeticChangesPreserveHistory()
    {
        var (connection, db, auth, profiles) = await Setup();
        await using (connection) await using (db)
        {
            var registered = await auth.RegisterStudent(new("Student", "student@example.com", "Password123", "Password123", "UIT", "Software", "nickname"), default);
            var user = await db.Users.SingleAsync();
            user.IsVerified = true;
            db.UserVerifications.Add(new UserVerification { UserId = user.Id, VerificationType = VerificationType.Student, Status = VerificationStatus.Approved, VerificationData = "{\"fullName\":\"Student\"}" });
            await db.SaveChangesAsync();
            var cosmetic = await profiles.Student(registered.Account.Id, new("Student", "avatar", null, "new bio", "UIT", "Software", null, "new nickname"), default);
            Assert.True(cosmetic.Profile!.IsVerified);
            var identity = await profiles.Student(registered.Account.Id, new("Changed", "avatar", null, "new bio", "UIT", "Software", null), default);
            Assert.False(identity.Profile!.IsVerified);
            Assert.Equal("new nickname", identity.Profile.Nickname);
            Assert.Equal(VerificationStatus.Approved, (await db.UserVerifications.SingleAsync()).Status);
            Assert.DoesNotContain("verificationData", System.Text.Json.JsonSerializer.Serialize(identity), StringComparison.OrdinalIgnoreCase);
        }
    }
}
