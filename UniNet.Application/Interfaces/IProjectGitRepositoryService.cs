using UniNet.Application.DTOs;

namespace UniNet.Application.Interfaces;

public interface IProjectGitRepositoryService
{
    Task<GitHubRepositoryPage> DiscoverAsync(Guid accountId, Guid projectId, Guid connectionId, int page, int pageSize, CancellationToken ct);
    Task<ProjectGitRepositoryResponse> LinkAsync(Guid accountId, Guid projectId, LinkGitHubRepositoryRequest request, CancellationToken ct);
    Task<ProjectGitRepositoryPage> GetAsync(Guid accountId, Guid projectId, int page, int pageSize, CancellationToken ct);
    Task<ProjectGitRepositoryResponse> GetAsync(Guid accountId, Guid projectId, Guid linkId, CancellationToken ct);
    Task<ProjectGitRepositoryResponse> ReplaceCredentialAsync(Guid accountId, Guid projectId, Guid linkId, ReplaceGitHubRepositoryCredentialRequest request, CancellationToken ct);
    Task UnlinkAsync(Guid accountId, Guid projectId, Guid linkId, CancellationToken ct);
}
