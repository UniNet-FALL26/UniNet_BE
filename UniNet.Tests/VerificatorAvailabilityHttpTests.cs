using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.Application;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task AvailabilityHttpEnforcesJwtRoleRequiredDatesAndCrudContract()
    {
        await using var f = await Fixture.CreateAsync();
        var staff = await ClearCalendarAsync(f);
        const string key = "availability-http-test-key-012345678901234567890123456789";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(f.Db);
        builder.Services.AddScoped<VerificatorAvailabilityService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(VerificatorAvailabilityController).Assembly);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidIssuer = "availability-tests", ValidAudience = "availability-tests",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateLifetime = true, RoleClaimType = ClaimTypes.Role
            };
        });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseExceptionHandler(errors => errors.Run(async context =>
        {
            var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            context.Response.StatusCode = error is ProjectVerificationException e ? e.Status : 500;
            await context.Response.WriteAsJsonAsync(new { code = (error as ProjectVerificationException)?.Code });
        }));
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new(app.Urls.Single()) };
        const string route = "/api/verificators/me/availability";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        void Authenticate(Guid accountId, string role)
        {
            var token = new JwtSecurityToken("availability-tests", "availability-tests",
                [new(JwtRegisteredClaimNames.Sub, accountId.ToString()), new(ClaimTypes.Role, role)],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
            client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        Authenticate(f.Owner.Id, "Student");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Authenticate(staff.Id, "Verificator");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(route)).StatusCode);
        var slot = Fixture.Slot();
        var response = await client.PostAsJsonAsync(route, new SaveVerificatorAvailabilityRequest(slot.StartAt, slot.EndAt));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<VerificatorAvailabilityResponse>())!;
        var query = $"?from={Uri.EscapeDataString(slot.StartAt.ToString("O"))}&to={Uri.EscapeDataString(slot.EndAt.ToString("O"))}";
        Assert.Equal(created.Id, Assert.Single((await client.GetFromJsonAsync<List<VerificatorAvailabilityResponse>>(route + query))!).Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(route + "/" + created.Id,
            new SaveVerificatorAvailabilityRequest(slot.StartAt, slot.EndAt.AddHours(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(route + "/" + created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(route + "/" + created.Id)).StatusCode);
        await app.StopAsync();
    }
}
