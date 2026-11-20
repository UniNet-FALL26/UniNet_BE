using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppInstallationTokenProviderTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(Clock clock) : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.Created;
        public bool RateLimited;
        public List<long> RequestedRepositories = [];
        public TaskCompletionSource? Started;
        public TaskCompletionSource? Resume;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            RequestedRepositories.Add(json.RootElement.GetProperty("repository_ids")[0].GetInt64());
            Started?.TrySetResult();
            if (Resume is not null) await Resume.Task.WaitAsync(cancellationToken);
            var response = new HttpResponseMessage(Status) { Content = new StringContent(
                JsonSerializer.Serialize(new { token = "fixture-token-" + Calls, expires_at = clock.Now.AddMinutes(10) }), Encoding.UTF8, "application/json") };
            if (RateLimited) response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            return response;
        }
    }

    [Fact]
    public async Task ForbiddenWithRateLimitHeaderIsNotClassifiedAsAccessLoss()
    {
        using var rsa = RSA.Create(2048);
        var clock = new Clock();
        using var provider = new GitHubAppInstallationTokenProvider(new HttpClient(new Handler(clock) { Status = HttpStatusCode.Forbidden, RateLimited = true }), Options.Create(
            new GitHubAppOptions { Enabled = true, ClientId = "fixture", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }), clock);
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => provider.GetAsync(42, 100, default));
        Assert.Equal("GITHUB_RATE_LIMITED", error.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "GITHUB_APP_AUTH_INVALID", 503)]
    [InlineData(HttpStatusCode.Forbidden, "GITHUB_APP_ACCESS_DENIED", 403)]
    [InlineData(HttpStatusCode.NotFound, "GITHUB_APP_INSTALLATION_UNAVAILABLE", 404)]
    [InlineData(HttpStatusCode.TooManyRequests, "GITHUB_RATE_LIMITED", 429)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "GITHUB_APP_TOKEN_REQUEST_FAILED", 502)]
    public async Task TokenErrorsAreClassifiedWithoutExposingProviderBody(HttpStatusCode status, string code, int expectedStatus)
    {
        using var rsa = RSA.Create(2048);
        var clock = new Clock();
        using var provider = new GitHubAppInstallationTokenProvider(new HttpClient(new Handler(clock) { Status = status }), Options.Create(
            new GitHubAppOptions { Enabled = true, ClientId = "fixture", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }), clock);
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => provider.GetAsync(42, 100, default));
        Assert.Equal(code, error.Code); Assert.Equal(expectedStatus, error.Status);
        Assert.DoesNotContain("fixture-token", error.Message);
    }

    [Fact]
    public async Task InvalidationDuringIssuanceDoesNotReturnOrCacheTheStaleToken()
    {
        using var rsa = RSA.Create(2048);
        var clock = new Clock();
        var handler = new Handler(clock) { Started = new(), Resume = new() };
        using var provider = new GitHubAppInstallationTokenProvider(new HttpClient(handler), Options.Create(
            new GitHubAppOptions { Enabled = true, ClientId = "fixture", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }), clock);
        var pending = provider.GetAsync(42, 100, default);
        await handler.Started.Task;
        provider.InvalidateInstallation(42);
        handler.Resume.SetResult();
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => pending);
        Assert.Equal("GITHUB_APP_INSTALLATION_CHANGED", error.Code);
        await provider.GetAsync(42, 100, default);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task CacheIsScopedToInstallationAndRepositoryAndExpiresOrInvalidates()
    {
        using var rsa = RSA.Create(2048);
        var clock = new Clock();
        var handler = new Handler(clock);
        using var provider = new GitHubAppInstallationTokenProvider(new HttpClient(handler), Options.Create(
            new GitHubAppOptions { Enabled = true, ClientId = "fixture", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }), clock);
        var first = await provider.GetAsync(42, 100, default);
        Assert.Equal(first, await provider.GetAsync(42, 100, default));
        Assert.NotEqual(first, await provider.GetAsync(42, 101, default));
        Assert.Equal(new long[] { 100, 101 }, handler.RequestedRepositories);
        clock.Now = clock.Now.AddMinutes(9).AddSeconds(1);
        Assert.NotEqual(first, await provider.GetAsync(42, 100, default));
        provider.InvalidateInstallation(42);
        await provider.GetAsync(42, 101, default);
        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task ProviderRefusesInvalidIdsAndDoesNotCacheGithubErrors()
    {
        using var rsa = RSA.Create(2048);
        var clock = new Clock();
        var handler = new Handler(clock) { Status = HttpStatusCode.Forbidden };
        using var provider = new GitHubAppInstallationTokenProvider(new HttpClient(handler), Options.Create(
            new GitHubAppOptions { Enabled = true, ClientId = "fixture", PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem() }), clock);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => provider.GetAsync(0, 1, default));
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => provider.GetAsync(42, 100, default));
        Assert.DoesNotContain("fixture-token", error.Message);
        handler.Status = HttpStatusCode.Created;
        await provider.GetAsync(42, 100, default);
        Assert.Equal(2, handler.Calls);
    }
}
