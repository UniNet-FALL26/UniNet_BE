using Microsoft.Extensions.Options;
using UniNet.Application.Common;

namespace UniNet.Application.Services;

public static class GitHubAppAvailability
{
    public static void Require(IOptions<GitHubAppOptions>? options)
    {
        if (options?.Value.Enabled != true)
            throw new GitHubIntegrationException("GITHUB_APP_DISABLED", "GitHub App integration is unavailable.", 503);
    }
}
