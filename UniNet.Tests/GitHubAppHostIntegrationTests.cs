using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppHostIntegrationTests
{
    [Fact]
    public async Task EnabledMisconfiguredRealHostExitsWithoutPrintingSecret()
    {
        var api = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../UniNet.API/bin/Debug/net8.0/UniNet.API.dll"));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(api); start.Environment.Clear();
        foreach (var key in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "DOTNET_ROOT" })
            if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "E2E"; start.Environment["UNINET_SKIP_LOCAL_ENV_FILE"] = "true";
        start.Environment["GitHubApp__Enabled"] = "true"; start.Environment["GitHubApp__ValidateDeployment"] = "false";
        start.Environment["GitHubApp__ClientSecret"] = "fixture-sensitive-value";
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
            Assert.DoesNotContain("fixture-sensitive-value", await stdout); Assert.DoesNotContain("fixture-sensitive-value", await stderr);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task RealHostBlocksDisabledAppRoutesAndKeepsOAuthAuthenticationActive()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var api = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../UniNet.API/bin/Debug/net8.0/UniNet.API.dll"));
        Assert.True(File.Exists(api));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(api); start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment.Clear();
        foreach (var key in new[] { "PATH", "SystemRoot", "TEMP", "TMP", "DOTNET_ROOT" })
            if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "E2E";
        start.Environment["UNINET_SKIP_LOCAL_ENV_FILE"] = "true";
        start.Environment["GitHubE2E__DisableUnrelatedWorkers"] = "true";
        start.Environment["GitHubApp__Enabled"] = "false";
        start.Environment["GitHubApp__ValidateDeployment"] = "false";
        start.Environment["GitHubSync__Enabled"] = "false";
        start.Environment["ConnectionStrings__UniNet"] = "Host=127.0.0.1;Port=1;Database=uninet_test_unavailable;Username=fixture;Timeout=1";
        start.Environment["Jwt__Key"] = "fixture-signing-key-at-least-32-bytes-long";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(2) };
            var deadline = DateTime.UtcNow.AddSeconds(20); var ready = false;
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                try { ready = (await client.GetAsync("/health")).IsSuccessStatusCode; if (ready) break; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(200);
            }
            Assert.True(ready, "Isolated test API host did not start.");
            var project = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync($"/api/projects/{project}/git/app/authorization/start", null)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync($"/api/projects/{project}/git/app/repositories/link", null)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/webhooks/github/app", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/github/oauth/start", null)).StatusCode);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await Task.WhenAll(stdout, stderr);
        }
    }
}
