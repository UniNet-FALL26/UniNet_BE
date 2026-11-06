using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectGitActivityService(UniNetDbContext db) : IProjectGitActivityService
{
    public async Task<GitActivityPage> GetAsync(
        Guid accountId, Guid projectId, int page, int pageSize,
        string? activityType, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100)
            throw Error("INVALID_PAGINATION", "Page must be between 1 and 10000 and page size between 1 and 100.", 400);
        if (from is not null && to is not null && from > to)
            throw Error("INVALID_ACTIVITY_RANGE", "The activity start time must be before its end time.", 400);
        if (activityType is { Length: > 100 })
            throw Error("INVALID_ACTIVITY_TYPE", "Activity type is too long.", 400);

        var visibilityQuery =
            from visibility in db.ProjectGitActivityVisibilities.AsNoTracking()
            join link in db.ProjectGitRepositories.AsNoTracking()
                on visibility.ProjectGitRepositoryId equals link.Id
            join activity in db.GitActivities.AsNoTracking()
                on visibility.GitActivityId equals activity.Id
            join source in db.GitHubProviderObjects.AsNoTracking()
                on activity.GitHubProviderObjectId equals source.Id
            join repository in db.GitHubRepositories.AsNoTracking()
                on source.GitHubRepositoryId equals repository.Id
            where link.ProjectId == projectId
            select new
            {
                activity.Id,
                activity.ActivityType,
                activity.OccurredAt,
                RepositoryFullName = repository.FullName,
                SourceCategory = source.Category,
                source.ExternalObjectId,
                visibility.ObservedAt,
                link.UnlinkedAt,
                link.AccessStatus,
                link.LastAccessCheckedAt
            };

        if (!string.IsNullOrWhiteSpace(activityType))
            visibilityQuery = visibilityQuery.Where(x => x.ActivityType == activityType);

        List<ActivityRow> rows;
        int total;
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            rows = (await visibilityQuery.ToListAsync(ct))
                .Where(x => (x.UnlinkedAt is null || x.ObservedAt <= x.UnlinkedAt) &&
                    (x.AccessStatus == UniNet.Domain.Enums.ProjectGitRepositoryAccessStatus.Active ||
                     x.LastAccessCheckedAt is { } cutoff && x.ObservedAt <= cutoff))
                .Where(x => from is null || x.OccurredAt >= from)
                .Where(x => to is null || x.OccurredAt <= to)
                .Select(x => new ActivityRow(x.Id, x.ActivityType, x.OccurredAt,
                    x.RepositoryFullName, x.SourceCategory, x.ExternalObjectId))
                .DistinctBy(x => x.Id)
                .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).ToList();
            total = rows.Count;
            rows = rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            if (from is { } start) visibilityQuery = visibilityQuery.Where(x => x.OccurredAt >= start);
            if (to is { } end) visibilityQuery = visibilityQuery.Where(x => x.OccurredAt <= end);
            var query = visibilityQuery
                .Where(x => (x.UnlinkedAt == null || x.ObservedAt <= x.UnlinkedAt) &&
                    (x.AccessStatus == UniNet.Domain.Enums.ProjectGitRepositoryAccessStatus.Active ||
                     x.LastAccessCheckedAt != null && x.ObservedAt <= x.LastAccessCheckedAt))
                .Select(x => new ActivityRow(
                    x.Id, x.ActivityType, x.OccurredAt, x.RepositoryFullName,
                    x.SourceCategory, x.ExternalObjectId));
            total = await query.CountAsync(ct);
            rows = await query.Distinct().OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        }

        var activityIds = rows.Select(x => x.Id).ToArray();
        var actors = await db.GitActivityActors.AsNoTracking()
            .Where(x => activityIds.Contains(x.GitActivityId))
            .OrderBy(x => x.Role).ThenBy(x => x.ExternalAccountId)
            .ToListAsync(ct);
        var actorsByActivity = actors.GroupBy(x => x.GitActivityId)
            .ToDictionary(x => x.Key, x => x.Select(actor =>
                new GitActivityActorResponse(actor.Role, actor.ExternalAccountId, actor.Login)).ToArray());
        return new(rows.Select(row => new GitActivityResponse(
            row.Id, row.ActivityType, row.OccurredAt, row.RepositoryFullName,
            row.SourceCategory, row.ExternalObjectId,
            actorsByActivity.GetValueOrDefault(row.Id) ?? [])).ToArray(), page, pageSize, total);
    }

    private static GitHubIntegrationException Error(string code, string message, int status) =>
        new(code, message, status);

    private sealed record ActivityRow(
        Guid Id, string ActivityType, DateTimeOffset OccurredAt,
        string RepositoryFullName, string SourceCategory, string ExternalObjectId);
}
