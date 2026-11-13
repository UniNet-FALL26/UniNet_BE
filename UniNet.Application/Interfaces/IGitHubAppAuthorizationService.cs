using UniNet.Application.DTOs;

namespace UniNet.Application.Interfaces;

public interface IGitHubAppAuthorizationService
{
    Task<StartGitHubAppAuthorizationResponse> StartAsync(Guid accountId, Guid projectId, StartGitHubAppAuthorizationRequest request, CancellationToken ct);
    Task<GitHubAppAuthorizationCallbackResult> CallbackAsync(string? state, string? code, string? providerError, CancellationToken ct);
}
