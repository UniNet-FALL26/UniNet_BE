using UniNet.Domain;

namespace UniNet.Application.Interfaces;

public interface IWorkspaceAccessService
{
    Task<UserProfile> RequireMemberAsync(Guid projectId, Guid accountId, CancellationToken ct = default);
    Task<UserProfile> RequireLeaderAsync(Guid projectId, Guid accountId, CancellationToken ct = default);
}
