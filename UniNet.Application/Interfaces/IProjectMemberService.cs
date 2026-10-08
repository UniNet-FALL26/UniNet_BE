namespace UniNet.Application.Interfaces;

public interface IProjectMemberService
{
    Task<ProjectMembersListResponse> GetProjectMembersAsync(Guid projectId, CancellationToken ct);
    // userId identifies the member's UserProfile; accountId identifies the acting account.
    Task<ProjectMemberResponse> KickAsync(Guid projectId, Guid userId, Guid accountId, CancellationToken ct = default);
    Task<ProjectMemberResponse> LeaveAsync(Guid projectId, Guid accountId, CancellationToken ct = default);
}
