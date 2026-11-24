namespace UniNet.Application.DTOs;

public sealed record StartGitHubAppAuthorizationRequest(Guid OAuthConnectionId, string ReturnTarget);
public sealed record StartGitHubAppAuthorizationResponse(Guid FlowId, string AuthorizationUrl, DateTimeOffset ExpiresAt);
public sealed record GitHubAppAuthorizationCallbackResult(Uri RedirectUri);
