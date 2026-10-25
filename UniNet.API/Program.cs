using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);
UniNet.API.Services.LocalEnvConfiguration.Apply(builder.Configuration, builder.Environment.ContentRootPath, args);
// Browser SignalR bearer tokens use a query parameter; the default Hosting request logs include query strings.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", Microsoft.Extensions.Logging.LogLevel.Warning);
_ = UniNet.API.Services.UploadFileReader.GetMaxVideoBytes(builder.Configuration);
var connection = builder.Configuration.GetConnectionString("UniNet")
    ?? throw new InvalidOperationException("ConnectionStrings:UniNet is required.");
var key = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
builder.Services.AddDbContext<UniNetDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AdminUsersService>();
builder.Services.AddScoped<AdminPermissionService>();
builder.Services.AddScoped<AdminVerificationQueries>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<VerificationNotifications>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<NotificationTargets>();
builder.Services.AddScoped<NotificationBroadcastService>();
builder.Services.AddHostedService<UniNet.API.Services.NotificationBackgroundWorker>();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, UniNet.API.Hubs.AccountUserIdProvider>();
builder.Services.AddScoped<VerificationDeadlineTasks>();
builder.Services.AddScoped<ProjectRecruitmentTasks>();
builder.Services.AddScoped<VerificationReservationTasks>();
builder.Services.AddScoped<VerificationRefundTasks>();
builder.Services.AddScoped<EvidenceModerationTasks>();
builder.Services.AddScoped<EvidenceModerationService>();
builder.Services.AddScoped<EvidenceModerationChecks>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IRefundProvider, ManualRefundProvider>();
builder.Services.AddHttpClient<GeminiVerificationAiReviewer>(client => client.Timeout = TimeSpan.FromSeconds(35))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<IEvidenceContentReviewer>(s => s.GetRequiredService<GeminiVerificationAiReviewer>());
builder.Services.AddHttpClient<IEvidenceUrlChecker, GoogleSafeBrowsingUrlChecker>(client => client.Timeout = TimeSpan.FromSeconds(35))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<UniNet.API.Services.VerificationBackgroundWorker>();
builder.Services.AddScoped<IAdminProjectVerificationService, AdminProjectVerificationService>();
builder.Services.AddScoped<VerificatorAvailabilityService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<PartnerVerificationService>();
builder.Services.AddScoped<StudentVerificationService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IPackageService, PackageService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectService, ProjectService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IContributionService, ContributionService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectJoinRequestService, ProjectJoinRequestService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectInvitationService, ProjectInvitationService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectMemberService, ProjectMemberService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IPartnerService, UniNet.Application.Services.PartnerService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IStudentOpportunityService, UniNet.Application.Services.StudentOpportunityService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectTaskService, UniNet.Application.Services.ProjectTaskService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectVerificationService, ProjectVerificationService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IAssignmentService, AssignmentService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IMeetingService, MeetingService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IReportService, ReportService>();
builder.Services.AddScoped<IWorkspaceAccessService, WorkspaceAccessService>();
builder.Services.AddScoped<IWorkspaceChatService, WorkspaceChatService>();
builder.Services.AddSingleton<UniNet.API.Hubs.IWorkspaceConnectionTracker, UniNet.API.Hubs.WorkspaceConnectionTracker>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IWorkspaceMemberRevoker, UniNet.API.Services.WorkspaceMemberRevoker>();
builder.Services.AddScoped<ILiveKitTokenService, LiveKitTokenService>();
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
builder.Services.AddSingleton<UniNet.API.Services.AccountSessionConnections>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UniNet.API.Services.AccountSessionConnections>());
builder.Services.AddSignalR(options => options.AddFilter<UniNet.API.Services.AccountSessionConnections>());
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
var figmaOrigins = new[]
{
    "http://localhost:3000",
    "https://ta-01m551rnn4dyqsyrf74mnkz5vv-1380-2pwdyx71k1tz10bigm5fnvcgc.makeproxy-m.figma.site"
};

var allowedOrigins = origins
    .Concat(figmaOrigins)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

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
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<UniNetDbContext>();
            if (!await UniNet.API.Services.AccountTokenValidation.ValidateAsync(context.Principal, db, context.HttpContext.RequestAborted))
                context.Fail("Account session is no longer active.");
        },
        OnMessageReceived = context =>
        {
            if ((context.Request.Path.StartsWithSegments("/hubs/workspace") || context.Request.Path.StartsWithSegments("/hubs/notifications")) &&
                context.Request.Query.TryGetValue("access_token", out var token))
                context.Token = token;
            return Task.CompletedTask;
        }
    };
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
    if (error?.GetBaseException() is Npgsql.PostgresException
        { SqlState: Npgsql.PostgresErrorCodes.SerializationFailure or Npgsql.PostgresErrorCodes.DeadlockDetected })
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "CONCURRENT_MODIFICATION",
            message = "A concurrent database update prevented this operation. Retry the request."
        });
        return;
    }
    // Serializable callers may hold an older snapshot even after the notification event lock.
    // Retry the whole business request rather than continuing an aborted PostgreSQL transaction.
    if (error?.GetBaseException() is Npgsql.PostgresException {
        SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Notifications_RecipientUserId_EventKey" })
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new { code = "CONCURRENT_MODIFICATION", message = "Thông báo đã được xử lý đồng thời. Vui lòng thử lại thao tác." });
        return;
    }
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
    if (error is MeetingException meetingError)
    {
        context.Response.StatusCode = meetingError.Status;
        await context.Response.WriteAsJsonAsync(new { code = meetingError.Code, message = meetingError.Message });
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
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/swagger/index.html"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
app.MapHub<UniNet.API.Hubs.WorkspaceChatHub>(
    "/hubs/workspace",
    options => options.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization();
app.MapHub<UniNet.API.Hubs.NotificationsHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
app.Run();

public partial class Program { }
