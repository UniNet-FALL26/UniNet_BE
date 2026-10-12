using UniNet.Application.DTOs.Partner;
using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IPartnerService
{
    Task<PartnerDashboardResponse> GetDashboardAsync(Guid accountId, CancellationToken ct);
    Task<PartnerJobsResponse> ListJobsAsync(Guid accountId, PartnerJobStatus? status, string? keyword, int page, int pageSize, CancellationToken ct);
    Task<PartnerJobResponse> GetJobAsync(Guid accountId, Guid jobId, CancellationToken ct);
    Task<PartnerJobResponse> CreateJobAsync(Guid accountId, SavePartnerJobRequest request, CancellationToken ct);
    Task<PartnerJobResponse> UpdateJobAsync(Guid accountId, Guid jobId, SavePartnerJobRequest request, CancellationToken ct);
    Task<PartnerJobResponse> UpdateJobStatusAsync(Guid accountId, Guid jobId, PartnerJobStatus status, CancellationToken ct);
    Task DeleteJobAsync(Guid accountId, Guid jobId, CancellationToken ct);
}
