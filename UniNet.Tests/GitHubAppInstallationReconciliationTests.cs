using System.Net;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppInstallationReconciliationTests
{
    private sealed class PagingHandler(bool failSecondPage) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"token\":\"fixture\"}") });
            Assert.Equal("fixture", request.Headers.Authorization?.Parameter);
            if (request.RequestUri!.Query.Contains("page=2"))
                return Task.FromResult(new HttpResponseMessage(failSecondPage ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
                { Content = new StringContent("{\"total_count\":101,\"repositories\":[{\"id\":101}]}") });
            var entries = string.Join(",", Enumerable.Range(1, 100).Select(i => $"{{\"id\":{i}}}"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent($"{{\"total_count\":101,\"repositories\":[{entries}]}}") });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepositoryEnumerationRequiresEveryPage(bool failSecondPage)
    {
        using var rsa = RSA.Create(2048);
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite("Data Source=:memory:").Options);
        using var http = new HttpClient(new PagingHandler(failSecondPage));
        var service = new GitHubAppInstallationReconciliationService(db, http,
            Options.Create(new GitHubAppOptions { Enabled = true, ClientId = "test-app", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() }),
            Options.Create(new GitSyncOptions { Enabled = true }), new Tokens());
        var result = await service.ReadRepositoriesAsync(123, default);
        if (failSecondPage) Assert.Null(result);
        else { Assert.NotNull(result); Assert.Equal(101, result.Count); Assert.Contains(101, result); }
    }
    private sealed class Handler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("/app/installations/123", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
        }
    }

    private sealed class Tokens : IGitHubAppInstallationTokenProvider
    {
        public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct) => throw new NotSupportedException();
        public void InvalidateInstallation(long installationId) { }
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"id\":123,\"suspended_at\":null}", GitHubAppInstallationStatus.Active)]
    [InlineData(HttpStatusCode.OK, "{\"id\":123,\"suspended_at\":\"2026-01-01T00:00:00Z\"}", GitHubAppInstallationStatus.Suspended)]
    [InlineData(HttpStatusCode.OK, "{\"id\":999,\"suspended_at\":null}", null)]
    [InlineData(HttpStatusCode.OK, "{}", null)]
    [InlineData(HttpStatusCode.Forbidden, "{}", null)]
    [InlineData(HttpStatusCode.NotFound, "{}", null)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", null)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", null)]
    public async Task OnlyVerifiedInstallationResponsesChangeStatus(HttpStatusCode status, string body, GitHubAppInstallationStatus? expected)
    {
        using var rsa = RSA.Create(2048);
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite("Data Source=:memory:").Options);
        using var http = new HttpClient(new Handler(status, body));
        var service = new GitHubAppInstallationReconciliationService(db, http,
            Options.Create(new GitHubAppOptions { Enabled = true, ClientId = "test-app", PrivateKeyPem = rsa.ExportRSAPrivateKeyPem() }),
            Options.Create(new GitSyncOptions { Enabled = true }), new Tokens());
        Assert.Equal(expected, await service.CheckInstallationAsync(123, default));
    }
}
