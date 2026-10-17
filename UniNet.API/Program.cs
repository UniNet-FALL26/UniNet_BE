using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
UniNet.API.Services.LocalEnvConfiguration.Apply(builder.Configuration, builder.Environment.ContentRootPath, args);
_ = UniNet.API.Services.UploadFileReader.GetMaxVideoBytes(builder.Configuration);
var connection = builder.Configuration.GetConnectionString("UniNet")
    ?? throw new InvalidOperationException("ConnectionStrings:UniNet is required.");
var key = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
builder.Services.AddDbContext<UniNetDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<VerificationNotifications>();
builder.Services.AddScoped<VerificationDeadlineTasks>();
builder.Services.AddScoped<VerificationRefundTasks>();
builder.Services.AddScoped<VerificationAiTasks>();
builder.Services.AddSingleton<IRefundProvider, ManualRefundProvider>();
builder.Services.AddHttpClient<IVerificationAiReviewer, GeminiVerificationAiReviewer>(client => client.Timeout = TimeSpan.FromSeconds(35))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<UniNet.API.Services.VerificationBackgroundWorker>();
builder.Services.AddScoped<IAdminProjectVerificationService, AdminProjectVerificationService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<PartnerVerificationService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IPackageService, PackageService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectService, ProjectService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectJoinRequestService, ProjectJoinRequestService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectInvitationService, ProjectInvitationService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectMemberService, ProjectMemberService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IPartnerService, UniNet.Application.Services.PartnerService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectTaskService, UniNet.Application.Services.ProjectTaskService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectVerificationService, ProjectVerificationService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IAssignmentService, AssignmentService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IReportService, ReportService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.ITransactionService, TransactionService>();
builder.Services.AddHttpClient<UniNet.Application.Interfaces.IPaymentProvider, PayOSPaymentProvider>(client =>
    client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<ProjectModerationService>();
builder.Services.AddScoped<ProfileDevelopmentSeed>();
builder.Services.AddScoped<ICommunityService,CommunityService>();
builder.Services.AddScoped<IAdvertisementService, AdvertisementService>();
builder.Services.AddHttpClient<UniNet.Application.Interfaces.IAIModerationService, UniNet.Application.Services.GeminiModerationService>();
builder.Services.AddHostedService<UniNet.API.ExpiredTokenCleanup>();
builder.Services.AddControllers();
builder.Services.AddSingleton<UniNet.API.Services.ProfileImageStorage>();
builder.Services.AddHttpClient<UniNet.API.Services.GeminiPortraitProcessor>(client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<UniNet.API.Services.OnnxPortraitProcessor>();
builder.Services.AddTransient<UniNet.API.Services.IPortraitProcessor>(services => new UniNet.API.Services.FallbackPortraitProcessor(
    services.GetRequiredService<UniNet.API.Services.GeminiPortraitProcessor>(),
    services.GetRequiredService<UniNet.API.Services.OnnxPortraitProcessor>(),
    builder.Configuration.GetValue("Portrait:LocalFallbackEnabled", true),
    services.GetRequiredService<ILogger<UniNet.API.Services.FallbackPortraitProcessor>>()));
builder.Services.AddHttpClient<UniNet.Application.Interfaces.ICloudImageStorage, UniNet.API.Services.CloudinaryImageStorage>(client => client.Timeout = TimeSpan.FromMinutes(2))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<UniNet.API.Services.IPortraitCloudStorage, UniNet.API.Services.CloudinaryPortraitStorage>();
builder.Services.AddSingleton<UniNet.API.Services.PortraitSessions>();
builder.Services.AddHostedService<UniNet.API.Services.PortraitCleanup>();
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("MobileWeb", policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddCors(options =>
{
    options.AddPolicy("UniNetDashboard", policy =>
    {
        policy
            .WithOrigins(
                "https://uninet-dashboard-nine.vercel.app"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
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
    if (error is TransactionException transactionError)
    {
        context.Response.StatusCode = transactionError.Status;
        await context.Response.WriteAsJsonAsync(new { code = transactionError.Code, message = transactionError.Message });
        return;
    }
    if (error is UniNet.Application.Common.PackageException packageError)
    {
        context.Response.StatusCode = packageError.Status;
        await context.Response.WriteAsJsonAsync(new { code = packageError.Code, message = packageError.Message });
        return;
    }
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
    if (error is AssignmentException assignmentError)
    {
        context.Response.StatusCode = assignmentError.Status;
        await context.Response.WriteAsJsonAsync(new { code = assignmentError.Code, message = assignmentError.Message });
        return;
    }
    if (error is ReportException reportError)
    {
        context.Response.StatusCode = reportError.Status;
        await context.Response.WriteAsJsonAsync(new { code = reportError.Code, message = reportError.Message });
        return;
    }
    if (error is ProjectVerificationException verificationError)
    {
        context.Response.StatusCode = verificationError.Status;
        await context.Response.WriteAsJsonAsync(new { code = verificationError.Code, message = verificationError.Message });
        return;
    }
    if (error is ProjectInvitationException invitationError)
    {
        context.Response.StatusCode = invitationError.Status;
        await context.Response.WriteAsJsonAsync(new { code = invitationError.Code, message = invitationError.Message });
        return;
    }
    if (error is ProjectMemberException memberError)
    {
        context.Response.StatusCode = memberError.Status;
        await context.Response.WriteAsJsonAsync(new { code = memberError.Code, message = memberError.Message });
        return;
    }
    if (error is UniNet.Application.Common.PartnerException partnerError)
    {
        context.Response.StatusCode = partnerError.Status;
        await context.Response.WriteAsJsonAsync(new { code = partnerError.Code, message = partnerError.Message });
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
app.UseCors("UniNetDashboard");
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/swagger/index.html"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.Run();

public partial class Program { }
