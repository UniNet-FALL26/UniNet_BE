using Google.Apis.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GoogleStudentLoginTests
{
    private sealed class VerifiedGoogle : IGoogleTokenVerifier
    {
        public Task<GoogleJsonWebSignature.Payload> Verify(string idToken, string[] clientIds)
        {
            Assert.Equal("fixture-id-token", idToken);
            Assert.Equal(new[] { "fixture.apps.googleusercontent.com" }, clientIds);
            return Task.FromResult(new GoogleJsonWebSignature.Payload
            {
                Subject = "google-student", Email = "student@google.test", EmailVerified = true, Name = "Student"
            });
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public UniNetDbContext Db { get; }
        public AuthService Auth { get; }
        public Fixture(bool realVerifier = false)
        {
            connection.Open();
            Db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "a-test-signing-key-with-at-least-32-bytes-12345",
                ["Jwt:Issuer"] = "UniNet", ["Jwt:Audience"] = "UniNetMobile",
                ["Google:ClientIds:0"] = "fixture.apps.googleusercontent.com"
            }).Build();
            Auth = new AuthService(Db, config, realVerifier ? new GoogleTokenVerifier() : new VerifiedGoogle());
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    public static IEnumerable<object[]> NonStudentAccounts() =>
        Enum.GetValues<AccountRole>().Where(role => role != AccountRole.Student)
            .SelectMany(role => new[] { new object[] { role, false }, new object[] { role, true } });

    [Theory]
    [MemberData(nameof(NonStudentAccounts))]
    public async Task ExistingNonStudentCannotLinkGoogleOrReceiveSession(AccountRole role, bool alreadyLinked)
    {
        await using var fixture = new Fixture();
        var account = new Account { Email = "student@google.test", Role = role,
            GoogleId = alreadyLinked ? "google-student" : null, EmailVerified = false };
        fixture.Db.Add(account); await fixture.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AuthException>(() => fixture.Auth.Google(new("fixture-id-token"), default));
        Assert.Equal("GOOGLE_STUDENT_ONLY", error.Code);
        Assert.Equal(403, error.Status);
        fixture.Db.ChangeTracker.Clear();
        var saved = await fixture.Db.Accounts.SingleAsync();
        Assert.Equal(role, saved.Role);
        Assert.Equal(alreadyLinked ? "google-student" : null, saved.GoogleId);
        Assert.False(saved.EmailVerified);
        Assert.Null(saved.LastLoginAt);
        Assert.Empty(await fixture.Db.RefreshTokens.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GoogleCreatesOrLinksOnlyStudentAndBindsDeviceSession(bool existing)
    {
        await using var fixture = new Fixture();
        if (existing) { fixture.Db.Add(new Account { Email = "student@google.test", Role = AccountRole.Student }); await fixture.Db.SaveChangesAsync(); }
        var response = await fixture.Auth.Google(new("fixture-id-token", DeviceId: "google-device"), default);
        Assert.Equal(AccountRole.Student, response.Account.Role);
        Assert.NotEmpty(response.AccessToken);
        Assert.Equal("google-student", (await fixture.Db.Accounts.SingleAsync()).GoogleId);
        Assert.Equal("google-device", (await fixture.Db.RefreshTokens.SingleAsync()).DeviceId);
    }

    [Fact]
    public async Task ProductionVerifierRejectsUntrustedTokenWithoutCreatingAccount()
    {
        await using var fixture = new Fixture(realVerifier: true);
        var error = await Assert.ThrowsAsync<AuthException>(() => fixture.Auth.Google(new("invalid-jwt"), default));
        Assert.Equal("INVALID_GOOGLE_TOKEN", error.Code);
        Assert.Empty(await fixture.Db.Accounts.ToListAsync());
        Assert.Empty(await fixture.Db.RefreshTokens.ToListAsync());
    }

    [Theory]
    [InlineData(AccountStatus.Pending)]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.Deleted)]
    public async Task GoogleDoesNotReactivateRestrictedStudent(AccountStatus status)
    {
        await using var fixture = new Fixture();
        fixture.Db.Add(new Account { Email = "student@google.test", Role = AccountRole.Student, Status = status });
        await fixture.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(() => fixture.Auth.Google(new("fixture-id-token"), default));
        Assert.Empty(await fixture.Db.RefreshTokens.ToListAsync());
        fixture.Db.ChangeTracker.Clear();
        var account = await fixture.Db.Accounts.SingleAsync();
        Assert.Equal(status, account.Status);
        Assert.Null(account.GoogleId);
    }
}
