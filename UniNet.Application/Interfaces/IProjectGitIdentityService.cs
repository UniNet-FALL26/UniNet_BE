using UniNet.Application.DTOs;
using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IProjectGitIdentityService
{
    Task<ProjectGitMemberIdentityPage> GetAsync(
        Guid accountId, Guid projectId, int page, int pageSize, Guid? userProfileId,
        ProjectGitMemberIdentityStatus? status, CancellationToken ct);
    Task<ProjectGitMemberIdentityResponse> GetAsync(
        Guid accountId, Guid projectId, Guid identityId, CancellationToken ct);
    Task<ProjectGitMemberIdentityResponse> LinkAsync(
        Guid accountId, Guid projectId, LinkProjectGitIdentityRequest request, CancellationToken ct);
    Task<ProjectGitMemberIdentityResponse> UnlinkAsync(
        Guid accountId, Guid projectId, Guid identityId, CancellationToken ct);
    Task<ProjectGitMemberIdentityResponse> ReactivateAsync(
        Guid accountId, Guid projectId, Guid identityId,
        ReactivateProjectGitIdentityRequest request, CancellationToken ct);
    Task<ProjectGitMemberIdentityHistoryPage> GetHistoryAsync(
        Guid accountId, Guid projectId, Guid identityId, int page, int pageSize, CancellationToken ct);
}
