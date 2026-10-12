using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IProjectJoinRequestService
{
    Task<ProjectJoinRequestResponse> SendAsync(Guid projectId, Guid accountId,
        CreateProjectJoinRequestRequest request, CancellationToken ct = default);
    Task<ProjectJoinRequestsResponse> GetProjectRequestsAsync(Guid projectId, Guid accountId,
        ProjectJoinRequestStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<ProjectJoinRequestsResponse> GetMyRequestsAsync(Guid accountId,
        ProjectJoinRequestStatus? status = null, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<ProjectJoinRequestResponse> CancelAsync(Guid requestId, Guid accountId, CancellationToken ct = default);
    Task<ProjectJoinRequestResponse> AcceptAsync(Guid requestId, Guid accountId, CancellationToken ct = default);
    Task<ProjectJoinRequestResponse> RejectAsync(Guid requestId, Guid accountId, CancellationToken ct = default);
}
