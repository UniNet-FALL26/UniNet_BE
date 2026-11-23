using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
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
UniNet.API.Services.GitHubDeploymentReadiness.Validate(builder.Configuration);
var phase3E2EEnvironment = builder.Environment.IsEnvironment("E2E");
builder.Services.Configure<GitHubIntegrationOptions>(
    builder.Configuration.GetSection(GitHubIntegrationOptions.SectionName));
builder.Services.Configure<GitSyncOptions>(
    builder.Configuration.GetSection(GitSyncOptions.SectionName));
builder.Services.Configure<GitHubWebhookOptions>(
    builder.Configuration.GetSection(GitHubWebhookOptions.SectionName));
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("UniNet.GitHubIntegration");
var gitHubKeyRingPath = builder.Configuration[$"{GitHubIntegrationOptions.SectionName}:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(gitHubKeyRingPath))
{
    var resolvedKeyRingPath = Path.IsPathRooted(gitHubKeyRingPath)
        ? gitHubKeyRingPath
        : Path.Combine(builder.Environment.ContentRootPath, gitHubKeyRingPath);
    builder.Configuration[$"{GitHubIntegrationOptions.SectionName}:KeyRingPath"] = resolvedKeyRingPath;
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(resolvedKeyRingPath));
}
if (!string.IsNullOrWhiteSpace(builder.Configuration["GitHub:KeyRingCertificatePath"]))
{
    var certificate = UniNet.API.Services.GitHubDeploymentReadiness.LoadCertificate(builder.Configuration);
    dataProtection.ProtectKeysWithCertificate(certificate);
    var previous = builder.Configuration.GetSection("GitHub:KeyRingPreviousCertificates").GetChildren()
        .Select(item => new System.Security.Cryptography.X509Certificates.X509Certificate2(
            item["Path"] ?? throw new InvalidOperationException("Previous key-ring certificate path is required."), item["Password"],
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet)).ToArray();
    if (previous.Length > 0) dataProtection.UnprotectKeysWithAnyCertificate(previous);
}
builder.Services.AddSingleton<UniNet.API.Services.GitHubWorkerProgress>();
builder.Services.AddSingleton<UniNet.API.Services.GitHubHealthTransitions>();
builder.Services.AddSingleton<IGitHubWorkerProgress>(services => services.GetRequiredService<UniNet.API.Services.GitHubWorkerProgress>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks().AddCheck<UniNet.API.Services.GitHubDeploymentHealthCheck>("github-deployment")
    .AddCheck<UniNet.API.Services.GitHubWorkerInstanceHealthCheck>("github-instance")
    .AddCheck<UniNet.API.Services.GitHubSharedQueueHealthCheck>("github-queue");
// Browser SignalR bearer tokens use a query parameter; the default Hosting request logs include query strings.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", Microsoft.Extensions.Logging.LogLevel.Warning);
_ = UniNet.API.Services.UploadFileReader.GetMaxVideoBytes(builder.Configuration);
var connection = builder.Configuration.GetConnectionString("UniNet")
    ?? throw new InvalidOperationException("ConnectionStrings:UniNet is required.");
var key = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
builder.Services.AddDbContext<UniNetDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MessagingService>();
builder.Services.AddHttpClient("ChatPush", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<IChatPushSender, UniNet.API.Services.FcmChatPushSender>();
builder.Services.AddHostedService<UniNet.API.Services.ChatDeliveryWorker>();
builder.Services.AddHttpClient<IChatAttachmentStorage, UniNet.API.Services.ChatCloudStorage>(client => client.Timeout = TimeSpan.FromMinutes(2))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<AssessorOnboardingService>();
builder.Services.AddScoped<AssessorContractService>();
builder.Services.AddScoped<AssessorActivationService>();
builder.Services.AddScoped<AssessorOnboardingEmailQueue>();
builder.Services.AddScoped<AssessorContractTasks>();
builder.Services.AddSingleton<AssessorContractSecrets>();
builder.Services.AddScoped<IAssessorContractFiles, AssessorContractFiles>();
var assessorMailProvider = builder.Configuration["AssessorOnboarding:MailProvider"] ?? "Smtp";
if (assessorMailProvider.Equals("Brevo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IAssessorOnboardingMailSender, UniNet.API.Services.AssessorBrevoSender>(
        client => client.Timeout = TimeSpan.FromSeconds(30))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
}
else if (assessorMailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IAssessorOnboardingMailSender, UniNet.API.Services.AssessorSmtpSender>();
}
else
{
    throw new InvalidOperationException("AssessorOnboarding:MailProvider must be Smtp or Brevo.");
}
builder.Services.AddOptions<AssessorContractOptions>()
    .Bind(builder.Configuration.GetSection("AssessorOnboarding"))
    .Validate(o => !o.Enabled || (
        Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == "https" || (builder.Environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == "http")) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/" &&
        Path.IsPathFullyQualified(o.StoragePath) && File.Exists(o.TemplatePath) && File.Exists(o.FontPath) &&
        !string.IsNullOrWhiteSpace(o.TemplateVersion) && !string.IsNullOrWhiteSpace(o.SignatureStatement) &&
        (builder.Environment.IsDevelopment() ||
         !string.IsNullOrWhiteSpace(builder.Configuration["AssessorOnboarding:KeyRingPath"]))),
        "Enabled assessor contracts require a public origin, private storage, template, Unicode font, template version, signature statement and a persistent key ring outside Development.")
    .Validate(o => !o.Enabled || !Path.GetFullPath(o.StoragePath).StartsWith(
        Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "wwwroot")), StringComparison.OrdinalIgnoreCase),
        "Assessor contracts must be stored outside wwwroot.")
    .ValidateOnStart();
builder.Services.AddOptions<UniNet.API.Services.AssessorSmtpOptions>()
    .Bind(builder.Configuration.GetSection("AssessorOnboarding:Smtp"))
    .Validate(o => !builder.Configuration.GetValue<bool>("AssessorOnboarding:Enabled") ||
        !assessorMailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase) ||
        (System.Net.Mail.MailAddress.TryCreate(o.From, out _) &&
        ((!string.IsNullOrWhiteSpace(o.Host) && o.Port > 0 && o.Port <= 65535) ||
         (builder.Environment.IsDevelopment() && Path.IsPathFullyQualified(o.PickupDirectory ?? "")))),
        "Configure SMTP or a Development-only pickup directory.")
    .ValidateOnStart();
builder.Services.AddOptions<UniNet.API.Services.AssessorBrevoOptions>()
    .Bind(builder.Configuration.GetSection("AssessorOnboarding:Brevo"))
    .Validate(o => !builder.Configuration.GetValue<bool>("AssessorOnboarding:Enabled") ||
        !assessorMailProvider.Equals("Brevo", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(o.ApiKey) && System.Net.Mail.MailAddress.TryCreate(o.From, out _)),
        "Configure the Brevo API key and sender email.")
    .ValidateOnStart();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("Messaging", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("AssessorContracts", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var onboardingProtection = builder.Services.AddDataProtection();
var onboardingKeyRing = builder.Configuration["AssessorOnboarding:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(onboardingKeyRing))
    onboardingProtection.PersistKeysToFileSystem(new DirectoryInfo(onboardingKeyRing)).SetApplicationName("UniNet");
builder.Services.AddScoped<AdminUsersService>();
builder.Services.AddScoped<AdminSkillsService>();
builder.Services.AddScoped<AdminPermissionService>();
builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<AdminVerificationQueries>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<VerificationNotifications>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<FriendshipService>();
builder.Services.AddScoped<NotificationTargets>();
builder.Services.AddScoped<NotificationBroadcastService>();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, UniNet.API.Hubs.AccountUserIdProvider>();
builder.Services.AddScoped<VerificationDeadlineTasks>();
builder.Services.AddScoped<ProjectRecruitmentTasks>();
builder.Services.AddScoped<VerificationReservationTasks>();
builder.Services.AddScoped<VerificationRefundTasks>();
builder.Services.AddScoped<ExternalProjectDraftService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IRefundProvider, ManualRefundProvider>();
builder.Services.AddScoped<IAdminProjectVerificationService, AdminProjectVerificationService>();
builder.Services.AddScoped<VerificatorAvailabilityService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<PartnerVerificationService>();
builder.Services.AddScoped<StudentVerificationService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IPackageService, PackageService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectService, ProjectService>();
builder.Services.AddScoped<SpecializationService>();
builder.Services.AddScoped<ProjectHubService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IContributionService, ContributionService>();
builder.Services.AddGitExperimentalContributionServices();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectJoinRequestService, ProjectJoinRequestService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectInvitationService, ProjectInvitationService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectMemberService, ProjectMemberService>();
builder.Services.AddScoped<IGitHubOAuthService, GitHubOAuthService>();
builder.Services.Configure<GitHubAppOptions>(builder.Configuration.GetSection(GitHubAppOptions.SectionName));
builder.Services.AddHttpClient("GitHubAppInstallationTokens")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton(sp => new GitHubAppInstallationTokenProvider(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("GitHubAppInstallationTokens"),
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubAppOptions>>(), TimeProvider.System));
builder.Services.AddSingleton<IGitHubAppInstallationTokenProvider, UniNet.API.Services.GitHubAppEnabledTokenProvider>();
builder.Services.AddScoped<GitHubAppRepositoryActivationService>();
builder.Services.AddHttpClient<GitHubAppInstallationReconciliationService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IGitHubAppUserClient, UniNet.API.Services.GitHubAppUserClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<GitHubAppUserGrantVerifier>();
builder.Services.AddScoped<IGitHubAppAuthorizationService, GitHubAppAuthorizationService>();
builder.Services.AddScoped<IGitHubAppGrantService, GitHubAppGrantService>();
builder.Services.AddScoped<IGitHubAppRepositoryLinkService, GitHubAppRepositoryLinkService>();
builder.Services.AddScoped<IProjectGitManagerService, ProjectGitManagerService>();
builder.Services.AddScoped<IProjectGitRepositoryService, ProjectGitRepositoryService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IProjectGitIdentityService,
    UniNet.Application.Services.ProjectGitIdentityService>();
builder.Services.AddScoped<IGitSyncJobService, GitSyncJobService>();
builder.Services.AddScoped<IGitSyncJobProcessor, GitHubSyncJobProcessor>();
builder.Services.AddScoped<IProjectGitActivityService, ProjectGitActivityService>();
builder.Services.AddScoped<IGitHubWebhookService, GitHubWebhookService>();
builder.Services.AddScoped<IGitHubAppWebhookService, GitHubAppWebhookService>();
builder.Services.AddScoped<GitHubAppWebhookConsumer>();
builder.Services.AddScoped<GitHubAppWebhookRecoveryService>();
builder.Services.AddScoped<GitHubAppInitialSyncRecoveryService>();
builder.Services.AddScoped<GitHubCredentialService>();
builder.Services.AddScoped<GitHubOAuthFlowCleanupService>();
builder.Services.AddSingleton<IGitHubTokenProtector, UniNet.API.Services.GitHubTokenProtector>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IPartnerService, UniNet.Application.Services.PartnerService>();
builder.Services.AddScoped<UniNet.Application.Interfaces.IStudentOpportunityService, UniNet.Application.Services.StudentOpportunityService>();
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
builder.Services.AddControllers();
builder.Services.AddHttpClient<IGitHubProviderClient, UniNet.API.Services.GitHubProviderClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<UniNet.API.Services.AccountSessionConnections>();
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
UniNet.API.Services.RuntimeWorkerRegistration.AddRuntimeWorkers(builder.Services, builder.Configuration, phase3E2EEnvironment);
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
var figmaOrigins = phase3E2EEnvironment
    ? Array.Empty<string>()
    : new[]
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
            .AllowCredentials()
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
            if ((context.Request.Path.StartsWithSegments("/hubs/workspace") || context.Request.Path.StartsWithSegments("/hubs/notifications") || context.Request.Path.StartsWithSegments("/hubs/messaging")) &&
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
    if (error is UniNet.Application.Common.AdvertisementException advertisementError)
    {
        context.Response.StatusCode = advertisementError.StatusCode;
        await context.Response.WriteAsJsonAsync(new { code = advertisementError.Code, message = advertisementError.Message });
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
    if (error is UniNet.Application.Common.GitHubIntegrationException gitHubError)
    {
        context.Response.StatusCode = gitHubError.Status;
        await context.Response.WriteAsJsonAsync(new { code = gitHubError.Code, message = gitHubError.Message });
        return;
    }
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new { code = "INTERNAL_ERROR", message = "Có lỗi xảy ra. Vui lòng thử lại." });
}));
if (!app.Environment.IsDevelopment() && !phase3E2EEnvironment)
{
    app.UseHttpsRedirection();
}
app.UseCors("Frontend");

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var isAppRoute = path.StartsWith("/api/github/app/", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/api/webhooks/github/app", StringComparison.OrdinalIgnoreCase) ||
        (path.StartsWith("/api/projects/", StringComparison.OrdinalIgnoreCase) && path.Contains("/git/app/", StringComparison.OrdinalIgnoreCase));
    if (isAppRoute && !builder.Configuration.GetValue<bool>("GitHubApp:Enabled"))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }
    await next();
});
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/swagger/index.html"));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHealthChecks("/health/github/readiness", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "github-deployment"
});
app.MapHealthChecks("/health/github/instance", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "github-instance"
});
app.MapHealthChecks("/health/github/queue", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Name == "github-queue"
});
app.UseStaticFiles();
app.MapControllers();
app.MapHub<UniNet.API.Hubs.WorkspaceChatHub>(
    "/hubs/workspace",
    options => options.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization();
app.MapHub<UniNet.API.Hubs.NotificationsHub>("/hubs/notifications", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
app.MapHub<UniNet.API.Hubs.MessagingHub>("/hubs/messaging", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
app.Run();

public partial class Program { }
