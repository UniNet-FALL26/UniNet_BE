using UniNet.Domain.Entities;

namespace UniNet.Application.Interfaces;

public interface IGitSyncJobProcessor
{
    Task ProcessAsync(GitSyncJob job, CancellationToken ct);
}
