using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("UniNet")
    ?? throw new InvalidOperationException("ConnectionStrings:UniNet is required.");
var key = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
builder.Services.AddDbContext<UniNetDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectService, ProjectService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectJoinRequestService, ProjectJoinRequestService>();
builder.Services.AddScoped<ProjectModerationService>();
builder.Services.AddScoped<ProfileDevelopmentSeed>();
builder.Services.AddHttpClient<UniNet.Application.Interfaces.IAIModerationService, UniNet.Application.Services.GeminiModerationService>();
builder.Services.AddHostedService<UniNet.API.ExpiredTokenCleanup>();
builder.Services.AddControllers();
builder.Services.AddSingleton<UniNet.API.Services.ProfileImageStorage>();
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("MobileWeb", policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter access token:"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"],
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
});
builder.Services.AddAuthorization();
var app = builder.Build();
var seedFlag = Array.FindIndex(args, a => a == "--seed-profile-email" || a == "--inspect-profile-email");
if (seedFlag >= 0)
{
    if (seedFlag + 1 >= args.Length) throw new InvalidOperationException("A target email is required.");
    using var scope = app.Services.CreateScope();
    var seed = scope.ServiceProvider.GetRequiredService<ProfileDevelopmentSeed>();
    var result = args[seedFlag] == "--seed-profile-email" ? await seed.Run(args[seedFlag + 1], default) : await seed.Inspect(args[seedFlag + 1], default);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return;
}
app.UseSwagger();
app.UseSwaggerUI();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    if (error is AuthException authError)
    {
        context.Response.StatusCode = authError.Status;
        await context.Response.WriteAsJsonAsync(new { code = authError.Code, message = authError.Message });
        return;
    }
    if (error is ProjectJoinRequestException joinRequestError)
    {
        context.Response.StatusCode = joinRequestError.Status;
        await context.Response.WriteAsJsonAsync(new { code = joinRequestError.Code, message = joinRequestError.Message });
        return;
    }
    if (error is ProjectException projectError)
    {
        context.Response.StatusCode = projectError.Status;
        await context.Response.WriteAsJsonAsync(new { code = projectError.Code, message = projectError.Message });
        return;
    }
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "Có lỗi xảy ra. Vui lòng thử lại." });
}));
app.UseHttpsRedirection();
app.UseCors("MobileWeb");
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/swagger/index.html"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.Run();

public partial class Program { }
