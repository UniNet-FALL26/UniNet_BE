using UniNet.Application.DTOs.Partner;
using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface IStudentOpportunityService
{
    Task<OpportunityJobsResponse> ListAsync(Guid accountId, string? view, string? keyword, JobWorkType? workType, int page, int pageSize, CancellationToken ct);
    Task<OpportunityJobResponse> GetAsync(Guid accountId, Guid jobId, CancellationToken ct);
    Task<PublicPartnerProfileResponse> GetPartnerProfileAsync(Guid accountId, Guid jobId, CancellationToken ct);
    Task<IReadOnlyList<StudentSubmittedApplicationResponse>> GetOwnApplicationsAsync(Guid accountId, Guid jobId, CancellationToken ct);
    Task SaveAsync(Guid accountId, Guid jobId, CancellationToken ct);
    Task UnsaveAsync(Guid accountId, Guid jobId, CancellationToken ct);
    Task ApplyAsync(Guid accountId, Guid jobId, string? coverMessage, CancellationToken ct);
}
