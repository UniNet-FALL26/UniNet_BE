using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.API.Hubs;
using UniNet.API.Services;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class NotificationsHttpTests
{
    [Theory]
    [InlineData("Assessor")]
    [InlineData("2")]
    [InlineData("Student")]
    public async Task WalletUsesCurrentAccountRoleRatherThanTokenRole(string tokenRole)
    {
        await using var api = await Api.StartAsync();
        var assessor = new Account { Email = "wallet-owner@finance.test", Role = AccountRole.Assessor };
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            db.Add(assessor); await db.SaveChangesAsync();
        }
        using var owner = api.Client(assessor, tokenRole);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/wallet")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/admin/finance/summary")).StatusCode);
        foreach (var role in Enum.GetValues<AccountRole>().Where(role => role != AccountRole.Assessor))
        {
            var account = role == AccountRole.Admin ? api.Admin : new Account { Email = $"wallet-denied-{role}@finance.test", Role = role };
            await using (var scope = api.App.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                if (role != AccountRole.Admin) { db.Add(account); await db.SaveChangesAsync(); }
            }
            using var denied = api.Client(account, "Assessor");
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync("/api/wallet")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync("/api/wallet/withdrawals",
                new { requestKey = Guid.NewGuid(), amount = "1", bankName = "Bank", bankAccountNumber = "123", bankAccountHolder = "Owner" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsync($"/api/wallet/withdrawals/{Guid.NewGuid()}/cancel", null)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Http.GetAsync("/api/wallet")).StatusCode);
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            (await db.Accounts.SingleAsync(a => a.Id == assessor.Id)).Role = AccountRole.Student;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/wallet")).StatusCode);
    }

    [Fact]
    public async Task FinanceHttpEnforcesRoleOwnershipRevocationAndExactMoneyContract()
    {
        await using var api = await Api.StartAsync();
        var accountant = new Account { Email = "accountant-http@finance.test", Role = AccountRole.Accountant, Profile = new() { FullName = "Accountant" } };
        var moderator = new Account { Email = "moderator-http@finance.test", Role = AccountRole.Moderator };
        var assessor = new Account { Email = "assessor-http@finance.test", Role = AccountRole.Assessor };
        var otherAssessor = new Account { Email = "other-assessor-http@finance.test", Role = AccountRole.Assessor };
        await using (var scope = api.App.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); db.AddRange(accountant, moderator, assessor, otherAssessor); await db.SaveChangesAsync();
        }
        using var finance = api.Client(accountant); using var mod = api.Client(moderator); using var student = api.Client(api.Student); using var partner = api.Client(api.Partner); using var admin = api.Client(api.Admin);
        using var owner = api.Client(assessor); using var otherOwner = api.Client(otherAssessor);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/admin/finance/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mod.GetAsync("/api/admin/finance/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/admin/finance/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/finance/summary")).StatusCode);
        var credit = await finance.PostAsJsonAsync("/api/admin/finance/wallet/credits", new { accountId = assessor.Id, amount = "9007199254740993", sourceReference = "EXACT-INCOME", reason = "Documented income" });
        credit.EnsureSuccessStatusCode();
        using var posted = await credit.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("9007199254740993", posted!.RootElement.GetProperty("amount").GetString());
        var walletResponse = await owner.GetAsync("/api/wallet"); Assert.True(walletResponse.Headers.CacheControl?.NoStore);
        using var wallet = await walletResponse.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("9007199254740993", wallet!.RootElement.GetProperty("balance").GetProperty("available").GetString());
        var requestKey = Guid.NewGuid();
        var request = await owner.PostAsJsonAsync("/api/wallet/withdrawals", new { requestKey, amount = "700", bankName = "Bank", bankAccountNumber = "123", bankAccountHolder = "Owner" }); request.EnsureSuccessStatusCode();
        using var row = await request.Content.ReadFromJsonAsync<JsonDocument>(); var id = row!.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(0, row.RootElement.GetProperty("state").GetInt32());
        Assert.False(row.RootElement.TryGetProperty("payingAccount", out _));
        Assert.False(row.RootElement.TryGetProperty("reviewedBy", out _));
        using var otherWallet = await otherOwner.GetFromJsonAsync<JsonDocument>($"/api/wallet?accountId={assessor.Id}");
        Assert.Equal("0", otherWallet!.RootElement.GetProperty("balance").GetProperty("available").GetString());
        Assert.Empty(otherWallet.RootElement.GetProperty("withdrawals").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await finance.GetAsync("/api/admin/finance/withdrawals")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/finance/withdrawals")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.PostAsync($"/api/wallet/withdrawals/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.PostAsync($"/api/wallet/withdrawals/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/wallet")).StatusCode);
        await using (var scope = api.App.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); db.Add(new NotificationPermissionOverride { AccountId = accountant.Id, Permission = NotificationPermissionPolicy.WalletCredit, IsGranted = false }); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await finance.PostAsJsonAsync("/api/admin/finance/wallet/credits", new { accountId = assessor.Id, amount = "1", sourceReference = "DENIED", reason = "Denied" })).StatusCode);
    }
    private sealed class SkillIconCloud : ICloudImageStorage
    {
        public Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct) {
            Assert.Equal("png", request.Format); Assert.StartsWith("uninet/skills/", request.PublicId);
            using var image = SkiaSharp.SKBitmap.Decode(request.Bytes);
            Assert.NotNull(image); Assert.InRange(image.Width, 1, 512); Assert.InRange(image.Height, 1, 512);
            return Task.FromResult(new ImageUploadResult($"https://res.cloudinary.com/fixture/image/upload/v1/{request.PublicId}.png", request.PublicId, "png"));
        }
        public Task Destroy(string publicId, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task SkillIconsValidateFilesRequireAdminProcessTransparencyAndUploadToCloud()
    {
        await using var api = await Api.StartAsync();
        using var admin = api.Client(api.Admin); using var student = api.Client(api.Student);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "portrait-sample.png"));
        MultipartFormDataContent Form(byte[] data) { var form = new MultipartFormDataContent(); var content = new ByteArrayContent(data); content.Headers.ContentType = new("image/png"); form.Add(content, "file", "icon.png"); return form; }
        using (var form = Form(bytes)) Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync("/api/admin/skills/icons", form)).StatusCode);
        using (var form = Form(bytes)) Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync("/api/admin/skills/icons/preview", form)).StatusCode);
        using (var form = Form(bytes)) {
            var preview = await admin.PostAsync("/api/admin/skills/icons/preview", form);
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            using var image = SkiaSharp.SKBitmap.Decode(await preview.Content.ReadAsByteArrayAsync());
            Assert.Equal(512, image.Width); Assert.Equal(512, image.Height);
        }
        using (var form = Form(bytes)) Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync("/api/admin/skills/icons/remove-background", form)).StatusCode);
        using (var form = Form("fake-image"u8.ToArray())) Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/skills/icons", form)).StatusCode);
        using var source = Form(bytes);
        var processed = await admin.PostAsync("/api/admin/skills/icons/remove-background", source);
        Assert.Equal(HttpStatusCode.OK, processed.StatusCode); Assert.Equal("image/png", processed.Content.Headers.ContentType!.MediaType);
        var png = await processed.Content.ReadAsByteArrayAsync();
        using (var image = SkiaSharp.SKBitmap.Decode(png)) { Assert.InRange(image.Width, 1, 512); Assert.Contains(image.Pixels, pixel => pixel.Alpha == 0); }
        using var uploadForm = Form(png);
        var uploaded = await admin.PostAsync("/api/admin/skills/icons", uploadForm);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var result = await uploaded.Content.ReadFromJsonAsync<ImageUploadResult>();
        var saved = await admin.PostAsJsonAsync("/api/admin/skills", new SaveAdminSkill("Cloud icon", SkillCategory.Frontend, true, result!.ImageUrl));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        Assert.Equal(result.ImageUrl, (await saved.Content.ReadFromJsonAsync<AdminSkillItem>())!.IconUrl);
    }

    [Fact]
    public async Task SkillsApiUsesDatabaseAndRequiresAdmin()
    {
        await using var api = await Api.StartAsync();
        using var admin = api.Client(api.Admin); using var student = api.Client(api.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/admin/skills")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync("/api/admin/skills", new SaveAdminSkill("HTTP skill", SkillCategory.Frontend, true))).StatusCode);
        var created = await admin.PostAsJsonAsync("/api/admin/skills", new SaveAdminSkill("HTTP skill", SkillCategory.Frontend, true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var skill = await created.Content.ReadFromJsonAsync<AdminSkillItem>();
        var list = await admin.GetFromJsonAsync<AdminPage<AdminSkillItem>>("/api/admin/skills?search=HTTP&category=0&isActive=true&pageSize=1");
        Assert.Equal(skill!.Id, Assert.Single(list!.Items).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/skills", new SaveAdminSkill("http SKILL", null, true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/skills?category=9")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/skills/{skill.Id}", new SaveAdminSkill("HTTP updated", SkillCategory.Backend, false))).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<AdminPage<AdminSkillItem>>("/api/admin/skills?search=HTTP&isActive=true"))!.Items);
    }

    [Fact]
    public async Task AdminUserEndpointsEnforcePermissionsAndLockedJwtStaysInvalidAfterUnlock()
    {
        await using var api = await Api.StartAsync();
        using var admin = api.Client(api.Admin); using var student = api.Client(api.Student);
        using var socket = await api.ConnectAsync(api.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/admin/users")).StatusCode);
        var list = await admin.GetFromJsonAsync<AdminPage<AdminUserItem>>("/api/admin/users?role=0&pageSize=1");
        Assert.Equal(api.Student.Id, Assert.Single(list!.Items).Id);
        var detail = await admin.GetStringAsync($"/api/admin/users/{api.Student.Id}");
        Assert.DoesNotContain("password", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{api.Student.Id}/lock", new { reason = "Violation" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync("/api/notifications")).StatusCode);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10))) {
            var sessionClosed = false;
            try {
                var buffer = new byte[4096];
                while (!sessionClosed) {
                    var message = await socket.ReceiveAsync(buffer, timeout.Token);
                    sessionClosed = message.MessageType == WebSocketMessageType.Close;
                    if (message.MessageType == WebSocketMessageType.Text)
                        foreach (var frame in Encoding.UTF8.GetString(buffer, 0, message.Count).Split('\x1e', StringSplitOptions.RemoveEmptyEntries)) {
                            using var json = JsonDocument.Parse(frame);
                            if (json.RootElement.TryGetProperty("type", out var type) && type.GetInt32() == 7) sessionClosed = true;
                        }
                }
            } catch (WebSocketException) { sessionClosed = true; }
            Assert.True(sessionClosed);
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/users/{api.Student.Id}/unlock", new { reason = "Reviewed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await student.GetAsync("/api/notifications")).StatusCode);
        await using (var scope = api.App.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            api.Student.SessionVersion = await db.Accounts.Where(a => a.Id == api.Student.Id).Select(a => a.SessionVersion).SingleAsync();
        }
        using var newSession = api.Client(api.Student);
        Assert.Equal(HttpStatusCode.OK, (await newSession.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/admin/users/{api.Admin.Id}/lock", new { reason = "Self" })).StatusCode);
    }
    private const string Issuer = "notification-http-tests";
    private const string Key = "isolated-notification-http-test-key-0123456789";
    private static readonly ConditionalWeakTable<ClientWebSocket, MemoryStream> Buffers = new();

    [Fact]
    public async Task HttpRequiresAuthenticationAndKeepsEveryInboxAndReadStatePrivate()
    {
        await using var api = await Api.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Http.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Http.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null)).StatusCode);
        var studentNotice = await api.SendAsync(api.Student.Id, "student");
        var partnerNotice = await api.SendAsync(api.Partner.Id, "partner");
        var adminNotice = await api.SendAsync(api.Admin.Id, "admin");
        using var student = api.Client(api.Student);
        using var partner = api.Client(api.Partner);
        using var admin = api.Client(api.Admin);
        Assert.Equal(studentNotice, Assert.Single((await student.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.Items).Id);
        Assert.Equal(partnerNotice, Assert.Single((await partner.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.Items).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await student.GetAsync($"/api/notifications/{partnerNotice}/target")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await partner.GetAsync($"/api/notifications/{adminNotice}/target")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/notifications/{studentNotice}/target")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await student.PatchAsync($"/api/notifications/{partnerNotice}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await student.PatchAsync("/api/notifications/read-all", null)).StatusCode);
        Assert.Equal(0, (await student.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.UnreadCount);
        Assert.Equal(1, (await partner.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.UnreadCount);
        Assert.Equal(1, (await admin.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.UnreadCount);

        Guid privateStaffNotice;
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            await NotificationService.EnqueuePermissionAsync(db, NotificationPermissionPolicy.StudentVerificationReview,
                new("verification.student.submitted", "Verification", "Cần xác minh", "Có hồ sơ mới.", EventKey: "permission-private"));
            await db.SaveChangesAsync();
            privateStaffNotice = (await db.Set<VerificationNotification>().SingleAsync(n => n.EventKey == "permission-private")).Id;
            db.Set<NotificationPermissionOverride>().Add(new() { AccountId = api.Admin.Id,
                Permission = NotificationPermissionPolicy.StudentVerificationReview, IsGranted = false });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/notifications/{privateStaffNotice}/target")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PatchAsync($"/api/notifications/{privateStaffNotice}/read", null)).StatusCode);
        Assert.DoesNotContain((await admin.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.Items, n => n.Id == privateStaffNotice);
    }

    [Fact]
    public async Task RealSignalRUsesJwtAccountSubjectAndOfflineReconnectResyncUsesDatabase()
    {
        await using var api = await Api.StartAsync();
        Assert.NotEqual(api.Student.Id, api.Student.Profile!.Id);
        using var student = await api.ConnectAsync(api.Student);
        using var partner = await api.ConnectAsync(api.Partner);
        using var admin = await api.ConnectAsync(api.Admin);
        var hub = api.App.Services.GetRequiredService<IHubContext<NotificationsHub>>();
        foreach (var account in new[] { api.Student, api.Partner, api.Admin })
            await hub.Clients.User(account.Id.ToString()).SendAsync("Sentinel", account.Id.ToString());
        foreach (var (socket, account) in new[] { (student, api.Student), (partner, api.Partner), (admin, api.Admin) })
        {
            using var message = await ReceiveAsync(socket);
            Assert.Equal("Sentinel", message.RootElement.GetProperty("target").GetString());
            Assert.Equal(account.Id.ToString(), message.RootElement.GetProperty("arguments")[0].GetString());
        }
        // A second per-user marker also detects any queued cross-account leakage without timeout assertions.
        foreach (var account in new[] { api.Student, api.Partner, api.Admin })
            await hub.Clients.User(account.Id.ToString()).SendAsync("Fence", account.Id.ToString());
        foreach (var (socket, account) in new[] { (student, api.Student), (partner, api.Partner), (admin, api.Admin) })
        {
            using var message = await ReceiveAsync(socket);
            Assert.Equal("Fence", message.RootElement.GetProperty("target").GetString());
            Assert.Equal(account.Id.ToString(), message.RootElement.GetProperty("arguments")[0].GetString());
        }
        await student.CloseAsync(WebSocketCloseStatus.NormalClosure, "offline", CancellationToken.None);
        var offlineNotice = await api.SendAsync(api.Student.Id, "created-offline");
        using var reconnect = await api.ConnectAsync(api.Student);
        using var client = api.Client(api.Student);
        var synchronized = (await client.GetFromJsonAsync<NotificationPage>("/api/notifications"))!;
        Assert.Equal(offlineNotice, Assert.Single(synchronized.Items).Id);
        Assert.Equal(1, synchronized.UnreadCount);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PatchAsync($"/api/notifications/{offlineNotice}/read", null)).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.UnreadCount);
    }

    [Fact]
    public async Task PersistedOutboxSynchronizesCreationAndReadAcrossTheAccountsConnections()
    {
        await using var api = await Api.StartAsync(withWorker: true);
        using var first = await api.ConnectAsync(api.Student);
        using var second = await api.ConnectAsync(api.Student);
        using var partner = await api.ConnectAsync(api.Partner);
        var noticeId = await api.SendAsync(api.Student.Id, "outbox-realtime");
        foreach (var socket in new[] { first, second })
        {
            using var created = await ReceiveAsync(socket);
            Assert.Equal("NotificationChanged", created.RootElement.GetProperty("target").GetString());
            var change = created.RootElement.GetProperty("arguments")[0];
            Assert.Equal(noticeId, change.GetProperty("notificationId").GetGuid());
            Assert.Equal("created", change.GetProperty("reason").GetString());
        }
        using var studentClient = api.Client(api.Student);
        Assert.Equal(HttpStatusCode.NoContent, (await studentClient.PatchAsync($"/api/notifications/{noticeId}/read", null)).StatusCode);
        foreach (var socket in new[] { first, second })
        {
            using var read = await ReceiveAsync(socket);
            Assert.Equal("NotificationChanged", read.RootElement.GetProperty("target").GetString());
            Assert.Equal("read", read.RootElement.GetProperty("arguments")[0].GetProperty("reason").GetString());
        }
        // Fence after both deliveries proves the partner did not receive another account's invalidations.
        await api.App.Services.GetRequiredService<IHubContext<NotificationsHub>>().Clients.User(api.Partner.Id.ToString()).SendAsync("Fence", "partner");
        using var fence = await ReceiveAsync(partner);
        Assert.Equal("Fence", fence.RootElement.GetProperty("target").GetString());
        Assert.Equal(0, (await studentClient.GetFromJsonAsync<NotificationPage>("/api/notifications"))!.UnreadCount);
    }

    private static async Task<JsonDocument> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = Buffers.GetValue(socket, _ => new MemoryStream());
        var bytes = new byte[4096];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var data = buffer.ToArray();
            var separator = Array.IndexOf(data, (byte)0x1e);
            if (separator >= 0)
            {
                var json = JsonDocument.Parse(data.AsMemory(0, separator));
                buffer.SetLength(0); buffer.Write(data, separator + 1, data.Length - separator - 1);
                if (json.RootElement.TryGetProperty("type", out var type) && type.GetInt32() == 6) { json.Dispose(); continue; }
                return json;
            }
            var result = await socket.ReceiveAsync(bytes, deadline.Token);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            buffer.Write(bytes, 0, result.Count);
        }
    }

    private sealed class Api(WebApplication app, string database, Account[] accounts) : IAsyncDisposable
    {
        public WebApplication App => app;
        public Account Student => accounts[0];
        public Account Partner => accounts[1];
        public Account Admin => accounts[2];
        public HttpClient Http { get; } = new() { BaseAddress = new Uri(app.Urls.Single()) };
        private static string Token(Account account, string? tokenRole = null) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            Issuer, Issuer, [new(JwtRegisteredClaimNames.Sub, account.Id.ToString()), new(ClaimTypes.Role, tokenRole ?? account.Role.ToString()), new("session_version", account.SessionVersion.ToString())],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));
        public HttpClient Client(Account account, string? tokenRole = null)
        {
            var client = new HttpClient { BaseAddress = Http.BaseAddress };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(account, tokenRole));
            return client;
        }
        public async Task<Guid> SendAsync(Guid accountId, string key)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            await scope.ServiceProvider.GetRequiredService<NotificationService>().SendToUserAsync(accountId,
                new("system.test", "System", "Thông báo", key, TargetUrl: "/notifications", EventKey: key));
            return (await db.Set<VerificationNotification>().SingleAsync(n => n.RecipientUserId == accountId && n.EventKey == key)).Id;
        }
        public async Task<ClientWebSocket> ConnectAsync(Account account)
        {
            using var client = Client(account);
            using var response = await client.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null);
            response.EnsureSuccessStatusCode();
            using var negotiation = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var connectionToken = negotiation!.RootElement.GetProperty("connectionToken").GetString();
            var socket = new ClientWebSocket();
            var uri = new UriBuilder(new Uri(Http.BaseAddress!, $"/hubs/notifications?id={Uri.EscapeDataString(connectionToken!)}&access_token={Uri.EscapeDataString(Token(account))}")) { Scheme = "ws" }.Uri;
            await socket.ConnectAsync(uri, CancellationToken.None);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, CancellationToken.None);
            using var handshake = await ReceiveAsync(socket);
            Assert.False(handshake.RootElement.TryGetProperty("error", out _));
            return socket;
        }
        public static async Task<Api> StartAsync(bool withWorker = false)
        {
            var database = Path.Combine(Path.GetTempPath(), $"uninet-notifications-{Guid.NewGuid():N}.sqlite");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<UniNetDbContext>(options => options.UseSqlite($"Data Source={database}")
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>());
            builder.Services.AddScoped<NotificationService>(); builder.Services.AddScoped<NotificationTargets>();
            builder.Services.AddScoped<AdminUsersService>(); builder.Services.AddScoped<AdminPermissionService>();
            builder.Services.AddScoped<AdminSkillsService>(); builder.Services.AddScoped<FinanceService>();
            builder.Services.AddSingleton<ICloudImageStorage>(new SkillIconCloud());
            builder.Services.AddSingleton<OnnxPortraitProcessor>();
            if (withWorker)
            {
                builder.Services.AddScoped<NotificationBroadcastService>();
                builder.Services.AddHostedService<UniNet.API.Services.NotificationBackgroundWorker>();
            }
            builder.Services.AddControllers().AddApplicationPart(typeof(NotificationsController).Assembly);
            builder.Services.AddSingleton<UniNet.API.Services.AccountSessionConnections>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<UniNet.API.Services.AccountSessionConnections>());
            builder.Services.AddSignalR(options => options.AddFilter<UniNet.API.Services.AccountSessionConnections>()); builder.Services.AddSingleton<IUserIdProvider, AccountUserIdProvider>();
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.Events = new JwtBearerEvents { OnMessageReceived = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/hubs/notifications") && context.Request.Query.TryGetValue("access_token", out var token)) context.Token = token;
                    return Task.CompletedTask;
                }, OnTokenValidated = async context => {
                    if (!await UniNet.API.Services.AccountTokenValidation.ValidateAsync(context.Principal, context.HttpContext.RequestServices.GetRequiredService<UniNetDbContext>(), context.HttpContext.RequestAborted)) context.Fail("Session revoked");
                } };
                options.TokenValidationParameters = new() { ValidIssuer = Issuer, ValidAudience = Issuer,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), ValidateLifetime = true, RoleClaimType = ClaimTypes.Role };
            });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseExceptionHandler(errors => errors.Run(async context =>
            {
                var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                context.Response.StatusCode = error is ProjectVerificationException failure ? failure.Status : error is AuthException auth ? auth.Status : 500;
                await context.Response.WriteAsJsonAsync(new { code = error is ProjectVerificationException e ? e.Code : "INTERNAL_ERROR" });
            }));
            app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            app.MapHub<NotificationsHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
            Account[] users;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); await db.Database.EnsureCreatedAsync();
                users = new[] { AccountRole.Student, AccountRole.Partner, AccountRole.Admin }.Select((role, i) => new Account
                { Email = $"notifications-http-{i}@example.test", Role = role, Profile = new UserProfile { FullName = role.ToString(), Nickname = $"notice{i}" } }).ToArray();
                db.Accounts.AddRange(users); await db.SaveChangesAsync();
            }
            await app.StartAsync(); return new(app, database, users);
        }
        public async ValueTask DisposeAsync()
        {
            Http.Dispose(); await app.StopAsync(); await app.DisposeAsync();
            SqliteConnection.ClearAllPools(); File.Delete(database);
        }
    }
}
