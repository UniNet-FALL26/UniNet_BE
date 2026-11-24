using System.Text.Json;
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
    public async Task InvitationEmailUsesTheProfileLinkAndIsSkippedAfterRevocation(bool revoke)
    {
        await using var fixture = await Fixture.Create();
        var admin = await fixture.Db.Accounts.SingleAsync(x => x.Role == AccountRole.Admin);
        var onboarding = new AssessorOnboardingService(fixture.Db, fixture.Protection, fixture.Clock,
            new(fixture.Db, fixture.Secrets, fixture.Clock), fixture.Options);

        var invitation = await onboarding.Invite(admin.Id, new("new-assessor@example.test"), default);
        var queued = await fixture.Db.AssessorOnboardingEmails.AsNoTracking().SingleAsync(default(CancellationToken));
        Assert.Equal(invitation.Id, queued.OnboardingId);
        Assert.Equal(invitation.ExpiresAt, queued.ExpiresAt);
        Assert.DoesNotContain(invitation.ProfileLink, queued.PayloadCiphertext!);

        if (revoke)
            await onboarding.Revoke(admin.Id, invitation.Id, default);
        await fixture.Tasks.SendNext(default);

        var processed = await fixture.Db.AssessorOnboardingEmails.AsNoTracking().SingleAsync(default(CancellationToken));
        Assert.Null(processed.PayloadCiphertext);
        if (revoke)
        {
            Assert.Empty(fixture.Mail.Sent);
            Assert.NotNull(processed.CancelledAt);
            return;
        }

        var sent = Assert.Single(fixture.Mail.Sent);
        Assert.Equal(invitation.Email, sent.Recipient);
        Assert.Contains("https://example.test" + invitation.ProfileLink, sent.Body);
        Assert.NotNull(processed.SentAt);
    }

    [Fact]
    public async Task SubmittedProfileCanBeSignedApprovedAndActivatedForNormalLogin()
    {
        await using var fixture = await Fixture.Create();
        var profileToken = AssessorContractSecrets.Token();
        var profileHash = AssessorContractSecrets.Hash(profileToken);
        var expires = fixture.Clock.GetUtcNow().AddDays(7);
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x =>
            x.SetProperty(p => p.Status, AssessorOnboardingStatus.Invited)
                .SetProperty(p => p.ProfileTokenHash, profileHash).SetProperty(p => p.ProfileTokenExpiresAt, expires)
                .SetProperty(p => p.AutoApprove, true));
        fixture.Db.ChangeTracker.Clear();
        var profile = new AssessorOnboardingService(fixture.Db, fixture.Protection, fixture.Clock,
            new(fixture.Db, fixture.Secrets, fixture.Clock), fixture.Options, fixture.Files);
        var submitted = await profile.Submit(new(profileToken, "Nguyễn Văn Thử", "000000000000",
            "Ngân hàng thử nghiệm", "000123456789", "NGUYEN VAN THU"), default);
        Assert.Equal("AwaitingSignature", submitted.Status);
        Assert.EndsWith("#token=" + profileToken, submitted.ContractLink);
        Assert.Empty(await fixture.Db.AssessorOnboardingEmails.ToListAsync());
        await fixture.Service.Inspect(profileToken, default);
        await fixture.Service.Sign(new(profileToken, "Nguyễn Văn Thử", fixture.Files.Hash, true), default);
        var activation = new AssessorActivationService(fixture.Db, new(fixture.Db, fixture.Secrets, fixture.Clock),
            fixture.Options, fixture.Clock);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await activation.ApproveNext(default);
        Assert.Equal(AssessorOnboardingStatus.AwaitingApproval, (await fixture.Db.AssessorOnboardings.SingleAsync()).Status);
        var admin = await fixture.Db.Accounts.SingleAsync(x => x.Role == AccountRole.Admin);
        await activation.Approve(admin.Id, submitted.Id, default);
        var passwordToken = (await fixture.LatestPayload("SetPassword")).Body.Split("#token=")[1];
        await fixture.Tasks.SendNext(default);
        Assert.Single(fixture.Mail.Sent);
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
    public async Task HttpContractEndpointsRequireInvitationCredentialAndAdminApproval()
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
        builder.Services.AddSingleton(new AssessorOnboardingService(fixture.Db, fixture.Protection, fixture.Clock,
            new(fixture.Db, fixture.Secrets, fixture.Clock), fixture.Options));
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
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync(route + "pdf",
            new { token = AssessorContractSecrets.Token() })).StatusCode);
        var online = await client.PostAsJsonAsync(route + "inspect", new { token });
        online.EnsureSuccessStatusCode();
        var detail = await online.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Contract test", detail.GetProperty("pages")[0].GetString());
        var signature = new { token, signedName = "Nguyễn Văn Thử", accepted = true, contractSha256 = fixture.Files.Hash };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route + "sign", signature)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync(route + "sign", signature)).StatusCode);
        var pdf = await client.PostAsJsonAsync(route + "pdf", new { token });
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.True(pdf.Headers.CacheControl!.NoStore);
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
        var approval = await client.PostAsync(reviewRoute + "/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var approvalBody = await approval.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(row.CreatedByAccountId, approvalBody.GetProperty("reviewedByAccountId").GetGuid());
        Assert.Equal(fixture.Clock.GetUtcNow(), approvalBody.GetProperty("reviewedAt").GetDateTimeOffset());
        var persistedReview = await fixture.Db.AssessorOnboardings.AsNoTracking().SingleAsync();
        Assert.Equal(row.CreatedByAccountId, persistedReview.ReviewedByAccountId);
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
    public async Task SignatureRequiresMatchingNameViewedDocumentAndMatchingHash()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var token = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Sign(
            new(token, "Nguyễn Văn Thử", fixture.Files.Hash, true), default));
        await fixture.Service.Inspect(token, default);
        await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Sign(
            new(token, "Someone else", fixture.Files.Hash, true), default));
        await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Sign(
            new(token, "Nguyễn Văn Thử", new string('A', 64), true), default));
        await fixture.Service.Sign(new(token, "Nguyễn Văn Thử", fixture.Files.Hash, true), default);
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        Assert.Equal(AssessorOnboardingStatus.AwaitingApproval, row.Status);
        Assert.Null(row.AccountId);
        Assert.NotNull(row.SignedAt);
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
        fixture.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Pdf(token, default))).Status);
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssessorOnboardingStatus.Revoked));
        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Inspect(token, default))).Status);
    }

    [Fact]
    public async Task ResendingContractKeepsPdfAndCancelsPreviousLink()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var oldToken = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var row = await fixture.Db.AssessorOnboardings.SingleAsync();
        var key = row.ContractStorageKey;
        await fixture.Service.Resend(row.CreatedByAccountId, row.Id, default);
        Assert.Equal(key, row.ContractStorageKey);
        Assert.Equal(410, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Inspect(oldToken, default))).Status);
        await fixture.Tasks.SendNext(default);
        Assert.Empty(fixture.Mail.Sent); // The oldest, superseded contract message is discarded.
    }

    [Fact]
    public async Task ConcurrentUpdatesCannotMarkContractViewedAgainstStaleRevision()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Tasks.GenerateNext(default);
        var token = (await fixture.LatestPayload("Contract")).Body.Split("#token=")[1];
        fixture.Db.ChangeTracker.Clear();
        await fixture.Db.AssessorOnboardings.SingleAsync(); // Stale tracked revision.
        var nextRevision = Guid.NewGuid();
        await fixture.Db.AssessorOnboardings.ExecuteUpdateAsync(x => x.SetProperty(p => p.Revision, nextRevision));
        Assert.Equal(409, (await Assert.ThrowsAsync<AuthException>(() => fixture.Service.Inspect(token, default))).Status);
        Assert.Null((await fixture.Db.AssessorOnboardings.AsNoTracking().SingleAsync()).ContractViewedAt);
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
            fixture.Service = new(fixture.Db, fixture.Files, fixture.Options, fixture.Clock, new(fixture.Db, fixture.Secrets, fixture.Clock));
            fixture.Tasks = new(fixture.Db, provider, fixture.Secrets, fixture.Files, fixture.Mail, fixture.Service, fixture.Options, fixture.Clock);
            var admin = new Account { Email = "admin@example.test", Role = AccountRole.Admin };
            fixture.Db.Accounts.Add(admin);
            fixture.Db.AssessorOnboardings.Add(new()
            {
                EmailNormalized = "assessor@example.test", FullName = "Nguyễn Văn Thử", CreatedByAccountId = admin.Id,
                Status = AssessorOnboardingStatus.ProfileSubmitted, CreatedAt = fixture.Clock.GetUtcNow(),
                SubmittedAt = fixture.Clock.GetUtcNow(), AutoApprove = true, ProfileTokenHash = "sample",
                SensitiveProfileCiphertext = provider.CreateProtector("UniNet.AssessorOnboarding.Profile.v1").Protect(
                    JsonSerializer.Serialize(new ContractProfile("000000000000", "Test bank", "000123456789", "NGUYEN VAN THU")))
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
        public Task<byte[]> Export(AssessorOnboarding row, CancellationToken ct) => Read(row.ContractStorageKey!, row.ContractSha256!, ct);
        public Task<byte[]> Read(string key, string sha256, CancellationToken ct) {
            using var document = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
            var font = document.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
            var page = document.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            page.AddText("Contract test", 12, new UglyToad.PdfPig.Core.PdfPoint(55, 785), font);
            return Task.FromResult(document.Build());
        }
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
