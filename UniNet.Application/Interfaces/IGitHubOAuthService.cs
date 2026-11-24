using UniNet.Application.DTOs;

namespace UniNet.Application.Interfaces;

public interface IGitHubOAuthService
{
    Task<GitHubOAuthStartResponse> StartAsync(Guid accountId, StartGitHubOAuthRequest request, CancellationToken ct);
    Task<GitHubOAuthCallbackResult> ProcessCallbackAsync(string? code, string? state, string? providerError, CancellationToken ct);
    Task<GitHubOAuthConnectionResponse> CompleteAsync(Guid accountId, CompleteGitHubOAuthRequest request, CancellationToken ct);
    Task<IReadOnlyList<GitHubOAuthConnectionResponse>> GetConnectionsAsync(Guid accountId, CancellationToken ct);
    Task DisconnectAsync(Guid accountId, Guid connectionId, CancellationToken ct);
}
