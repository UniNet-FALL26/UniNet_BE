namespace UniNet.Application.Interfaces;

public interface IContributionService
{
    Task<ContributionCycleResponse> GetCycleAsync(Guid projectId, Guid accountId, CancellationToken ct);
    Task<IReadOnlyList<ContributionPeerEvaluationTaskResponse>> GetMyPeerEvaluationsAsync(
        Guid projectId, Guid accountId, CancellationToken ct);
    Task<ContributionPeerEvaluationResponse> UpsertPeerEvaluationAsync(
        Guid projectId, Guid evaluateeUserId, Guid accountId, ContributionPeerEvaluationRequest request, CancellationToken ct);
    Task<ContributionRunResponse> CalculateAsync(Guid projectId, Guid accountId, CancellationToken ct);
    Task<ContributionRunResponse> GetRunAsync(Guid projectId, Guid runId, Guid accountId, CancellationToken ct);
    Task<ContributionReviewResponse> ReviewRunAsync(
        Guid projectId, Guid runId, Guid accountId, ContributionReviewRequest request, CancellationToken ct);
    Task<ContributionFinalResultsResponse> GetFinalResultsAsync(Guid projectId, Guid accountId, CancellationToken ct);
}
