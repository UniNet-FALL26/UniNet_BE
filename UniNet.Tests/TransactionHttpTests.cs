using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed class TransactionHttpTests
{
    [Fact]
    public async Task PaymentRoutesRequireStudentAndWebhookAllowsAnonymousWhileReconciliationIsBackgroundOnly()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("payment-http-test-key-at-least-32-bytes"));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(TransactionController).Assembly);
        var service = new FakeService();
        builder.Services.AddSingleton<ITransactionService>(service);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = false, ValidateAudience = false, ValidateIssuerSigningKey = true,
                IssuerSigningKey = key, RoleClaimType = ClaimTypes.Role
            };
        });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
        var basePath = $"api/project-verifications/{service.VerificationId}/payments";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(basePath)).StatusCode);

        string Token(string role) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new(JwtRegisteredClaimNames.Sub, service.AccountId.ToString()), new(ClaimTypes.Role, role)],
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new(key, SecurityAlgorithms.HmacSha256)));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("Admin"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(basePath, null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token("Student"));
        using var create = await client.PostAsync(basePath, null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.EndsWith($"/{service.TransactionId}", create.Headers.Location!.ToString());
        var created = await create.Content.ReadFromJsonAsync<TransactionResponse>();
        Assert.Equal(service.TransactionId, created!.Id);
        var history = await client.GetFromJsonAsync<TransactionsResponse>($"{basePath}?page=2&pageSize=5");
        Assert.Equal(2, history!.Page);
        Assert.Equal(5, history.PageSize);
        Assert.Equal(service.TransactionId, (await client.GetFromJsonAsync<TransactionResponse>(
            $"{basePath}/{service.TransactionId}"))!.Id);
        using var reconcile = await client.PostAsync($"api/payments/{service.TransactionId}/reconcile", null);
        Assert.Equal(HttpStatusCode.NotFound, reconcile.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var webhook = await client.PostAsJsonAsync("api/webhooks/payos", new { data = new { orderCode = 123 } });
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Equal(1, service.WebhookCalls);
        Assert.Equal(0, service.ReconcileCalls);
        await app.StopAsync();
    }

    private sealed class FakeService : ITransactionService
    {
        public Guid VerificationId { get; } = Guid.NewGuid();
        public Guid TransactionId { get; } = Guid.NewGuid();
        public Guid AccountId { get; } = Guid.NewGuid();
        public int WebhookCalls { get; private set; }
        public int ReconcileCalls { get; private set; }
        private TransactionResponse Response => new(TransactionId, VerificationId, Guid.NewGuid(), 37000, "VND",
            PaymentStatus.Pending, "PayOS", "123", "test", "https://pay.payos.vn/web/test", null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, false, null);
        public Task<TransactionResponse> CreateAsync(Guid verificationId, Guid accountId, CancellationToken ct = default)
        {
            Assert.Equal(VerificationId, verificationId);
            Assert.Equal(AccountId, accountId);
            return Task.FromResult(Response);
        }
        public Task<TransactionsResponse> GetHistoryAsync(Guid verificationId, Guid accountId,
            int page = 1, int pageSize = 10, CancellationToken ct = default)
        {
            Assert.Equal(VerificationId, verificationId);
            Assert.Equal(AccountId, accountId);
            return Task.FromResult(new TransactionsResponse(page, pageSize, 1, 1, [Response]));
        }
        public Task<TransactionResponse> GetAsync(Guid verificationId, Guid transactionId, Guid accountId,
            CancellationToken ct = default)
        {
            Assert.Equal(VerificationId, verificationId);
            Assert.Equal(TransactionId, transactionId);
            Assert.Equal(AccountId, accountId);
            return Task.FromResult(Response);
        }
        public Task<TransactionResponse> ReconcileAsync(Guid transactionId, Guid accountId, CancellationToken ct = default)
        {
            Assert.Equal(TransactionId, transactionId);
            Assert.Equal(AccountId, accountId);
            ReconcileCalls++;
            return Task.FromResult(Response);
        }
        public Task<PaymentWebhookResponse> HandlePayOSWebhookAsync(JsonElement payload, CancellationToken ct = default)
        {
            Assert.Equal(123, payload.GetProperty("data").GetProperty("orderCode").GetInt32());
            WebhookCalls++;
            return Task.FromResult(new PaymentWebhookResponse(true, true));
        }
    }
}
