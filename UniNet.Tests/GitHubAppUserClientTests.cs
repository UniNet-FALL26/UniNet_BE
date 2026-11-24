using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using UniNet.API.Services;
using UniNet.Application.Common;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppUserClientTests
{
    [Theory]
    [InlineData("https://github.com/settings/installations/12", true)]
    [InlineData("https://github.com/organizations/example-org/settings/installations/12", true)]
    [InlineData("https://github.com/settings/installations/99", false)]
    [InlineData("http://github.com/settings/installations/12", false)]
    [InlineData("https://github.com.evil.test/settings/installations/12", false)]
    [InlineData("https://user@github.com/settings/installations/12", false)]
    [InlineData("https://github.com/settings/installations/12?redirect=https://evil.test", false)]
    [InlineData("https://github.com/login/oauth/authorize", false)]
    public async Task ConfigurationLinkMustComeFromProviderAndMatchInstallation(string url, bool valid)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { installations = new[] {
            new { id = 12, account = new { id = 22, login = "example-org", type = "Organization" }, html_url = url }
        } });
        var client = new GitHubAppUserClient(new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(payload) })), Options.Create(new GitHubAppOptions { Enabled = true }));
        var result = await client.GetInstallationsAsync("user-fixture", 1, default);
        Assert.Equal(valid ? url : null, Assert.Single(result.Items).ConfigurationUrl);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task ListsUserVisibleInstallationsAndRepositoriesUsingUserToken()
    {
        var client = new GitHubAppUserClient(new HttpClient(new Handler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("user-fixture", request.Headers.Authorization.Parameter);
            var json = request.RequestUri!.AbsolutePath switch
            {
                "/user/installations" => """{"installations":[{"id":12,"account":{"id":22,"login":"example-org","type":"Organization"},"suspended_at":null}]}""",
                "/user/installations/12/repositories" => """{"repositories":[{"id":33,"full_name":"example-org/repo","private":true,"owner":{"id":22,"login":"example-org"},"name":"repo","html_url":"https://github.com/example-org/repo","permissions":{"pull":true,"admin":true}}]}""",
                _ => throw new InvalidOperationException()
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        })), Options.Create(new GitHubAppOptions { Enabled = true }));
        var installations = await client.GetInstallationsAsync("user-fixture", 1, default);
        Assert.Equal("Organization", Assert.Single(installations.Items).OwnerType);
        var repos = await client.GetRepositoriesAsync("user-fixture", 12, 1, default);
        Assert.True(Assert.Single(repos.Items).CanRead);
        Assert.True(repos.Items[0].IsPrivate);
        Assert.True(repos.Items[0].CanAdmin);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    public async Task ProviderErrorsDoNotExposeBody(HttpStatusCode status, int expected)
    {
        var client = new GitHubAppUserClient(new HttpClient(new Handler(_ => new(status)
        { Content = new StringContent("private-response-fixture") })), Options.Create(new GitHubAppOptions { Enabled = true }));
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => client.GetRepositoriesAsync("user-fixture", 12, 1, default));
        Assert.Equal(expected, error.Status);
        Assert.DoesNotContain("private-response-fixture", error.Message);
    }
}
