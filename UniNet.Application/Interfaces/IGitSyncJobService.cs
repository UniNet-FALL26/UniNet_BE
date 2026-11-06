using UniNet.Application.DTOs;
using UniNet.Domain.Entities;

namespace UniNet.Application.Interfaces;

public interface IGitSyncJobService
{
    Task<GitSyncJob?> ClaimNextAsync(TimeSpan leaseDuration, CancellationToken ct);
    Task<bool> RenewLeaseAsync(Guid jobId, Guid leaseId, TimeSpan leaseDuration, CancellationToken ct);
    Task CompleteAsync(Guid jobId, Guid leaseId, CancellationToken ct);
    Task<bool> FailAsync(
        Guid jobId, Guid leaseId, string errorCode, string safeErrorMessage,
        DateTimeOffset retryAt, CancellationToken ct, bool retryable = true);
    Task<int> EnqueueDueScheduledJobsAsync(DateTimeOffset now, CancellationToken ct);

    Task<GitSyncRequestResult> RequestManualSyncAsync(
        Guid accountId, Guid projectId, Guid projectGitRepositoryId, CancellationToken ct);

    Task<ProjectGitSyncStatusResponse> UpdateSyncSettingsAsync(
        Guid accountId, Guid projectId, Guid projectGitRepositoryId,
        GitSyncSettingsRequest request, CancellationToken ct);

    Task<GitSyncJobPage> GetJobsAsync(
        Guid accountId, Guid projectId, Guid projectGitRepositoryId,
        int page, int pageSize, CancellationToken ct);

    Task<ProjectGitSyncStatusPage> GetProjectStatusAsync(
        Guid accountId, Guid projectId, CancellationToken ct);
}
