using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.DTOs.Verification;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class StudentVerificationHttpTests
{
    [Fact]
    public async Task MultipartSubmitReviewAndPrivateDownloadEnforceRealJwtAuthorization()
    {
        await using var api = await Api.Start();
        using var student = api.Client(api.Student, "Student");
        using var other = api.Client(api.Other, "Student");
        using var admin = api.Client(api.Admin, "Admin");
        using var partner = api.Client(api.Other, "Partner");
        using var anonymous = api.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/student/verification/me")).StatusCode);
        using var expired = api.Client(api.Student, "Student", expired: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("api/student/verification/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.GetAsync("api/student/verification/me")).StatusCode);
        using (var missing = Body(includeName: false))
            Assert.Equal(HttpStatusCode.BadRequest, (await student.PostAsync("api/student/verification", missing)).StatusCode);
        using var body = Body();
        using var concurrentBody = Body();
        var submissions = await Task.WhenAll(student.PostAsync("api/student/verification", body), student.PostAsync("api/student/verification", concurrentBody));
        Assert.Single(submissions, x => x.StatusCode == HttpStatusCode.Conflict);
        var submission = Assert.Single(submissions, x => x.IsSuccessStatusCode);
        submission.EnsureSuccessStatusCode();
        var result = (await submission.Content.ReadFromJsonAsync<StudentVerificationStatusResponse>())!;
        var id = result.Id!.Value;
        Assert.Equal(VerificationStatus.Pending, result.Status);
        using (var duplicate = Body())
            Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsync("api/student/verification", duplicate)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"api/student/verification/{id}/document")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"api/student/verification/{id}/document")).StatusCode);
        foreach (var client in new[] { student, admin })
        {
            var file = await client.GetAsync($"api/student/verification/{id}/document");
            file.EnsureSuccessStatusCode();
            Assert.Equal("attachment", file.Content.Headers.ContentDisposition!.DispositionType);
            Assert.True(file.Headers.CacheControl!.NoStore);
            Assert.Contains("nosniff", file.Headers.GetValues("X-Content-Type-Options"));
            Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
            Assert.Equal("%PDF-1.7\nprivate-document", await file.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsync($"api/admin/student-verifications/{id}/approve", null)).StatusCode);
        var queue = (await admin.GetFromJsonAsync<StudentVerificationQueueResponse>("api/admin/student-verifications?status=0"))!;
        Assert.Equal(id, Assert.Single(queue.Items).Id);
        Assert.Equal(VerificationStatus.Pending, (await admin.GetFromJsonAsync<StudentVerificationStatusResponse>($"api/admin/student-verifications/{id}"))!.Status);
        (await admin.PostAsync($"api/admin/student-verifications/{id}/reject", JsonContent.Create(new { reason = "Ảnh chưa rõ" }))).EnsureSuccessStatusCode();
        Assert.Equal("Ảnh chưa rõ", (await student.GetFromJsonAsync<StudentVerificationStatusResponse>("api/student/verification/me"))!.RejectReason);
        var notices = (await student.GetFromJsonAsync<List<VerificationNotification>>("api/verification-notifications"))!;
        Assert.Equal(id, Assert.Single(notices).UserVerificationId);
        Assert.Empty((await other.GetFromJsonAsync<List<VerificationNotification>>("api/verification-notifications"))!);
        await using (var scope = api.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            (await db.UserVerifications.SingleAsync()).SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
        }
        using var second = Body();
        var resubmitted = (await (await student.PostAsync("api/student/verification", second)).Content.ReadFromJsonAsync<StudentVerificationStatusResponse>())!;
        var reviews = await Task.WhenAll(admin.PostAsync($"api/admin/student-verifications/{resubmitted.Id}/approve", null),
            admin.PostAsync($"api/admin/student-verifications/{resubmitted.Id}/approve", null));
        Assert.Single(reviews, x => x.IsSuccessStatusCode);
        Assert.Single(reviews, x => x.StatusCode == HttpStatusCode.Conflict);
        var approved = (await student.GetFromJsonAsync<StudentVerificationStatusResponse>("api/student/verification/me"))!;
        Assert.True(approved.IsVerified);
        Assert.Equal(VerificationStatus.Approved, approved.Status);
    }

    private static MultipartFormDataContent Body(bool includeName = true)
    {
        var body = new MultipartFormDataContent();
        if (includeName) body.Add(new StringContent("Trịnh Trọng Quyền"), "fullName");
        body.Add(new StringContent("Any College"), "schoolName");
        body.Add(new StringContent("SE170347"), "studentCode");
        var file = new ByteArrayContent("%PDF-1.7\nprivate-document"u8.ToArray());
        file.Headers.ContentType = new("application/pdf");
        body.Add(file, "document", "student-card.pdf");
        return body;
    }

    private sealed class Api(WebApplication app, string database, Guid student, Guid other, Guid admin) : IAsyncDisposable
    {
        private const string Key = "isolated-student-verification-key-01234567890123456789";
        public WebApplication App => app;
        public Guid Student => student;
        public Guid Other => other;
        public Guid Admin => admin;
        public HttpClient Client(Guid? id = null, string role = "Student", bool expired = false)
        {
            var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
            if (id.HasValue)
            {
                var token = new JwtSecurityToken("student-tests", "student-tests", [new(JwtRegisteredClaimNames.Sub, id.ToString()!), new(ClaimTypes.Role, role)],
                    expires: expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddMinutes(5),
                    signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            return client;
        }
        public static async Task<Api> Start()
        {
            var database = Path.Combine(Path.GetTempPath(), $"uninet-student-{Guid.NewGuid():N}.sqlite");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<UniNetDbContext>(options => options.UseSqlite($"Data Source={database}").ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>());
            builder.Services.AddScoped<StudentVerificationService>(); builder.Services.AddScoped<VerificationNotifications>();
            builder.Services.AddControllers().AddApplicationPart(typeof(StudentVerificationController).Assembly);
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new() { ValidIssuer = "student-tests", ValidAudience = "student-tests", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), ValidateLifetime = true, RoleClaimType = ClaimTypes.Role };
            });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseExceptionHandler(errors => errors.Run(async context =>
            {
                var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                context.Response.StatusCode = exception is AuthException auth ? auth.Status : 500;
                await context.Response.WriteAsJsonAsync(new { code = exception is AuthException e ? e.Code : "INTERNAL_ERROR" });
            }));
            app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            Account[] users;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>(); await db.Database.EnsureCreatedAsync();
                users = new[] { AccountRole.Student, AccountRole.Student, AccountRole.Admin }.Select((role, i) => new Account
                { Email = $"student-http-{i}@example.test", Role = role, Profile = new UserProfile { FullName = "Student", Nickname = $"user{i}" } }).ToArray();
                db.Accounts.AddRange(users); await db.SaveChangesAsync();
            }
            await app.StartAsync(); return new(app, database, users[0].Id, users[1].Id, users[2].Id);
        }
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync(); await app.DisposeAsync(); SqliteConnection.ClearAllPools(); File.Delete(database);
        }
    }
}
