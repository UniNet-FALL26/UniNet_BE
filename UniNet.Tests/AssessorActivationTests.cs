using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class AssessorActivationTests
{
    [Fact]
    public async Task ApprovalCreatesOnePendingAccountAndPasswordActivatesNormalLoginExactlyOnce()
    {
        await using var fixture = await Fixture.Create();
        var result = await fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default);
        Assert.Equal("AwaitingPassword", result.Status);
        fixture.Db.ChangeTracker.Clear();
        var account = await fixture.Db.Accounts.Include(x => x.Profile).SingleAsync(x => x.Id == result.AccountId);
        Assert.Equal(AccountRole.Assessor, account.Role);
        Assert.Equal(AccountStatus.Pending, account.Status);
        Assert.True(account.EmailVerified);
        Assert.Null(account.PasswordHash);
        Assert.Equal(fixture.Row.FullName, account.Profile!.FullName);
        Assert.Equal(401, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Auth.Login(new(fixture.Row.EmailNormalized, "Password123"), default))).Status);
        var repeated = await fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default);
        Assert.Equal(result, repeated);
        Assert.Single(await fixture.Db.AssessorOnboardingEmails.ToListAsync());
        Assert.Equal(2, await fixture.Db.Accounts.CountAsync());

        var token = await fixture.Token();
        await fixture.Service.InspectPassword(token, default);
        await fixture.Service.SetPassword(new(token, "Password123", "Password123"), default);
        fixture.Db.ChangeTracker.Clear();
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        account = await fixture.Db.Accounts.SingleAsync(x => x.Id == result.AccountId);
        Assert.Equal(AssessorOnboardingStatus.Activated, row.Status);
        Assert.NotNull(row.SetPasswordTokenUsedAt);
        Assert.NotNull(row.ActivatedAt);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.NotEqual("Password123", account.PasswordHash);
        var login = await fixture.Auth.Login(new(row.EmailNormalized, "Password123"), default);
        Assert.NotNull(login);
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.SetPassword(new(token, "Changed123", "Changed123"), default))).Status);
    }

    [Fact]
    public async Task AutomaticApprovalWaitsForDueTimeAndSkipsManualUnsignedAndConflictingProfiles()
    {
        await using var fixture = await Fixture.Create();
        var due = fixture.Clock.GetUtcNow().AddMinutes(15);
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x =>
            x.SetProperty(p => p.AutoApprove, true).SetProperty(p => p.AutoApproveAt, due));
        await fixture.Service.ApproveNext(default);
        Assert.Equal(1, await fixture.Db.Accounts.CountAsync());
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await fixture.Service.ApproveNext(default);
        fixture.Db.ChangeTracker.Clear();
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        Assert.Equal(AssessorOnboardingStatus.AwaitingPassword, row.Status);
        Assert.Null(row.ReviewedByAccountId);
        Assert.Equal(fixture.Clock.GetUtcNow(), row.ReviewedAt);
        await fixture.Service.ApproveNext(default);
        Assert.Equal(2, await fixture.Db.Accounts.CountAsync());
        Assert.Single(await fixture.Db.AssessorOnboardingEmails.ToListAsync());
    }

    [Fact]
    public async Task EmailConflictIsPersistedAndCanBeRejectedWithoutChangingExistingRole()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.Accounts.Add(new Account { Email = fixture.Row.EmailNormalized, Role = AccountRole.Student });
        await fixture.Db.SaveChangesAsync();
        var conflict = await fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default);
        Assert.Equal("ApprovalConflict", conflict.Status);
        fixture.Db.ChangeTracker.Clear();
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        Assert.Null(row.AccountId);
        Assert.Null(row.AutoApproveAt);
        Assert.Equal("EMAIL_ALREADY_REGISTERED", row.ReviewReason);
        Assert.Empty(await fixture.Db.AssessorOnboardingEmails.ToListAsync());
        Assert.Equal(AccountRole.Student, (await fixture.Db.Accounts.SingleAsync(x => x.Email == row.EmailNormalized)).Role);
        await fixture.Service.Reject(fixture.Admin.Id, row.Id, "Email đã được đăng ký.", default);
        await fixture.Service.Reject(fixture.Admin.Id, row.Id, "Email đã được đăng ký.", default);
        Assert.Single(await fixture.Db.AssessorOnboardingEmails.Where(x => x.Kind == "Rejected").ToListAsync());
        Assert.Equal(2, await fixture.Db.Accounts.CountAsync());
    }

    [Fact]
    public async Task PasswordExpiryResendAndSuspensionPreserveAccountStateAndInvalidateOldLinks()
    {
        await using var fixture = await Fixture.Create();
        var approved = await fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default);
        var old = await fixture.Token();
        Assert.Equal(429, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.ResendPassword(old, default))).Status);
        fixture.Clock.Advance(TimeSpan.FromHours(49));
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.InspectPassword(old, default))).Status);
        await fixture.Service.ResendPassword(old, default);
        var fresh = await fixture.Token();
        Assert.NotEqual(old, fresh);
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.InspectPassword(old, default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.SetPassword(new(fresh, "short", "short"), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.SetPassword(new(fresh, "Password123", "Mismatch123"), default))).Status);
        await fixture.Db.Accounts.Where(x => x.Id == approved.AccountId)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AccountStatus.Suspended));
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.SetPassword(new(fresh, "Password123", "Password123"), default))).Status);
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(AssessorOnboardingStatus.AwaitingPassword, (await fixture.Db.AssessorOnboardings.SingleAsync()).Status);
        Assert.Null((await fixture.Db.Accounts.SingleAsync(x => x.Id == approved.AccountId)).PasswordHash);
    }

    [Fact]
    public async Task FailedEmailInsertRollsBackProvisioningAndRetryCanComplete()
    {
        var failure = new FailPasswordOutbox();
        await using var fixture = await Fixture.Create(failure);
        failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(1, await fixture.Db.Accounts.CountAsync());
        Assert.Empty(await fixture.Db.UserProfiles.ToListAsync());
        Assert.Empty(await fixture.Db.AssessorOnboardingEmails.ToListAsync());
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        Assert.Equal(AssessorOnboardingStatus.AwaitingApproval, row.Status);
        Assert.Null(row.AccountId);
        failure.Enabled = false;
        await fixture.Service.Approve(fixture.Admin.Id, row.Id, default);
        Assert.Equal(2, await fixture.Db.Accounts.CountAsync());
    }

    [Fact]
    public async Task ConcurrentEmailUniqueViolationRollsBackAndPersistsApprovalConflict()
    {
        await using var fixture = await Fixture.Create(new ConcurrentEmailRegistration());
        var result = await fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default);
        Assert.Equal("ApprovalConflict", result.Status);
        fixture.Db.ChangeTracker.Clear();
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        Assert.Equal(AssessorOnboardingStatus.ApprovalConflict, row.Status);
        Assert.Equal("EMAIL_ALREADY_REGISTERED", row.ReviewReason);
        Assert.Null(row.AccountId);
        Assert.Equal(1, await fixture.Db.Accounts.CountAsync());
        Assert.Empty(await fixture.Db.AssessorOnboardingEmails.ToListAsync());
    }

    [Fact]
    public async Task OnlyActiveAdminCanReviewAndUnsignedOrRejectedProfilesCannotBeApproved()
    {
        await using var fixture = await Fixture.Create();
        var staff = new Account { Email = "staff@example.test", Role = AccountRole.Assessor };
        fixture.Db.Accounts.Add(staff);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.Approve(staff.Id, fixture.Row.Id, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.Review(staff.Id, fixture.Row.Id, default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.Reject(fixture.Admin.Id, fixture.Row.Id, " ", default))).Status);
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x => x.SetProperty(p => p.SignedAt, (DateTimeOffset?)null));
        Assert.Equal(409, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default))).Status);
        await fixture.Service.Reject(fixture.Admin.Id, fixture.Row.Id, "Hồ sơ chưa hợp lệ.", default);
        Assert.Equal(409, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.Approve(fixture.Admin.Id, fixture.Row.Id, default))).Status);
    }

    private sealed class ConcurrentEmailRegistration : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Account>().Any(x =>
                x.State == EntityState.Added && x.Entity.Role == AccountRole.Assessor))
                throw new DbUpdateException("Simulated concurrent email registration.",
                    new PostgresException("Duplicate email.", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
                        constraintName: "IX_Accounts_Email"));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailPasswordOutbox : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<AssessorOnboardingEmail>()
                .Any(x => x.State == EntityState.Added && x.Entity.Kind == "SetPassword"))
                throw new InvalidOperationException("Simulated outbox failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; private set; } = null!;
        public UniNetDbContext Db { get; private set; } = null!;
        public Account Admin { get; private set; } = null!;
        public AssessorOnboarding Row { get; private set; } = null!;
        public TestClock Clock { get; } = new();
        public AssessorActivationService Service { get; private set; } = null!;
        public AssessorContractSecrets Secrets { get; private set; } = null!;
        public AuthService Auth { get; private set; } = null!;

        public static async Task<Fixture> Create(SaveChangesInterceptor? interceptor = null)
        {
            var fixture = new Fixture { Connection = new SqliteConnection("Data Source=:memory:") };
            await fixture.Connection.OpenAsync();
            var builder = new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(fixture.Connection)
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>();
            if (interceptor != null) builder.AddInterceptors(interceptor);
            fixture.Db = new(builder.Options);
            await fixture.Db.Database.EnsureCreatedAsync();
            fixture.Admin = new() { Email = "admin@example.test", Role = AccountRole.Admin };
            fixture.Row = new()
            {
                EmailNormalized = "assessor@example.test", FullName = "Nguyễn Văn Thử", CreatedByAccountId = fixture.Admin.Id,
                ProfileTokenHash = "test-profile-hash", Status = AssessorOnboardingStatus.AwaitingApproval,
                SignedName = "Nguyễn Văn Thử", SignedAt = fixture.Clock.GetUtcNow(),
                ContractSha256 = new string('A', 64), EmailConfirmedAt = fixture.Clock.GetUtcNow()
            };
            fixture.Db.AddRange(fixture.Admin, fixture.Row);
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();
            fixture.Secrets = new(new EphemeralDataProtectionProvider());
            fixture.Service = new(fixture.Db, new(fixture.Db, fixture.Secrets, fixture.Clock),
                Options.Create(new AssessorContractOptions { Enabled = true, PublicBaseUrl = "https://example.test" }), fixture.Clock);
            fixture.Auth = new(fixture.Db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Jwt:Key"] = "activation-test-signing-key-at-least-32-bytes",
                ["Jwt:Issuer"] = "UniNet", ["Jwt:Audience"] = "UniNetMobile"
            }).Build());
            return fixture;
        }

        public async Task<string> Token()
        {
            var row = await Db.AssessorOnboardingEmails.AsNoTracking().Where(x => x.Kind == "SetPassword")
                .OrderByDescending(x => x.CreatedAt).FirstAsync();
            var payload = JsonSerializer.Deserialize<AssessorEmailPayload>(Secrets.Unprotect(row.PayloadCiphertext!))!;
            return payload.Body.Split("#token=")[1];
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
