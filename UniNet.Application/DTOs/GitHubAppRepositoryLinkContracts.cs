using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs;

public sealed record LinkGitHubAppRepositoryRequest(Guid CredentialId, long InstallationId, long RepositoryId);
public sealed record GitHubAppRepositoryLinkResponse(Guid ProjectGitRepositoryId, long RepositoryId, string RepositoryFullName,
    GitRepositoryCredentialSource CredentialSource, long InstallationId, ProjectGitRepositoryAccessStatus LinkStatus,
    string Readiness, bool SyncPaused, bool WasAlreadyLinked);
