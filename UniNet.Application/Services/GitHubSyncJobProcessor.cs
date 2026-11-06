using System.Globalization;
using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubSyncJobProcessor(
    UniNetDbContext db,
    IGitHubProviderClient provider,
    GitHubCredentialService credentials,
    IOptions<GitSyncOptions>? syncOptions = null) : IGitSyncJobProcessor
{
    private int PageSize => Math.Clamp(syncOptions?.Value.BatchSize ?? 100, 1, 100);

    public async Task ProcessAsync(GitSyncJob job, CancellationToken ct)
    {
        var link = await LoadEligibleLinkAsync(job.ProjectGitRepositoryId, ct,
            includeRecoverable: job.SyncType == GitSyncType.Scheduled);
        if (link.GitHubRepositoryId != job.GitHubRepositoryId ||
            link.OAuthConnectionId != job.OAuthConnectionId)
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed before synchronization.", 409);
        if (link.AccessStatus != ProjectGitRepositoryAccessStatus.Active)
            link = await ReconcileAccessAsync(link, ct);

        await ProcessCategoryAsync(job.Id, link, "branches", null, ct);
        var branches = await db.GitHubProviderObjects.AsNoTracking()
            .Where(x => x.GitHubRepositoryId == link.GitHubRepositoryId && x.Category == "branches" &&
                db.ProjectGitActivityGrants.Any(grant =>
                    grant.GitHubProviderObjectId == x.Id && grant.ProjectGitRepositoryId == link.Id))
            .Select(x => x.PayloadJson).ToListAsync(ct);
        foreach (var payload in branches)
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("name", out var name) &&
                name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                await ProcessCategoryAsync(job.Id, link, "commits", name.GetString(), ct);
        }

        await ProcessCategoryAsync(job.Id, link, "pull-requests", null, ct);
        var pullRequests = await ReadObjectScopesAsync(link.Id, link.GitHubRepositoryId, "pull-requests", ct);
        foreach (var number in pullRequests)
        {
            await ProcessCategoryAsync(job.Id, link, "pull-request-reviews", number, ct);
            await ProcessCategoryAsync(job.Id, link, "pull-request-review-comments", number, ct);
        }

        await ProcessCategoryAsync(job.Id, link, "issues", null, ct);
        var issues = await ReadObjectScopesAsync(link.Id, link.GitHubRepositoryId, "issues", ct);
        foreach (var number in issues)
            await ProcessCategoryAsync(job.Id, link, "issue-comments", number, ct);

        await ProcessCategoryAsync(job.Id, link, "releases", null, ct);
    }

    private async Task ProcessCategoryAsync(
        Guid jobId,
        EligibleLink link,
        string category,
        string? scope,
        CancellationToken ct)
    {
        var checkpointScope = scope ?? "";
        var checkpoint = await db.GitSyncCheckpoints.AsNoTracking().SingleOrDefaultAsync(
            x => x.SyncJobId == jobId && x.Category == category && x.ScopeKey == checkpointScope, ct);
        if (checkpoint?.Status == GitSyncCheckpointStatus.Completed) return;
        var pageNumber = checkpoint is not null &&
            int.TryParse(checkpoint.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var savedPage)
                ? Math.Max(1, savedPage)
                : 1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var eligible = await LoadEligibleLinkAsync(link.Id, ct);
            if (eligible.GitHubRepositoryId != link.GitHubRepositoryId ||
                eligible.OAuthConnectionId != link.OAuthConnectionId)
                throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed during synchronization.", 409);
            var accessToken = await credentials.GetValidAccessTokenAsync(
                eligible.AccountId, eligible.OAuthConnectionId, eligible.LinkedByProfileId, ct);
            GitHubSyncPage result;
            try
            {
                result = await provider.GetSyncPageAsync(accessToken, eligible.FullName,
                    category, scope, pageNumber, PageSize, ct);
            }
            catch (GitHubIntegrationException exception) when (exception.Code == "GITHUB_CREDENTIAL_INVALID")
            {
                await credentials.MarkReauthorizationRequiredAsync(
                    eligible.OAuthConnectionId, eligible.LinkedByProfileId, ct);
                throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect the selected GitHub account before synchronizing.", 409);
            }
            catch (GitHubIntegrationException exception) when (exception.Code == "GITHUB_ACCESS_DENIED")
            {
                await MarkAccessLostAsync(eligible, ct);
                throw Error("GITHUB_REPOSITORY_ACCESS_LOST", "GitHub denied access to this repository.", 409);
            }

            var observedAt = DateTimeOffset.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            eligible = await LoadEligibleLinkAsync(link.Id, ct);
            if (eligible.AccessStatus != ProjectGitRepositoryAccessStatus.Active)
                throw Error("GITHUB_REPOSITORY_NOT_AUTHORIZED", "Repository access changed during synchronization.", 409);

            foreach (var item in result.Items)
                await PersistItemAsync(eligible, category, item, observedAt,
                    scope, result.RateLimitRemaining, result.RateLimitResetAt, ct);

            var currentCheckpoint = await db.GitSyncCheckpoints.SingleOrDefaultAsync(
                x => x.SyncJobId == jobId && x.Category == category && x.ScopeKey == checkpointScope, ct);
            if (currentCheckpoint is null)
            {
                currentCheckpoint = new GitSyncCheckpoint
                {
                    SyncJobId = jobId,
                    Category = category,
                    ScopeKey = checkpointScope
                };
                db.GitSyncCheckpoints.Add(currentCheckpoint);
            }
            currentCheckpoint.Cursor = result.HasMore
                ? (pageNumber + 1).ToString(CultureInfo.InvariantCulture)
                : pageNumber.ToString(CultureInfo.InvariantCulture);
            currentCheckpoint.Status = result.HasMore
                ? GitSyncCheckpointStatus.InProgress
                : GitSyncCheckpointStatus.Completed;
            currentCheckpoint.ItemsProcessed += result.Items.Count;
            currentCheckpoint.LastProcessedAt = observedAt;
            currentCheckpoint.UpdatedAt = observedAt;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            if (!result.HasMore) return;
            pageNumber++;
        }
    }

    private async Task PersistItemAsync(
        EligibleLink link, string category, JsonElement payload,
        DateTimeOffset observedAt, string? scope, int? rateLimitRemaining,
        DateTimeOffset? rateLimitResetAt, CancellationToken ct)
    {
        if (!TryGetObjectId(category, payload, out var externalId)) return;
        var occurredAt = GetOccurredAt(category, payload);
        var providerObject = db.GitHubProviderObjects.Local.FirstOrDefault(x =>
            x.GitHubRepositoryId == link.GitHubRepositoryId &&
            x.Category == category && x.ExternalObjectId == externalId)
            ?? await db.GitHubProviderObjects.SingleOrDefaultAsync(x =>
            x.GitHubRepositoryId == link.GitHubRepositoryId &&
            x.Category == category && x.ExternalObjectId == externalId, ct);
        var retrievalMetadata = JsonSerializer.Serialize(new
        {
            observedAt,
            rateLimitRemaining,
            rateLimitResetAt
        });
        if (providerObject is null)
        {
            providerObject = new GitHubProviderObject
            {
                GitHubRepositoryId = link.GitHubRepositoryId,
                Category = category,
                ExternalObjectId = externalId,
                OccurredAt = occurredAt,
                FirstObservedAt = observedAt,
                LastObservedAt = observedAt,
                PayloadJson = payload.GetRawText(),
                RetrievalMetadataJson = retrievalMetadata
            };
            db.GitHubProviderObjects.Add(providerObject);
        }
        else
        {
            providerObject.OccurredAt = occurredAt ?? providerObject.OccurredAt;
            providerObject.LastObservedAt = observedAt;
            providerObject.PayloadJson = payload.GetRawText();
            providerObject.RetrievalMetadataJson = retrievalMetadata;
        }
        if (category == "commits" && !string.IsNullOrWhiteSpace(scope))
        {
            var branchReferenceExists = db.GitCommitBranchReferences.Local.Any(x =>
                x.GitHubProviderObjectId == providerObject.Id && x.BranchName == scope) ||
                await db.GitCommitBranchReferences.AnyAsync(x =>
                    x.GitHubProviderObjectId == providerObject.Id && x.BranchName == scope, ct);
            if (!branchReferenceExists)
                db.GitCommitBranchReferences.Add(new GitCommitBranchReference
                {
                    GitHubProviderObjectId = providerObject.Id,
                    BranchName = scope,
                    FirstObservedAt = observedAt
                });
        }

        var activityDefinitions = GetActivities(category, payload, occurredAt ?? observedAt);
        foreach (var activity in activityDefinitions)
        {
            var normalized = db.GitActivities.Local.FirstOrDefault(x =>
                x.GitHubProviderObjectId == providerObject.Id && x.ActivityType == activity.Type)
                ?? await db.GitActivities.SingleOrDefaultAsync(x =>
                x.GitHubProviderObjectId == providerObject.Id && x.ActivityType == activity.Type, ct);
            var created = normalized is null;
            if (normalized is null)
            {
                normalized = new GitActivity
                {
                    GitHubProviderObjectId = providerObject.Id,
                    ActivityType = activity.Type,
                    OccurredAt = activity.OccurredAt,
                    MetadataJson = GetActivityMetadata(category, payload)
                };
                db.GitActivities.Add(normalized);
            }
            if (created)
            {
                foreach (var actor in activity.Actors)
                {
                    var actorKey = GetActorKey(actor);
                    var existingActor = db.GitActivityActors.Local.Any(x =>
                        x.GitActivityId == normalized.Id && x.ActorKey == actorKey) ||
                        await db.GitActivityActors.AnyAsync(x =>
                            x.GitActivityId == normalized.Id && x.ActorKey == actorKey, ct);
                    if (!existingActor)
                        db.GitActivityActors.Add(new GitActivityActor
                        {
                            GitActivityId = normalized.Id,
                            Role = actor.Role,
                            ActorKey = actorKey,
                            ExternalAccountId = actor.ExternalId,
                            Login = actor.Login
                        });
                }
            }

            var hasActivityVisibility = db.ProjectGitActivityVisibilities.Local.Any(x =>
                x.ProjectGitRepositoryId == link.Id && x.GitActivityId == normalized.Id) ||
                await db.ProjectGitActivityVisibilities.AnyAsync(x =>
                    x.ProjectGitRepositoryId == link.Id && x.GitActivityId == normalized.Id, ct);
            if (!hasActivityVisibility)
                db.ProjectGitActivityVisibilities.Add(new ProjectGitActivityVisibility
                {
                    ProjectGitRepositoryId = link.Id,
                    GitActivityId = normalized.Id,
                    ObservedAt = observedAt
                });
        }

        var hasGrant = db.ProjectGitActivityGrants.Local.Any(x =>
            x.ProjectGitRepositoryId == link.Id && x.GitHubProviderObjectId == providerObject.Id) ||
            await db.ProjectGitActivityGrants.AnyAsync(x =>
                x.ProjectGitRepositoryId == link.Id &&
                x.GitHubProviderObjectId == providerObject.Id, ct);
        if (!hasGrant)
            db.ProjectGitActivityGrants.Add(new ProjectGitActivityGrant
            {
                ProjectGitRepositoryId = link.Id,
                GitHubProviderObjectId = providerObject.Id,
                ObservedAt = observedAt
            });
    }

    private async Task<IReadOnlyList<string>> ReadObjectScopesAsync(
        Guid linkId, Guid repositoryId, string category, CancellationToken ct)
    {
        var payloads = await db.GitHubProviderObjects.AsNoTracking()
            .Where(x => x.GitHubRepositoryId == repositoryId && x.Category == category &&
                db.ProjectGitActivityGrants.Any(grant =>
                    grant.GitHubProviderObjectId == x.Id && grant.ProjectGitRepositoryId == linkId))
            .Select(x => x.PayloadJson).ToListAsync(ct);
        return payloads.Select(payload =>
            {
                using var document = JsonDocument.Parse(payload);
                return document.RootElement.TryGetProperty("number", out var number)
                    ? number.ToString()
                    : null;
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<EligibleLink> LoadEligibleLinkAsync(
        Guid linkId, CancellationToken ct, bool includeRecoverable = false) =>
        await (
            from link in db.ProjectGitRepositories.AsNoTracking()
            join repository in db.GitHubRepositories.AsNoTracking()
                on link.GitHubRepositoryId equals repository.Id
            join connection in db.GitHubOAuthConnections.AsNoTracking()
                on link.OAuthConnectionId equals connection.Id
            where link.Id == linkId && link.UnlinkedAt == null &&
                !link.SyncPaused &&
                (link.AccessStatus == ProjectGitRepositoryAccessStatus.Active ||
                 includeRecoverable &&
                    (link.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization ||
                     link.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost)) &&
                connection.Status == GitHubOAuthConnectionStatus.Active
            select new EligibleLink(
                link.Id, link.GitHubRepositoryId, link.OAuthConnectionId,
                link.LinkedByProfileId, connection.AccountId, repository.ProviderRepositoryId, repository.FullName,
                link.AccessStatus))
            .SingleOrDefaultAsync(ct)
        ?? throw Error("GITHUB_REPOSITORY_NOT_AUTHORIZED", "Repository association is no longer eligible for synchronization.", 409);

    private async Task MarkAccessLostAsync(EligibleLink link, CancellationToken ct)
    {
        var association = await db.ProjectGitRepositories.SingleOrDefaultAsync(
            x => x.Id == link.Id && x.UnlinkedAt == null, ct);
        if (association is null) return;
        if (association.OAuthConnectionId != link.OAuthConnectionId)
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed during synchronization.", 409);
        if (association.AccessStatus != link.AccessStatus)
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed during synchronization.", 409);
        if (association.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost) return;
        var connectionIsActive = await db.GitHubOAuthConnections.AsNoTracking().AnyAsync(x =>
            x.Id == link.OAuthConnectionId && x.Status == GitHubOAuthConnectionStatus.Active, ct);
        if (!connectionIsActive)
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "OAuth authorization changed during synchronization.", 409);
        var now = DateTimeOffset.UtcNow;
        association.AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost;
        association.LastAccessCheckedAt = now;
        association.UpdatedAt = now;
        association.Version = Guid.NewGuid();
        db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
        {
            ProjectGitRepositoryId = association.Id,
            ActorProfileId = association.LinkedByProfileId,
            Action = ProjectGitRepositoryHistoryAction.AccessLost,
            PreviousOAuthConnectionId = association.OAuthConnectionId,
            NewOAuthConnectionId = association.OAuthConnectionId,
            Details = "GitHub denied access to this repository."
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<EligibleLink> ReconcileAccessAsync(EligibleLink link, CancellationToken ct)
    {
        var accessToken = await credentials.GetValidAccessTokenAsync(
            link.AccountId, link.OAuthConnectionId, link.LinkedByProfileId, ct);
        GitHubRemoteRepository remote;
        try
        {
            remote = await provider.GetRepositoryAsync(accessToken, link.ProviderRepositoryId, ct);
        }
        catch (GitHubIntegrationException exception) when (exception.Code == "GITHUB_CREDENTIAL_INVALID")
        {
            await credentials.MarkReauthorizationRequiredAsync(
                link.OAuthConnectionId, link.LinkedByProfileId, ct);
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect the selected GitHub account before synchronizing.", 409);
        }
        catch (GitHubIntegrationException exception) when (exception.Code == "GITHUB_ACCESS_DENIED")
        {
            await MarkAccessLostAsync(link, ct);
            throw Error("GITHUB_REPOSITORY_ACCESS_LOST", "GitHub denied access to this repository.", 409);
        }

        if (remote.Id != link.ProviderRepositoryId)
            throw Error("GITHUB_INVALID_RESPONSE", "GitHub returned a different repository than requested.", 502);
        if (!remote.CanRead)
        {
            await MarkAccessLostAsync(link, ct);
            throw Error("GITHUB_REPOSITORY_ACCESS_LOST", "GitHub denied access to this repository.", 409);
        }

        var association = await db.ProjectGitRepositories.SingleOrDefaultAsync(
            x => x.Id == link.Id && x.UnlinkedAt == null, ct)
            ?? throw Error("GITHUB_REPOSITORY_NOT_AUTHORIZED", "Repository association is no longer available.", 409);
        var connectionIsActive = await db.GitHubOAuthConnections.AsNoTracking().AnyAsync(x =>
            x.Id == link.OAuthConnectionId && x.Status == GitHubOAuthConnectionStatus.Active, ct);
        if (association.OAuthConnectionId != link.OAuthConnectionId || association.SyncPaused ||
            association.AccessStatus != link.AccessStatus || !connectionIsActive)
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed during reconciliation.", 409);

        var repository = await db.GitHubRepositories.SingleAsync(x => x.Id == link.GitHubRepositoryId, ct);
        var metadataChanged = repository.ProviderOwnerId != remote.OwnerId ||
            repository.OwnerLogin != remote.OwnerLogin || repository.Name != remote.Name ||
            repository.FullName != remote.FullName || repository.HtmlUrl != remote.HtmlUrl ||
            repository.Description != remote.Description || repository.DefaultBranch != remote.DefaultBranch ||
            repository.IsPrivate != remote.IsPrivate || repository.IsArchived != remote.IsArchived;
        repository.ProviderOwnerId = remote.OwnerId;
        repository.OwnerLogin = remote.OwnerLogin;
        repository.Name = remote.Name;
        repository.FullName = remote.FullName;
        repository.HtmlUrl = remote.HtmlUrl;
        repository.Description = remote.Description;
        repository.DefaultBranch = remote.DefaultBranch;
        repository.IsPrivate = remote.IsPrivate;
        repository.IsArchived = remote.IsArchived;
        repository.LastVerifiedAt = DateTimeOffset.UtcNow;
        repository.UpdatedAt = repository.LastVerifiedAt;
        var previousStatus = association.AccessStatus;
        association.AccessStatus = ProjectGitRepositoryAccessStatus.Active;
        association.LastAccessCheckedAt = repository.LastVerifiedAt;
        association.UpdatedAt = repository.LastVerifiedAt;
        association.Version = Guid.NewGuid();
        if (previousStatus != ProjectGitRepositoryAccessStatus.Active)
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
            {
                ProjectGitRepositoryId = association.Id,
                ActorProfileId = association.LinkedByProfileId,
                Action = ProjectGitRepositoryHistoryAction.AccessRestored,
                PreviousOAuthConnectionId = association.OAuthConnectionId,
                NewOAuthConnectionId = association.OAuthConnectionId,
                Details = $"Repository access verified as {remote.FullName}."
            });
        if (metadataChanged)
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
            {
                ProjectGitRepositoryId = association.Id,
                ActorProfileId = association.LinkedByProfileId,
                Action = ProjectGitRepositoryHistoryAction.MetadataChanged,
                Details = $"Repository metadata updated to {remote.FullName}."
            });
        await db.SaveChangesAsync(ct);
        return link with { FullName = remote.FullName, AccessStatus = ProjectGitRepositoryAccessStatus.Active };
    }

    private static bool TryGetObjectId(string category, JsonElement payload, out string id)
    {
        id = "";
        var propertyName = category == "branches" ? "name" :
            category == "commits" ? "sha" :
            category is "pull-requests" or "issues" ? "id" : "id";
        if (!payload.TryGetProperty(propertyName, out var value)) return false;
        id = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();
        return !string.IsNullOrWhiteSpace(id) && id.Length <= 255;
    }

    private static DateTimeOffset? GetOccurredAt(string category, JsonElement payload)
    {
        string? value = category switch
        {
            "commits" => ReadString(payload, "commit", "committer", "date")
                ?? ReadString(payload, "commit", "author", "date"),
            "pull-request-reviews" => ReadString(payload, "submitted_at"),
            "issue-comments" or "pull-request-review-comments" => ReadString(payload, "created_at"),
            "releases" => ReadString(payload, "published_at") ?? ReadString(payload, "created_at"),
            _ => ReadString(payload, "updated_at") ?? ReadString(payload, "created_at")
        };
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var occurredAt)
            ? occurredAt
            : null;
    }

    private static IReadOnlyList<ActivityDefinition> GetActivities(
        string category, JsonElement payload, DateTimeOffset occurredAt)
    {
        var actors = new List<ActivityActor>();
        AddActor(payload, "author", "author", actors);
        AddActor(payload, "committer", "committer", actors);
        AddActor(payload, "user", category.StartsWith("pull-request", StringComparison.Ordinal)
            ? "reviewer"
            : "actor", actors);
        if (category == "commits")
            AddCoAuthors(payload, actors);
        var types = category switch
        {
            "branches" => new[] { "branch.observed" },
            "commits" => new[] { "commit.created" },
            "pull-requests" => GetPullRequestTypes(payload),
            "pull-request-reviews" => new[] { "pull_request.reviewed" },
            "issues" when payload.TryGetProperty("pull_request", out _) => [],
            "issues" => GetIssueTypes(payload),
            "issue-comments" => new[] { "issue.comment.created" },
            "pull-request-review-comments" => new[] { "pull_request.review_comment.created" },
            "releases" => new[] { "release.published" },
            _ => []
        };
        var distinctActors = actors.DistinctBy(GetActorKey).ToArray();
        return types.Select(type => new ActivityDefinition(type, occurredAt, distinctActors)).ToArray();
    }

    private static string GetActivityMetadata(string category, JsonElement payload)
    {
        var names = category switch
        {
            "commits" => new[] { "commit" },
            "pull-requests" => new[] { "number", "state", "merged", "merged_at", "merge_commit_sha", "head", "base" },
            "pull-request-reviews" => new[] { "state", "submitted_at", "commit_id" },
            "issues" => new[] { "number", "state", "closed_at" },
            "issue-comments" or "pull-request-review-comments" =>
                new[] { "issue_url", "pull_request_url", "path", "commit_id", "in_reply_to_id" },
            "releases" => new[] { "tag_name", "name", "draft", "prerelease", "published_at" },
            _ => new[] { "name" }
        };
        var metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in names)
            if (payload.TryGetProperty(name, out var value))
                metadata[name] = value.Clone();
        return JsonSerializer.Serialize(metadata);
    }

    private static string GetActorKey(ActivityActor actor) =>
        actor.ExternalId is not null
            ? $"{actor.Role}:id:{actor.ExternalId}"
            : $"{actor.Role}:login:{actor.Login}";

    private static void AddCoAuthors(JsonElement payload, ICollection<ActivityActor> actors)
    {
        var message = ReadString(payload, "commit", "message");
        if (message is null) return;
        foreach (var line in message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Co-authored-by:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var identity = line[prefix.Length..].Trim();
            var separator = identity.IndexOf('<');
            var name = (separator >= 0 ? identity[..separator] : identity).Trim();
            if (name.Length > 0) actors.Add(new("co-author", null, name[..Math.Min(name.Length, 255)]));
        }
    }

    private static string[] GetPullRequestTypes(JsonElement payload)
    {
        var types = new List<string> { "pull_request.opened" };
        if (ReadString(payload, "merged_at") is not null) types.Add("pull_request.merged");
        return types.ToArray();
    }

    private static string[] GetIssueTypes(JsonElement payload)
    {
        var types = new List<string> { "issue.opened" };
        if (ReadString(payload, "closed_at") is not null) types.Add("issue.closed");
        return types.ToArray();
    }

    private static string? ReadString(JsonElement payload, params string[] path)
    {
        foreach (var segment in path)
        {
            if (!payload.TryGetProperty(segment, out payload)) return null;
        }
        return payload.ValueKind == JsonValueKind.String ? payload.GetString() : null;
    }

    private static void AddActor(
        JsonElement payload, string property, string role, ICollection<ActivityActor> actors)
    {
        if (!payload.TryGetProperty(property, out var actor) || actor.ValueKind != JsonValueKind.Object) return;
        var externalId = actor.TryGetProperty("id", out var id) ? id.ToString() : null;
        var login = ReadString(actor, "login");
        if (externalId is null && login is null) return;
        actors.Add(new(role, externalId, login));
    }

    private static GitHubIntegrationException Error(string code, string message, int status) =>
        new(code, message, status);

    private sealed record EligibleLink(
        Guid Id, Guid GitHubRepositoryId, Guid OAuthConnectionId, Guid LinkedByProfileId,
        Guid AccountId, long ProviderRepositoryId, string FullName, ProjectGitRepositoryAccessStatus AccessStatus);
    private sealed record ActivityActor(string Role, string? ExternalId, string? Login);
    private sealed record ActivityDefinition(
        string Type, DateTimeOffset OccurredAt, IReadOnlyList<ActivityActor> Actors);
}
