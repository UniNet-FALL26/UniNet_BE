namespace UniNet.Application.Interfaces;

public interface IGitExperimentalContributionService
{
    Task<GitExperimentalRunResponse> CreateRunAsync(
        Guid projectId, Guid accountId, GitExperimentalRunRequest request, CancellationToken ct);
    Task<GitExperimentalRunResponse> GetRunAsync(Guid projectId, Guid runId, Guid accountId, CancellationToken ct);
    Task<GitExperimentalRunResponse> ReviewRunAsync(
        Guid projectId, Guid runId, Guid accountId, GitExperimentalReviewRequest request, CancellationToken ct);
    Task<GitExperimentalRunResponse> FinalizeRunAsync(Guid projectId, Guid runId, Guid accountId, CancellationToken ct);
    Task<GitExperimentalFinalResultsResponse> GetFinalResultsAsync(Guid projectId, Guid accountId, CancellationToken ct);
}
