using UniNet.Application.DTOs.Company;
using UniNet.Domain.Enums;

namespace UniNet.Application.Interfaces;

public interface ICompanyService
{
    Task<CompanyProfileResponse> GetProfileAsync(Guid accountId, CancellationToken ct);
    Task<CompanyProfileResponse> UpdateProfileAsync(Guid accountId, UpdateCompanyProfileRequest request, CancellationToken ct);
    Task<CompanyDashboardResponse> GetDashboardAsync(Guid accountId, CancellationToken ct);
    Task<CompanyJobsResponse> ListJobsAsync(Guid accountId, CompanyJobStatus? status, string? keyword, int page, int pageSize, CancellationToken ct);
    Task<CompanyJobResponse> GetJobAsync(Guid accountId, Guid jobId, CancellationToken ct);
    Task<CompanyJobResponse> CreateJobAsync(Guid accountId, SaveCompanyJobRequest request, CancellationToken ct);
    Task<CompanyJobResponse> UpdateJobAsync(Guid accountId, Guid jobId, SaveCompanyJobRequest request, CancellationToken ct);
    Task<CompanyJobResponse> UpdateJobStatusAsync(Guid accountId, Guid jobId, CompanyJobStatus status, CancellationToken ct);
    Task DeleteJobAsync(Guid accountId, Guid jobId, CancellationToken ct);
}
