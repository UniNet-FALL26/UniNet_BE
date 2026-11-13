using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;

namespace UniNet.API.Services;

// Enforce the feature switch before consulting even the token cache.
public sealed class GitHubAppEnabledTokenProvider(GitHubAppInstallationTokenProvider inner, IOptions<GitHubAppOptions> options) : IGitHubAppInstallationTokenProvider
{
    public Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct)
    {
        if (!options.Value.Enabled) throw new GitHubIntegrationException("GITHUB_APP_DISABLED", "GitHub App integration is unavailable.", 503);
        return inner.GetAsync(installationId, repositoryId, ct);
    }
    public void InvalidateInstallation(long installationId) => inner.InvalidateInstallation(installationId);
}
