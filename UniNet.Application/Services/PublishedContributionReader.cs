using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class PublishedContributionReader(UniNetDbContext db)
{
    public async Task<PublishedContributionResponse> ReadAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        // Project Detail currently requires an active Student session. No anonymous expansion here.
        var actor = await ProjectGitAuthorization.RequireActorProfileAsync(db, accountId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Projects\" WHERE \"Id\"={projectId} FOR SHARE").ToListAsync(ct);
        var project = await db.Projects.AsNoTracking().Where(x => x.Id == projectId)
            .Select(x => new { x.CreatorId, x.Visibility }).SingleOrDefaultAsync(ct)
            ?? throw new ProjectException("PROJECT_NOT_FOUND", "Project not found.", 404);
        if (project.Visibility != ProjectVisibility.Public && project.CreatorId != actor &&
            !await ProjectMembership.IsEffectiveMemberAsync(db, projectId, actor, ct))
            throw new ProjectException("FORBIDDEN", "Project access is required.", 403);

        var release = await db.Set<OfficialContributionRelease>().AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (release is null) { await tx.CommitAsync(ct); return new(projectId, null, null, null, []); }
        var run = await db.Set<OfficialContributionRun>().AsNoTracking().SingleAsync(x => x.Id == release.RunId && x.ProjectId == projectId, ct);
        // Project only fields needed for frozen result integrity. Never fetch Peer evaluations or
        // the whole input JSON (which contains private historical attribution and evaluations).
        var result = await db.GitContributionRuns.AsNoTracking().Where(x => x.Id == release.RunId && x.ProjectId == projectId)
            .Select(x => new GitContributionRun { Id = x.Id, ProjectId = x.ProjectId, InputHash = x.InputHash,
                ScoringPolicyVersion = x.ScoringPolicyVersion, NormalizationPolicyVersion = x.NormalizationPolicyVersion,
                PeerSnapshotId = x.PeerSnapshotId, CalculationStatus = x.CalculationStatus, ResultsJson = x.ResultsJson,
                NormalizedResultsJson = x.NormalizedResultsJson, CategoryStatusJson = x.CategoryStatusJson,
                ExcludedActivitiesJson = x.ExcludedActivitiesJson }).SingleAsync(ct);
        try
        {
            if (result.CalculationStatus != "Ready" || run.ResultHash != GitExperimentalContributionService.OfficialResultHash(result))
                throw new InvalidOperationException();
            var rosterJson = await db.Database.SqlQuery<string>($"""
                SELECT ("InputSnapshotJson"::jsonb->'roster')::text AS "Value" FROM "GitContributionRuns"
                WHERE "Id"={release.RunId} AND "ProjectId"={projectId}
                """).SingleAsync(ct);
            using var roster = JsonDocument.Parse(rosterJson);
            using var normalized = JsonDocument.Parse(result.NormalizedResultsJson!);
            var ids = roster.RootElement.EnumerateArray().Select(x => x.GetProperty("userId").GetGuid()).ToArray();
            var values = normalized.RootElement.GetProperty("members").EnumerateObject()
                .ToDictionary(x => Guid.Parse(x.Name), x => x.Value.GetDecimal());
            if (ids.Distinct().Count() != ids.Length || !ids.ToHashSet().SetEquals(values.Keys) ||
                values.Values.Any(x => x < 0 || x > 100)) throw new InvalidOperationException();
            var members = ids.OrderBy(x => x).Select(id => new PublishedContributionMember(id, values[id])).ToArray();
            await tx.CommitAsync(ct);
            return new(projectId, release.RunId, release.Version, release.PublishedAt, members);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or OverflowException)
        { throw new ProjectException("PUBLISHED_CONTRIBUTION_INTEGRITY", "Published results are unavailable.", 409); }
    }
}
