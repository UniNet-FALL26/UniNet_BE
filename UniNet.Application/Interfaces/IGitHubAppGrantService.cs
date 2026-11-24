using UniNet.Application.DTOs;

namespace UniNet.Application.Interfaces;

public interface IGitHubAppGrantService
{
    Task<IReadOnlyList<GitHubAppCredentialItem>> GetCredentialsAsync(Guid accountId, Guid projectId, CancellationToken ct);
    Task<GitHubAppGrantPage<GitHubAppInstallationItem>> GetInstallationsAsync(Guid accountId, Guid projectId, Guid credentialId, int page, CancellationToken ct);
    Task<GitHubAppGrantPage<GitHubAppRepositoryItem>> GetRepositoriesAsync(Guid accountId, Guid projectId, Guid credentialId, long installationId, int page, CancellationToken ct);
}
