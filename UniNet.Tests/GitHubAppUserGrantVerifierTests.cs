using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppUserGrantVerifierTests
{
    private sealed class Provider : IGitHubAppUserClient
    {
        public long UserId = 1;
        public bool Suspended;
        public bool Read = true;
        public bool Admin;
        public int RepositoryCalls;
        public Task<GitHubAppUserToken> ExchangeAsync(string code, CancellationToken ct) => throw new NotImplementedException();
        public Task<GitHubAppUserIdentity> GetUserAsync(string token, CancellationToken ct) => Task.FromResult(new GitHubAppUserIdentity(UserId, "user"));
        public Task<GitHubAppUserPage<GitHubAppUserInstallation>> GetInstallationsAsync(string token, int page, CancellationToken ct) =>
            Task.FromResult(new GitHubAppUserPage<GitHubAppUserInstallation>([new(2, 3, "org", "Organization", Suspended)], false));
        public Task<GitHubAppUserPage<GitHubAppUserRepository>> GetRepositoriesAsync(string token, long installationId, int page, CancellationToken ct)
        {
            RepositoryCalls++;
            return Task.FromResult(new GitHubAppUserPage<GitHubAppUserRepository>([new(4, "org/repo", true, Read, Admin)], false));
        }
    }

    [Fact]
    public async Task ManagementRequiresActualAdminPermissionNotOnlyRead()
    {
        var provider = new Provider();
        var verifier = new GitHubAppUserGrantVerifier(provider, Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true }));
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() => verifier.VerifyForManagementAsync("user-token", 1, 2, 4, default));
        Assert.Equal("GITHUB_APP_REPOSITORY_ADMIN_REQUIRED", error.Code);
        provider.Admin = true;
        Assert.Equal(4, (await verifier.VerifyForManagementAsync("user-token", 1, 2, 4, default)).Repository.Id);
    }

    [Fact]
    public async Task VerifiesUserInstallationAndReadableRepository()
    {
        var result = await new GitHubAppUserGrantVerifier(new Provider(), Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })).VerifyAsync("user-token", 1, 2, 4, default);
        Assert.Equal(4, result.Repository.Id);
        Assert.True(result.Repository.IsPrivate);
    }

    [Theory]
    [InlineData(9, 2, 4, false, true, "GITHUB_APP_USER_MISMATCH")]
    [InlineData(1, 9, 4, false, true, "GITHUB_APP_INSTALLATION_UNAVAILABLE")]
    [InlineData(1, 2, 4, true, true, "GITHUB_APP_INSTALLATION_UNAVAILABLE")]
    [InlineData(1, 2, 9, false, true, "GITHUB_APP_REPOSITORY_UNAVAILABLE")]
    [InlineData(1, 2, 4, false, false, "GITHUB_APP_REPOSITORY_UNAVAILABLE")]
    public async Task RejectsUserOrGrantMismatch(long user, long installation, long repo, bool suspended, bool read, string code)
    {
        var provider = new Provider { Suspended = suspended, Read = read };
        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            new GitHubAppUserGrantVerifier(provider, Microsoft.Extensions.Options.Options.Create(new GitHubAppOptions { Enabled = true })).VerifyAsync("user-token", user, installation, repo, default));
        Assert.Equal(code, error.Code);
        if (code != "GITHUB_APP_REPOSITORY_UNAVAILABLE") Assert.Equal(0, provider.RepositoryCalls);
    }
}
