using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class AssessorContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubmittedProfileCanBeSignedApprovedAndActivatedForNormalLogin(bool automatic)
    {
        await using var fixture = await Fixture.Create();
        var profileToken = AssessorContractSecrets.Token();
        var profileHash = AssessorContractSecrets.Hash(profileToken);
        var expires = fixture.Clock.GetUtcNow().AddDays(7);
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x =>
            x.SetProperty(p => p.Status, AssessorOnboardingStatus.Invited)
                .SetProperty(p => p.ProfileTokenHash, profileHash).SetProperty(p => p.ProfileTokenExpiresAt, expires)
                .SetProperty(p => p.AutoApprove, automatic));
        fixture.Db.ChangeTracker.Clear();
        var profile = new AssessorOnboardingService(fixture.Db, fixture.Protection, fixture.Clock);
        Assert.Equal("assessor@example.test", await profile.ReadForm(profileToken, default));
        var id = await profile.Submit(new(profileToken, "Nguyễn Văn Thử", "000000000000", "0123456789",
            "Ngân hàng thử nghiệm", "000123456789", "NGUYEN VAN THU"), default);
        await fixture.Tasks.GenerateNext(default);
        var contractToken = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        await fixture.Tasks.SendNext(default);
        await fixture.Service.RequestReadOtp(contractToken, default);
        var readCode = await fixture.Code("ReadOtp");
        await fixture.Tasks.SendNext(default);
        var session = await fixture.Service.VerifyReadOtp(contractToken, readCode, default);
        await fixture.Service.Pdf(session, default);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        await fixture.Service.RequestSignOtp(session, default);
        var signCode = await fixture.Code("SignOtp");
        await fixture.Tasks.SendNext(default);
        await fixture.Service.Sign(new(session, signCode, "Nguyễn Văn Thử", fixture.Files.Hash, true), default);
        var activation = new AssessorActivationService(fixture.Db, new(fixture.Db, fixture.Secrets, fixture.Clock),
            fixture.Options, fixture.Clock);
        if (automatic)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(16));
            await activation.ApproveNext(default);
        }
        else
        {
            var admin = await fixture.Db.Accounts.SingleAsync(x => x.Role == AccountRole.Admin);
            await activation.Approve(admin.Id, id, default);
        }
        var passwordToken = (await fixture.LatestPayload("SetPassword")).Body.Split("#token=")[1];
        await fixture.Tasks.SendNext(default);
        Assert.Equal(4, fixture.Mail.Sent.Count);
        await activation.SetPassword(new(passwordToken, "Password123", "Password123"), default);
        fixture.Db.ChangeTracker.Clear();
        var auth = new AuthService(fixture.Db, new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["Jwt:Key"] = "e2e-assessor-signing-key-at-least-32-bytes",
                ["Jwt:Issuer"] = "UniNet", ["Jwt:Audience"] = "UniNetMobile"
            }).Build());
        await auth.Login(new("assessor@example.test", "Password123"), default);
        Assert.Equal(AssessorOnboardingStatus.Activated, (await fixture.Db.AssessorOnboardings.SingleAsync()).Status);
    }

    [Fact]
    public async Task HttpContractEndpointsValidateInputsAndKeepPdfPrivateUntilEmailVerification()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var token = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(fixture.Service);
        var activation = new AssessorActivationService(fixture.Db, new(fixture.Db, fixture.Secrets, fixture.Clock),
            fixture.Options, fixture.Clock);
        builder.Services.AddSingleton(activation);
        builder.Services.AddSingleton(new AssessorOnboardingService(fixture.Db, fixture.Protection, fixture.Clock));
        builder.Services.AddSingleton<IAssessorContractFiles>(fixture.Files);
        const string signingKey = "assessor-http-test-signing-key-at-least-32-bytes";
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters {
                ValidateIssuer = false, ValidateAudience = false, ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
            };
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(AssessorContractController).Assembly);
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o => o.AddFixedWindowLimiter("AssessorContracts",
            limit => { limit.PermitLimit = 30; limit.Window = TimeSpan.FromMinutes(1); }));
        await using var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()!.Error;
            context.Response.StatusCode = error is AuthException auth ? auth.Status : 500;
            await context.Response.WriteAsJsonAsync(new { message = error.Message });
        }));
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        const string route = "/api/assessor-onboarding/contract/";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route + "verify-read-otp",
            new { token, code = "bad" })).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync(route + "pdf", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(route + "read-otp", new { token })).StatusCode);
        var verification = await client.PostAsJsonAsync(route + "verify-read-otp",
            new { token, code = await fixture.Code("ReadOtp") });
        verification.EnsureSuccessStatusCode();
        var session = (await verification.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var pdf = await client.PostAsJsonAsync(route + "pdf", new { token = session });
        pdf.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.True(pdf.Headers.CacheControl!.NoStore);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(route + "sign-otp", new { token = session })).StatusCode);
        var signature = new { token = session, code = await fixture.Code("SignOtp"), signedName = "Nguyễn Văn Thử",
            accepted = true, contractSha256 = fixture.Files.Hash };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route + "sign", signature)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync(route + "pdf", new { token = session })).StatusCode);
        var row = await fixture.Db.AssessorOnboardings.AsNoTracking().SingleAsync();
        var reviewRoute = $"/api/admin/assessor-onboardings/{row.Id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(reviewRoute + "/approve", null)).StatusCode);
        var jwt = new JwtSecurityToken(claims: [
            new Claim(JwtRegisteredClaimNames.Sub, row.CreatedByAccountId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        var review = await client.GetAsync(reviewRoute);
        review.EnsureSuccessStatusCode();
        Assert.DoesNotContain("sensitiveProfileCiphertext", await review.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(reviewRoute + "/contract")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(reviewRoute + "/approve", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var passwordToken = (await fixture.LatestPayload("SetPassword")).Body.Split("#token=")[1];
        const string passwordRoute = "/api/assessor-onboarding/password";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(passwordRoute + "/inspect", new { token = passwordToken })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(passwordRoute,
            new { token = passwordToken, password = "short", confirmPassword = "short" })).StatusCode);
        var credentials = new { token = passwordToken, password = "Password123", confirmPassword = "Password123" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(passwordRoute, credentials)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync(passwordRoute, credentials)).StatusCode);
        await app.StopAsync();
    }

    [Fact]
    public async Task GeneratedContractRequiresSeparateOtpForViewingAndSigningAndCannotBeSignedTwice()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        fixture.Db.ChangeTracker.Clear();
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        Assert.Equal(AssessorOnboardingStatus.AwaitingSignature, row.Status);
        Assert.Equal("V1-TEST", row.TemplateVersion);
        var linkMail = await fixture.LatestPayload("Contract");
        var token = linkMail.Body.Split("#token=")[1];
        await fixture.Tasks.SendNext(default);
        Assert.Single(fixture.Mail.Sent);
        Assert.Null((await fixture.Db.AssessorOnboardingEmails.AsNoTracking().SingleAsync()).PayloadCiphertext);

        await fixture.Service.RequestReadOtp(token, default);
        var readCode = await fixture.Code("ReadOtp");
        Assert.Equal(429, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.RequestReadOtp(token, default))).Status);
        var session = await fixture.Service.VerifyReadOtp(token, readCode, default);
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.RequestSignOtp(session, default))).Status);
        Assert.NotEmpty(await fixture.Service.Pdf(session, default));
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Sign(
            new(session, readCode, "Nguyễn Văn Thử", fixture.Files.Hash, true), default))).Status);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        await fixture.Service.RequestSignOtp(session, default);
        var code = await fixture.Code("SignOtp");
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Sign(
            new(session, code, "Nguyễn Văn Thử", new string('A', 64), true), default))).Status);
        await fixture.Service.Sign(new(session, code, "Nguyễn Văn Thử", fixture.Files.Hash, true), default);

        fixture.Db.ChangeTracker.Clear();
        row = await fixture.Db.AssessorOnboardings.SingleAsync();
        Assert.Equal(AssessorOnboardingStatus.AwaitingApproval, row.Status);
        Assert.Equal(fixture.Clock.GetUtcNow().AddMinutes(15), row.AutoApproveAt);
        Assert.Equal(fixture.Options.Value.SignatureStatement, row.SignatureStatement);
        Assert.NotNull(row.ContractViewedAt);
        Assert.Null(row.OtpHash);
        Assert.Null(row.AccountId);
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Sign(
            new(session, code, "Nguyễn Văn Thử", fixture.Files.Hash, true), default))).Status);
        await fixture.Tasks.SendNext(default);
        Assert.Single(fixture.Mail.Sent); // Pending OTP was cancelled after it was consumed.
    }

    [Fact]
    public async Task WrongOtpLocksAfterFiveAttemptsResendInvalidatesOldCodeAndHourlyLimitIsPersisted()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var token = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        await fixture.Service.RequestReadOtp(token, default);
        var code = await fixture.Code("ReadOtp");
        var wrongCode = code == "000000" ? "000001" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
            await Assert.ThrowsAsync<AuthException>(() => fixture.Service.VerifyReadOtp(token, wrongCode, default));
        Assert.Equal("OTP_UNAVAILABLE", (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.VerifyReadOtp(token, code, default))).Code);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        await fixture.Service.RequestReadOtp(token, default);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        await fixture.Service.RequestReadOtp(token, default);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(429, (await Assert.ThrowsAsync<AuthException>(() =>
            fixture.Service.RequestReadOtp(token, default))).Status);
    }

    [Fact]
    public async Task RevocationAndExpiredSessionsBlockPdfAndEmailAndDeliveryFailureCanRetry()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var token = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        fixture.Mail.Fail = true;
        await fixture.Tasks.SendNext(default);
        var mail = await fixture.Db.AssessorOnboardingEmails.AsNoTracking().SingleAsync();
        Assert.Equal(1, mail.Attempts);
        Assert.NotNull(mail.PayloadCiphertext);
        Assert.Null(mail.LeaseId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        fixture.Mail.Fail = false;
        await fixture.Tasks.SendNext(default);
        Assert.Single(fixture.Mail.Sent);
        await fixture.Service.RequestReadOtp(token, default);
        var session = await fixture.Service.VerifyReadOtp(token, await fixture.Code("ReadOtp"), default);
        fixture.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Pdf(session, default))).Status);
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssessorOnboardingStatus.Revoked));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.RequestReadOtp(token, default))).Status);
    }

    [Fact]
    public async Task ResendingContractKeepsPdfAndCancelsPreviousLinkAndOtp()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var oldToken = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        await fixture.Service.RequestReadOtp(oldToken, default);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        var key = row.ContractStorageKey;
        await fixture.Service.Resend(row.CreatedByAccountId, row.Id, default);
        Assert.Equal(key, row.ContractStorageKey);
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.RequestReadOtp(oldToken, default))).Status);
        await fixture.Tasks.SendNext(default);
        Assert.Empty(fixture.Mail.Sent); // The oldest, superseded contract message is discarded.
    }

    [Fact]
    public async Task ConcurrentUpdatesCannotCommitAnOtpIssuedAgainstAStaleRevision()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var token = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        fixture.Db.ChangeTracker.Clear();
        await fixture.Db.AssessorOnboardings.SingleAsync(); // Stale tracked revision.
        var nextRevision = Guid.NewGuid();
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x => x.SetProperty(p => p.Revision, nextRevision));
        Assert.Equal(409, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.RequestReadOtp(token, default))).Status);
        Assert.Empty(await fixture.Db.AssessorOnboardingEmails.AsNoTracking().Where(x => x.Kind == "ReadOtp").ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; private set; } = null!;
        public UniNetDbContext Db { get; private set; } = null!;
        public TestClock Clock { get; } = new();
        public TestFiles Files { get; } = new();
        public TestMail Mail { get; } = new();
        public IOptions<AssessorContractOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(new AssessorContractOptions
        {
            Enabled = true, PublicBaseUrl = "https://example.test", TemplateVersion = "V1-TEST",
            SignatureStatement = "Tôi đồng ý với bản thử nghiệm."
        });
        public AssessorContractSecrets Secrets { get; private set; } = null!;
        public IDataProtectionProvider Protection { get; private set; } = null!;
        public AssessorContractService Service { get; private set; } = null!;
        public AssessorContractTasks Tasks { get; private set; } = null!;

        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
            fixture.Connection = new("Data Source=:memory:");
            await fixture.Connection.OpenAsync();
            fixture.Db = new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(fixture.Connection)
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await fixture.Db.Database.EnsureCreatedAsync();
            var provider = new EphemeralDataProtectionProvider();
            fixture.Protection = provider;
            fixture.Secrets = new(provider);
            fixture.Service = new(fixture.Db, fixture.Secrets, fixture.Files, fixture.Options, fixture.Clock, new(fixture.Db, fixture.Secrets, fixture.Clock));
            fixture.Tasks = new(fixture.Db, provider, fixture.Secrets, fixture.Files, fixture.Mail, fixture.Service, fixture.Options, fixture.Clock);
            var admin = new Account { Email = "admin@example.test", Role = AccountRole.Admin };
            fixture.Db.Accounts.Add(admin);
            fixture.Db.AssessorOnboardings.Add(new()
            {
                EmailNormalized = "assessor@example.test", FullName = "Nguyễn Văn Thử", CreatedByAccountId = admin.Id,
                Status = AssessorOnboardingStatus.ProfileSubmitted, CreatedAt = fixture.Clock.GetUtcNow(),
                SubmittedAt = fixture.Clock.GetUtcNow(), AutoApprove = true, ProfileTokenHash = "sample",
                SensitiveProfileCiphertext = provider.CreateProtector("UniNet.AssessorOnboarding.Profile.v1").Protect(
                    JsonSerializer.Serialize(new ContractProfile("000000000000", "0123456789", "Test bank", "000123456789", "NGUYEN VAN THU")))
            });
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public async Task<AssessorEmailPayload> LatestPayload(string kind)
        {
            var row = await Db.AssessorOnboardingEmails.AsNoTracking().Where(x => x.Kind == kind)
                .OrderByDescending(x => x.CreatedAt).FirstAsync();
            return JsonSerializer.Deserialize<AssessorEmailPayload>(Secrets.Unprotect(row.PayloadCiphertext!))!;
        }

        public async Task<string> Code(string kind) => Regex.Match((await LatestPayload(kind)).Body, @"\b[0-9]{6}\b").Value;
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class TestFiles : IAssessorContractFiles
    {
        public string Hash { get; } = new('B', 64);
        public Task<StoredAssessorContract> Generate(AssessorOnboarding row, ContractProfile profile, CancellationToken ct)
            => Task.FromResult(new StoredAssessorContract("private/test.pdf", Hash));
        public Task<byte[]> Read(string key, string sha256, CancellationToken ct) => Task.FromResult(new byte[] { 1, 2, 3 });
    }

    private sealed class TestMail : IAssessorOnboardingMailSender
    {
        public bool Fail { get; set; }
        public List<AssessorEmailPayload> Sent { get; } = [];
        public Task Send(AssessorEmailPayload email, string messageKey, CancellationToken ct)
        {
            if (Fail) throw new IOException("Test transient failure.");
            Sent.Add(email);
            return Task.CompletedTask;
        }
    }
}
