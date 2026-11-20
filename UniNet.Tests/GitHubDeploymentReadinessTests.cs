using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using UniNet.Infrastructure.Data;
using UniNet.API.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubDeploymentReadinessTests
{
    private sealed class TransitionLogger : ILogger<GitHubHealthTransitions>
    {
        public int Count;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Count++;
    }

    [Fact]
    public void HealthLoggingOnlyOccursOnTransitions()
    {
        long measurements = 0; var keys = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == "UniNet.GitHubHealth") l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => { measurements += value; foreach (var tag in tags) keys.Add(tag.Key); });
        listener.Start();
        var logger = new TransitionLogger(); var transitions = new GitHubHealthTransitions(logger);
        transitions.Record("queue", HealthStatus.Healthy); transitions.Record("queue", HealthStatus.Healthy);
        transitions.Record("queue", HealthStatus.Unhealthy); transitions.Record("queue", HealthStatus.Unhealthy);
        transitions.Record("queue", HealthStatus.Healthy); Assert.Equal(3, logger.Count);
        Assert.Equal(3, measurements); Assert.Equal(new[] { "component", "status" }, keys.Order());
    }
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }

    [Fact]
    public void LongCyclesStayHealthyOnlyWhenBusinessMilestonesAdvance()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow); var progress = new GitHubWorkerProgress(clock);
        foreach (var loop in new[] { "worker", "consumer", "reconciliation" }) progress.Completed(loop);
        for (var i = 0; i < 4; i++)
        {
            clock.Now = clock.Now.AddMinutes(10);
            foreach (var loop in new[] { "worker", "consumer", "reconciliation" }) progress.Completed(loop);
            Assert.True(progress.IsHealthy(clock.Now, TimeSpan.FromMinutes(15)));
        }
        clock.Now = clock.Now.AddMinutes(16);
        progress.Completed("worker");
        Assert.False(progress.IsHealthy(clock.Now, TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task SharedQueueReportsDisabledAndDatabaseUnavailableSeparately()
    {
        var services = new ServiceCollection();
        services.AddDbContext<UniNetDbContext>(x => x.UseNpgsql("Host=127.0.0.1;Port=1;Database=unavailable;Username=fixture;Timeout=1"));
        await using var container = services.BuildServiceProvider();
        var off = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GitHubApp:Enabled"] = "false" }).Build();
        var on = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GitHubApp:Enabled"] = "true" }).Build();
        var scopes = container.GetRequiredService<IServiceScopeFactory>();
        Assert.Equal(HealthStatus.Degraded, (await new GitHubSharedQueueHealthCheck(scopes, off, TimeProvider.System).CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.Equal(HealthStatus.Unhealthy, (await new GitHubSharedQueueHealthCheck(scopes, on, TimeProvider.System).CheckHealthAsync(new HealthCheckContext())).Status);
    }
    [Fact]
    public async Task DisabledServiceBoundariesRejectWithoutAccessingDependencies()
    {
        var disabled = Options.Create(new GitHubAppOptions { Enabled = false });
        var auth = new GitHubAppAuthorizationService(null!, null!, null!, disabled);
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => auth.CallbackAsync("state", "code", null, default));
        Assert.Equal("GITHUB_APP_DISABLED", error.Code);
        var consumer = new GitHubAppWebhookConsumer(null!, null!, null!, null!, disabled, Options.Create(new GitSyncOptions()));
        error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => consumer.ClaimNextAsync(default));
        Assert.Equal("GITHUB_APP_DISABLED", error.Code);
        error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => consumer.ProcessClaimAsync(new GitHubAppWebhookDelivery(), default));
        Assert.Equal("GITHUB_APP_DISABLED", error.Code);
        var processor = new GitHubSyncJobProcessor(null!, null!, null!, appOptions: disabled);
        error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => processor.ProcessAsync(new GitSyncJob { CredentialSource = GitRepositoryCredentialSource.GitHubApp }, default));
        Assert.Equal("GITHUB_APP_DISABLED", error.Code);
        var activation = new GitHubAppRepositoryActivationService(null!, null!, null!, Options.Create(new GitSyncOptions { Enabled = true }));
        error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => activation.ActivateAsync(Guid.NewGuid(), default));
        Assert.Equal("GITHUB_APP_DISABLED", error.Code);
    }
    [Fact]
    public void InstanceHeartbeatAcceptsIdleSuccessfulCyclesAndDetectsStaleness()
    {
        var progress = new GitHubWorkerProgress();
        progress.Completed("worker"); progress.Completed("consumer"); progress.Completed("reconciliation");
        Assert.True(progress.IsHealthy(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15)));
        Assert.False(progress.IsHealthy(DateTimeOffset.UtcNow.AddMinutes(16), TimeSpan.FromMinutes(15)));
    }
    [Fact]
    public async Task DisabledAppTokenResolutionFailsBeforeAnyProviderCall()
    {
        var handler = new CountingHandler();
        using var inner = new GitHubAppInstallationTokenProvider(new HttpClient(handler), Options.Create(new GitHubAppOptions()), TimeProvider.System);
        var guarded = new GitHubAppEnabledTokenProvider(inner, Options.Create(new GitHubAppOptions { Enabled = false }));
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => guarded.GetAsync(1, 1, default));
        Assert.Equal("GITHUB_APP_DISABLED", error.Code); Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void EnabledAppCannotBypassValidation()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["GitHubApp:Enabled"] = "true", ["GitHubApp:ValidateDeployment"] = "false" }).Build();
        Assert.Throws<InvalidOperationException>(() => GitHubDeploymentReadiness.Validate(config));
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected HTTP request."); }
    }

    [Theory]
    [InlineData("GitHubApp:AppId", null)]
    [InlineData("GitHubApp:PrivateKeyPem", "invalid")]
    [InlineData("GitHubApp:WebhookSecret", null)]
    [InlineData("GitHub:KeyRingPath", null)]
    [InlineData("GitHubApp:CallbackUrl", "http://example.test/callback")]
    public void ValidationAcceptsCompleteConfigurationAndRejectsInvalidValues(string field, string? invalid)
    {
        var path = Path.Combine(Path.GetTempPath(), "uninet-dp-cert-" + Guid.NewGuid().ToString("N") + ".pfx");
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=uninet-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        try
        {
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx));
            var values = new Dictionary<string, string?> {
                ["GitHubApp:ValidateDeployment"] = "true", ["GitHubApp:AppId"] = "123", ["GitHubApp:ClientId"] = "app-client",
                ["GitHubApp:ClientSecret"] = "fixture", ["GitHubApp:PrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem(),
                ["GitHubApp:WebhookSecret"] = "fixture", ["GitHubApp:CallbackUrl"] = "https://api.example.test/callback",
                ["GitHubApp:ReturnTargets:web"] = "https://web.example.test/complete", ["GitHub:ClientId"] = "oauth-client",
                ["GitHub:ClientSecret"] = "fixture", ["GitHub:CallbackUrl"] = "https://api.example.test/oauth",
                ["GitHub:KeyRingPath"] = Path.GetTempPath(), ["GitHub:KeyRingCertificatePath"] = path };
            GitHubDeploymentReadiness.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
            values[field] = invalid;
            Assert.Throws<InvalidOperationException>(() => GitHubDeploymentReadiness.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ValidationRejectsMissingConfigurationWithoutLeakingValues()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["GitHubApp:ValidateDeployment"] = "true", ["GitHubApp:ClientSecret"] = "fixture-secret" }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => GitHubDeploymentReadiness.Validate(config));
        Assert.DoesNotContain("fixture-secret", error.Message);
        GitHubDeploymentReadiness.Validate(new ConfigurationBuilder().Build());
    }

    [Fact]
    public void EncryptedKeyRingSupportsRestartInstancesAndRotation()
    {
        var path = Path.Combine(Path.GetTempPath(), "uninet-dp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=uninet-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(3));
        ServiceProvider MakeProvider()
        {
            var services = new ServiceCollection(); services.AddLogging();
            services.AddDataProtection().SetApplicationName("UniNet.GitHubIntegration")
                .PersistKeysToFileSystem(new DirectoryInfo(path)).ProtectKeysWithCertificate(cert);
            return services.BuildServiceProvider();
        }
        try
        {
            string oldCipher;
            using (var first = MakeProvider())
                oldCipher = new GitHubTokenProtector(first.GetRequiredService<IDataProtectionProvider>()).Protect("fixture-oauth-and-webhook");
            using (var second = MakeProvider())
            {
                var protector = new GitHubTokenProtector(second.GetRequiredService<IDataProtectionProvider>());
                Assert.Equal("fixture-oauth-and-webhook", protector.Unprotect(oldCipher));
                second.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddDays(90));
            }
            using var third = MakeProvider();
            Assert.Equal("fixture-oauth-and-webhook", new GitHubTokenProtector(third.GetRequiredService<IDataProtectionProvider>()).Unprotect(oldCipher));
            Assert.True(Directory.GetFiles(path, "*.xml").Length >= 2);
            Assert.All(Directory.GetFiles(path, "*.xml"), file => Assert.Contains("EncryptedData", File.ReadAllText(file)));
        }
        finally { Directory.Delete(path, recursive: true); }
    }

    [Fact]
    public void CertificateRolloverRetainsOldKeysWhenPreviousCertificateIsAvailable()
    {
        var path = Path.Combine(Path.GetTempPath(), "uninet-dp-rollover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        using var oldRsa = RSA.Create(2048); using var newRsa = RSA.Create(2048);
        var oldRequest = new CertificateRequest("CN=old", oldRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var newRequest = new CertificateRequest("CN=new", newRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var oldCert = oldRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        using var newCert = newRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(2));
        try
        {
            ServiceProvider Make(X509Certificate2 current, X509Certificate2? previous = null)
            {
                var services = new ServiceCollection(); services.AddLogging();
                var builder = services.AddDataProtection().SetApplicationName("UniNet.GitHubIntegration")
                    .PersistKeysToFileSystem(new DirectoryInfo(path)).ProtectKeysWithCertificate(current);
                if (previous is not null) builder.UnprotectKeysWithAnyCertificate(previous);
                return services.BuildServiceProvider();
            }
            string oldCipher;
            using (var oldInstance = Make(oldCert)) oldCipher = new GitHubTokenProtector(oldInstance.GetRequiredService<IDataProtectionProvider>()).Protect("fixture");
            using (var updated = Make(newCert, oldCert))
            {
                Assert.Equal("fixture", new GitHubTokenProtector(updated.GetRequiredService<IDataProtectionProvider>()).Unprotect(oldCipher));
                updated.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddDays(90));
            }
            using var restarted = Make(newCert, oldCert);
            Assert.Equal("fixture", new GitHubTokenProtector(restarted.GetRequiredService<IDataProtectionProvider>()).Unprotect(oldCipher));
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void DifferentApplicationDiscriminatorCannotDecrypt()
    {
        var path = Path.Combine(Path.GetTempPath(), "uninet-dp-isolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var ring = new DirectoryInfo(path);
            var a = new GitHubTokenProtector(DataProtectionProvider.Create(ring, x => x.SetApplicationName("UniNet.GitHubIntegration")));
            var b = new GitHubTokenProtector(DataProtectionProvider.Create(ring, x => x.SetApplicationName("Different.Application")));
            var encrypted = a.Protect("fixture");
            Assert.Throws<GitHubIntegrationException>(() => b.Unprotect(encrypted));
        }
        finally { Directory.Delete(path, true); }
    }
}
