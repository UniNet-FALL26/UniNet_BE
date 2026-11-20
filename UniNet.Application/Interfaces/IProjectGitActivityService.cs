using UniNet.Application.DTOs;

namespace UniNet.Application.Interfaces;

public interface IProjectGitActivityService
{
    Task<GitActivityPage> GetAsync(
        Guid accountId, Guid projectId, int page, int pageSize,
        string? activityType, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}
