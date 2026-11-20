using UniNet.Application.DTOs;

namespace UniNet.Application.Interfaces;

public interface IGitHubAppRepositoryLinkService
{
    Task<GitHubAppRepositoryLinkResponse> LinkAsync(Guid accountId, Guid projectId, LinkGitHubAppRepositoryRequest request, CancellationToken ct);
}
