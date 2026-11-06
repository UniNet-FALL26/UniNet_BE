using UniNet.Application.DTOs;

namespace UniNet.Application.Interfaces;

public interface IProjectGitManagerService
{
    Task<IReadOnlyList<ProjectGitManagerResponse>> GetAsync(Guid projectId, Guid accountId, CancellationToken ct);
    Task<ProjectGitManagerResponse> GrantAsync(Guid projectId, Guid accountId, GrantProjectGitManagerRequest request, CancellationToken ct);
    Task RevokeAsync(Guid projectId, Guid userProfileId, Guid accountId, CancellationToken ct);
}
