using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

// Opt-in validation preserves existing OAuth-only and E2E startup behavior.
public static class GitHubDeploymentReadiness
{
    public static void Validate(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("GitHubApp:Enabled") && !configuration.GetValue<bool>("GitHubApp:ValidateDeployment")) return;
        var app = configuration.GetSection(GitHubAppOptions.SectionName).Get<GitHubAppOptions>() ?? new();
        if (app.AppId is null or <= 0 || string.IsNullOrWhiteSpace(app.ClientId) || app.ClientId.Any(char.IsWhiteSpace) ||
            string.IsNullOrWhiteSpace(app.ClientSecret) || string.IsNullOrWhiteSpace(app.WebhookSecret) ||
            string.IsNullOrWhiteSpace(app.PrivateKeyPem) || !Callback(app.CallbackUrl) ||
            app.ReturnTargets.Count == 0 || app.ReturnTargets.Values.Any(x => !Https(x)) ||
            !Callback(configuration["GitHub:CallbackUrl"]) || string.IsNullOrWhiteSpace(configuration["GitHub:ClientId"]) ||
            string.IsNullOrWhiteSpace(configuration["GitHub:ClientSecret"]) ||
            string.IsNullOrWhiteSpace(configuration["GitHub:KeyRingPath"]) ||
            !Path.IsPathFullyQualified(configuration["GitHub:KeyRingPath"]!) ||
            !Directory.Exists(configuration["GitHub:KeyRingPath"]) ||
            string.IsNullOrWhiteSpace(configuration["GitHub:KeyRingCertificatePath"]))
            throw new InvalidOperationException("GitHub App deployment configuration is incomplete or invalid.");
        try
        {
            using var rsa = RSA.Create(); rsa.ImportFromPem(app.PrivateKeyPem);
            if (rsa.KeySize < 2048 || rsa.ExportParameters(true).D is null) throw new CryptographicException();
            using var cert = LoadCertificate(configuration);
            using var certKey = cert.GetRSAPrivateKey();
            if (!cert.HasPrivateKey || certKey is null || cert.NotBefore.ToUniversalTime() > DateTime.UtcNow || cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new CryptographicException();
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("GitHub App deployment cryptographic configuration is invalid."); }
    }

    public static X509Certificate2 LoadCertificate(IConfiguration c) => new(
        c["GitHub:KeyRingCertificatePath"]!, c["GitHub:KeyRingCertificatePassword"],
        X509KeyStorageFlags.EphemeralKeySet);

    private static bool Https(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment);
    private static bool Callback(string? value) => Https(value) && string.IsNullOrEmpty(new Uri(value!).Query);
}

public sealed class GitHubDeploymentHealthCheck(IServiceScopeFactory scopes, IGitHubTokenProtector protector, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!configuration.GetValue<bool>("GitHubApp:Enabled") || !configuration.GetValue<bool>("GitHubSync:Enabled"))
                return HealthCheckResult.Unhealthy();
            GitHubDeploymentReadiness.Validate(configuration);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
            if (!await db.Database.CanConnectAsync(cancellationToken) || (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return HealthCheckResult.Unhealthy();
            var probe = Guid.NewGuid().ToString("N");
            return protector.Unprotect(protector.Protect(probe)) == probe ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception) { return HealthCheckResult.Unhealthy(); }
    }
}
