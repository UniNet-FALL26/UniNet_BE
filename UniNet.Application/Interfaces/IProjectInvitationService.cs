using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IProjectInvitationService
{
    Task<ProjectInvitationResponse> SendAsync(Guid projectId, Guid accountId,
        CreateProjectInvitationRequest request, CancellationToken ct = default);
    Task<ProjectInvitationsResponse> GetProjectInvitationsAsync(Guid projectId, Guid accountId,
        ProjectInvitationStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<ProjectInvitationsResponse> GetMyInvitationsAsync(Guid accountId,
        ProjectInvitationStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<ProjectInvitationResponse> CancelAsync(Guid invitationId, Guid accountId, CancellationToken ct = default);
    Task<ProjectInvitationResponse> DeclineAsync(Guid invitationId, Guid accountId, CancellationToken ct = default);
    Task<ProjectInvitationResponse> AcceptAsync(Guid invitationId, Guid accountId, CancellationToken ct = default);
}
