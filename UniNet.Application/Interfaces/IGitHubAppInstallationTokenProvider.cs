namespace UniNet.Application.Interfaces;

public interface IGitHubAppInstallationTokenProvider
{
    Task<string> GetAsync(long installationId, long repositoryId, CancellationToken ct);
    void InvalidateInstallation(long installationId);
}
