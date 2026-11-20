using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace UniNet.Application.Services;

/// <summary>Verifies a single repository against the GitHub App user-token view.</summary>
public sealed class GitHubAppUserGrantVerifier(IGitHubAppUserClient client, IOptions<GitHubAppOptions>? options = null)
{
    public async Task<(GitHubAppUserInstallation Installation, GitHubAppUserRepository Repository)> VerifyForManagementAsync(
        string userToken, long expectedGitHubUserId, long installationId, long repositoryId, CancellationToken ct)
    {
        var result = await VerifyAsync(userToken, expectedGitHubUserId, installationId, repositoryId, ct);
        if (!result.Repository.CanAdmin)
            throw Error("GITHUB_APP_REPOSITORY_ADMIN_REQUIRED", "GitHub repository administrator access is required.", 403);
        return result;
    }

    // A caller must independently check project creator/manager authorization first.
    public async Task<(GitHubAppUserInstallation Installation, GitHubAppUserRepository Repository)> VerifyAsync(
        string userToken, long expectedGitHubUserId, long installationId, long repositoryId, CancellationToken ct)
    {
        GitHubAppAvailability.Require(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedGitHubUserId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repositoryId);
        var user = await client.GetUserAsync(userToken, ct);
        if (user.Id != expectedGitHubUserId) throw Error("GITHUB_APP_USER_MISMATCH", "GitHub user does not match the authorized account.", 403);
        GitHubAppUserInstallation? installation = null;
        for (var page = 1; page <= 1000; page++)
        {
            var result = await client.GetInstallationsAsync(userToken, page, ct);
            installation = result.Items.SingleOrDefault(x => x.Id == installationId);
            if (installation is not null || !result.HasMore) break;
            if (page == 1000) throw Error("GITHUB_APP_PAGE_LIMIT", "GitHub installation listing is incomplete.", 502);
        }
        if (installation is null || installation.Suspended)
            throw Error("GITHUB_APP_INSTALLATION_UNAVAILABLE", "Installation is not available to this user.", 403);
        for (var page = 1; page <= 1000; page++)
        {
            var result = await client.GetRepositoriesAsync(userToken, installationId, page, ct);
            var repository = result.Items.SingleOrDefault(x => x.Id == repositoryId);
            if (repository is { CanRead: true }) return (installation, repository);
            if (repository is not null || !result.HasMore) break;
            if (page == 1000) throw Error("GITHUB_APP_PAGE_LIMIT", "GitHub repository listing is incomplete.", 502);
        }
        throw Error("GITHUB_APP_REPOSITORY_UNAVAILABLE", "Repository is not available to this user in this installation.", 403);
    }

    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
