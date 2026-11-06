using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectGitRepositoryService(
    UniNetDbContext db,
    IGitHubProviderClient provider,
    GitHubCredentialService credentials,
    IOptions<GitSyncOptions>? syncOptions = null) : IProjectGitRepositoryService
{
    public async Task<GitHubRepositoryPage> DiscoverAsync(
        Guid accountId, Guid projectId, Guid connectionId, int page, int pageSize, CancellationToken ct)
    {
        var actorId = await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        if (page is < 1 or > 1000 || pageSize is < 1 or > 100)
            throw Error("INVALID_PAGINATION", "Page must be between 1 and 1000 and page size between 1 and 100.", 400);
        var accessToken = await GetAccessTokenAsync(accountId, connectionId, actorId, ct);
        var repositories = await CallProviderAsync(connectionId, actorId,
            () => provider.GetRepositoriesAsync(accessToken, page, pageSize, ct), ct);
        return new(repositories.Where(x => x.CanRead).Select(ToResponse).ToArray(), page, pageSize);
    }

    public async Task<ProjectGitRepositoryResponse> LinkAsync(
        Guid accountId, Guid projectId, LinkGitHubRepositoryRequest request, CancellationToken ct)
    {
        var actorId = await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        if (request.ProviderRepositoryId <= 0 || request.OAuthConnectionId == Guid.Empty)
            throw Error("INVALID_GITHUB_REPOSITORY", "A valid repository ID and OAuth connection are required.", 400);
        var accessToken = await GetAccessTokenAsync(accountId, request.OAuthConnectionId, actorId, ct);
        var remote = await GetReadableRepositoryAsync(request.OAuthConnectionId, actorId,
            accessToken, request.ProviderRepositoryId, ct);
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var knownRepositoryId = await db.GitHubRepositories.AsNoTracking()
            .Where(x => x.ProviderRepositoryId == remote.Id)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);
        if (knownRepositoryId is { } existingRepositoryId &&
            await db.ProjectGitRepositories.AnyAsync(x => x.ProjectId == projectId &&
                x.GitHubRepositoryId == existingRepositoryId && x.UnlinkedAt == null, ct))
            throw Error("GITHUB_REPOSITORY_ALREADY_LINKED", "This repository is already linked to the project.", 409);

        var repository = await db.GitHubRepositories
            .SingleOrDefaultAsync(x => x.ProviderRepositoryId == remote.Id, ct);
        var previousFullName = repository?.FullName;
        var previousOwnerLogin = repository?.OwnerLogin;
        var metadataChanged = repository is not null && MetadataChanged(repository, remote);
        if (repository is null)
        {
            repository = new GitHubRepository
            {
                ProviderRepositoryId = remote.Id,
                ProviderOwnerId = remote.OwnerId,
                OwnerLogin = remote.OwnerLogin,
                Name = remote.Name,
                FullName = remote.FullName,
                HtmlUrl = remote.HtmlUrl,
                Description = remote.Description,
                DefaultBranch = remote.DefaultBranch,
                IsPrivate = remote.IsPrivate,
                IsArchived = remote.IsArchived,
                LastVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.GitHubRepositories.Add(repository);
        }
        else
        {
            UpdateMetadata(repository, remote, now);
        }

        var link = new ProjectGitRepository
        {
            ProjectId = projectId,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = request.OAuthConnectionId,
            LinkedByProfileId = actorId,
            AccessStatus = ProjectGitRepositoryAccessStatus.Active,
            SyncIntervalHours = syncOptions?.Value.DefaultIntervalHours is > 0
                ? syncOptions.Value.DefaultIntervalHours
                : 6,
            LastAccessCheckedAt = now,
            LinkedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ProjectGitRepositories.Add(link);
        db.GitSyncJobs.Add(new GitSyncJob
        {
            ProjectGitRepositoryId = link.Id,
            GitHubRepositoryId = repository.Id,
            OAuthConnectionId = request.OAuthConnectionId,
            RequestedByProfileId = actorId,
            SyncType = GitSyncType.Initial,
            Status = GitSyncJobStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
        {
            ProjectGitRepositoryId = link.Id,
            ActorProfileId = actorId,
            Action = ProjectGitRepositoryHistoryAction.Linked,
            NewOAuthConnectionId = request.OAuthConnectionId,
            Details = $"Linked GitHub repository {remote.FullName}."
        });
        if (metadataChanged)
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
            {
                ProjectGitRepositoryId = link.Id,
                ActorProfileId = actorId,
                Action = ProjectGitRepositoryHistoryAction.MetadataChanged,
                Details = $"Repository metadata changed from {previousFullName} ({previousOwnerLogin}) to {remote.FullName} ({remote.OwnerLogin})."
            });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw Error("GITHUB_LINK_CONFLICT", "Repository integration changed concurrently. Retry the request.", 409);
        }
        return ToResponse(link, repository);
    }

    public async Task<ProjectGitRepositoryPage> GetAsync(
        Guid accountId, Guid projectId, int page, int pageSize, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        if (page is < 1 or > 1000 || pageSize is < 1 or > 100)
            throw Error("INVALID_PAGINATION", "Page must be between 1 and 1000 and page size between 1 and 100.", 400);
        var query = db.ProjectGitRepositories.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.UnlinkedAt == null)
            .Join(db.GitHubRepositories.AsNoTracking(), x => x.GitHubRepositoryId, r => r.Id,
                (link, repository) => new { Link = link, Repository = repository });
        var total = await query.CountAsync(ct);
        var links = await query.OrderBy(x => x.Repository.FullName).ThenBy(x => x.Link.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(links.Select(x => ToResponse(x.Link, x.Repository)).ToArray(), page, pageSize, total);
    }

    public async Task<ProjectGitRepositoryResponse> GetAsync(
        Guid accountId, Guid projectId, Guid linkId, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        var row = await db.ProjectGitRepositories.AsNoTracking()
            .Where(x => x.Id == linkId && x.ProjectId == projectId && x.UnlinkedAt == null)
            .Join(db.GitHubRepositories.AsNoTracking(), x => x.GitHubRepositoryId, r => r.Id,
                (link, repository) => new { Link = link, Repository = repository })
            .SingleOrDefaultAsync(ct)
            ?? throw Error("GITHUB_REPOSITORY_LINK_NOT_FOUND", "Linked repository not found.", 404);
        return ToResponse(row.Link, row.Repository);
    }

    public async Task<ProjectGitRepositoryResponse> ReplaceCredentialAsync(
        Guid accountId, Guid projectId, Guid linkId, ReplaceGitHubRepositoryCredentialRequest request,
        CancellationToken ct)
    {
        var actorId = await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        if (request.OAuthConnectionId == Guid.Empty)
            throw Error("INVALID_GITHUB_CONNECTION", "A valid OAuth connection is required.", 400);
        var repositoryId = await db.ProjectGitRepositories.AsNoTracking()
            .Where(x => x.Id == linkId && x.ProjectId == projectId && x.UnlinkedAt == null)
            .Select(x => (Guid?)x.GitHubRepositoryId)
            .SingleOrDefaultAsync(ct)
            ?? throw Error("GITHUB_REPOSITORY_LINK_NOT_FOUND", "Linked repository not found.", 404);
        var accessToken = await GetAccessTokenAsync(accountId, request.OAuthConnectionId, actorId, ct);
        var remote = await GetReadableRepositoryAsync(request.OAuthConnectionId, actorId, accessToken,
            (await db.GitHubRepositories.AsNoTracking().SingleAsync(x => x.Id == repositoryId, ct))
            .ProviderRepositoryId, ct);

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var link = await db.ProjectGitRepositories
            .SingleOrDefaultAsync(x => x.Id == linkId && x.ProjectId == projectId && x.UnlinkedAt == null, ct)
            ?? throw Error("GITHUB_REPOSITORY_LINK_NOT_FOUND", "Linked repository not found.", 404);
        if (link.OAuthConnectionId == request.OAuthConnectionId)
            throw Error("SAME_GITHUB_CREDENTIAL", "Select a different GitHub connection to replace the credential.", 409);
        var oldConnectionId = link.OAuthConnectionId;
        var repository = await db.GitHubRepositories.SingleAsync(x => x.Id == repositoryId, ct);
        var previousFullName = repository.FullName;
        var previousOwnerLogin = repository.OwnerLogin;
        var metadataChanged = MetadataChanged(repository, remote);
        UpdateMetadata(repository, remote, now);
        link.OAuthConnectionId = request.OAuthConnectionId;
        link.AccessStatus = ProjectGitRepositoryAccessStatus.Active;
        link.LastAccessCheckedAt = now;
        link.UpdatedAt = now;
        link.Version = Guid.NewGuid();
        db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
        {
            ProjectGitRepositoryId = link.Id,
            ActorProfileId = actorId,
            Action = ProjectGitRepositoryHistoryAction.CredentialReplaced,
            PreviousOAuthConnectionId = oldConnectionId,
            NewOAuthConnectionId = request.OAuthConnectionId,
            Details = $"Repository access verified as {remote.FullName}.",
            CreatedAt = now
        });
        if (metadataChanged)
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
            {
                ProjectGitRepositoryId = link.Id,
                ActorProfileId = actorId,
                Action = ProjectGitRepositoryHistoryAction.MetadataChanged,
                Details = $"Repository metadata changed from {previousFullName} ({previousOwnerLogin}) to {remote.FullName} ({remote.OwnerLogin})."
            });
        var linkJobs = await db.GitSyncJobs
            .Where(x => x.ProjectGitRepositoryId == link.Id &&
                (x.Status == GitSyncJobStatus.Pending || x.Status == GitSyncJobStatus.Running ||
                 x.Status == GitSyncJobStatus.RetryScheduled))
            .ToListAsync(ct);
        var activeJob = linkJobs.OrderBy(x => x.CreatedAt).FirstOrDefault();
        if (activeJob is null)
        {
            db.GitSyncJobs.Add(new GitSyncJob
            {
                ProjectGitRepositoryId = link.Id,
                GitHubRepositoryId = repository.Id,
                OAuthConnectionId = request.OAuthConnectionId,
                RequestedByProfileId = actorId,
                SyncType = GitSyncType.Initial,
                Status = GitSyncJobStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else if (activeJob.Status != GitSyncJobStatus.Running)
        {
            activeJob.OAuthConnectionId = request.OAuthConnectionId;
            activeJob.RequestedByProfileId = actorId;
            activeJob.SyncType = GitSyncType.Initial;
            activeJob.Status = GitSyncJobStatus.Pending;
            activeJob.RetryCount = 0;
            activeJob.NextAttemptAt = null;
            activeJob.ErrorCode = null;
            activeJob.ErrorMessage = null;
            activeJob.StartedAt = null;
            activeJob.CompletedAt = null;
            activeJob.LeaseId = null;
            activeJob.LeaseExpiresAt = null;
            activeJob.LastHeartbeatAt = null;
            activeJob.UpdatedAt = now;
            var checkpoints = await db.GitSyncCheckpoints
                .Where(x => x.SyncJobId == activeJob.Id).ToListAsync(ct);
            db.GitSyncCheckpoints.RemoveRange(checkpoints);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error("GITHUB_LINK_CONFLICT", "Repository integration changed concurrently. Retry the request.", 409);
        }
        await transaction.CommitAsync(ct);
        return ToResponse(link, repository);
    }

    public async Task UnlinkAsync(Guid accountId, Guid projectId, Guid linkId, CancellationToken ct)
    {
        var actorId = await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var link = await db.ProjectGitRepositories
            .SingleOrDefaultAsync(x => x.Id == linkId && x.ProjectId == projectId && x.UnlinkedAt == null, ct)
            ?? throw Error("GITHUB_REPOSITORY_LINK_NOT_FOUND", "Linked repository not found.", 404);
        var repository = await db.GitHubRepositories.AsNoTracking()
            .SingleAsync(x => x.Id == link.GitHubRepositoryId, ct);
        var now = DateTimeOffset.UtcNow;
        link.UnlinkedAt = now;
        link.UpdatedAt = now;
        link.Version = Guid.NewGuid();
        db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
        {
            ProjectGitRepositoryId = link.Id,
            ActorProfileId = actorId,
            Action = ProjectGitRepositoryHistoryAction.Unlinked,
            PreviousOAuthConnectionId = link.OAuthConnectionId,
            Details = $"Unlinked GitHub repository {repository.FullName}."
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error("GITHUB_LINK_CONFLICT", "Repository integration changed concurrently. Retry the request.", 409);
        }
        await transaction.CommitAsync(ct);
    }

    private async Task<string> GetAccessTokenAsync(
        Guid accountId, Guid connectionId, Guid actorId, CancellationToken ct)
    {
        try
        {
            return await credentials.GetValidAccessTokenAsync(accountId, connectionId, actorId, ct);
        }
        catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_CREDENTIAL_INVALID")
        {
            await credentials.MarkReauthorizationRequiredAsync(connectionId, actorId, ct);
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect this GitHub account before using it.", 409);
        }
    }

    private async Task<GitHubRemoteRepository> GetReadableRepositoryAsync(
        Guid connectionId, Guid actorId, string accessToken, long repositoryId, CancellationToken ct)
    {
        var repository = await CallProviderAsync(connectionId, actorId,
            () => provider.GetRepositoryAsync(accessToken, repositoryId, ct), ct);
        if (repository.Id != repositoryId)
            throw Error("GITHUB_INVALID_RESPONSE", "GitHub returned a different repository than requested.", 502);
        if (!repository.CanRead)
            throw Error("GITHUB_REPOSITORY_READ_ACCESS_REQUIRED",
                "The selected GitHub connection does not have repository read access.", 403);
        return repository;
    }

    private async Task<T> CallProviderAsync<T>(
        Guid connectionId, Guid actorId, Func<Task<T>> request, CancellationToken ct)
    {
        try
        {
            return await request();
        }
        catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_CREDENTIAL_INVALID")
        {
            await credentials.MarkReauthorizationRequiredAsync(connectionId, actorId, ct);
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect this GitHub account before using it.", 409);
        }
    }

    private static bool MetadataChanged(GitHubRepository repository, GitHubRemoteRepository remote) =>
        repository.ProviderOwnerId != remote.OwnerId || repository.OwnerLogin != remote.OwnerLogin ||
        repository.Name != remote.Name || repository.FullName != remote.FullName ||
        repository.HtmlUrl != remote.HtmlUrl || repository.Description != remote.Description ||
        repository.DefaultBranch != remote.DefaultBranch || repository.IsPrivate != remote.IsPrivate ||
        repository.IsArchived != remote.IsArchived;

    private static void UpdateMetadata(GitHubRepository repository, GitHubRemoteRepository remote, DateTimeOffset now)
    {
        repository.ProviderOwnerId = remote.OwnerId;
        repository.OwnerLogin = remote.OwnerLogin;
        repository.Name = remote.Name;
        repository.FullName = remote.FullName;
        repository.HtmlUrl = remote.HtmlUrl;
        repository.Description = remote.Description;
        repository.DefaultBranch = remote.DefaultBranch;
        repository.IsPrivate = remote.IsPrivate;
        repository.IsArchived = remote.IsArchived;
        repository.LastVerifiedAt = now;
        repository.UpdatedAt = now;
    }

    private static GitHubRepositoryResponse ToResponse(GitHubRemoteRepository repository) =>
        new(repository.Id, repository.OwnerLogin, repository.Name, repository.FullName, repository.HtmlUrl,
            repository.Description, repository.DefaultBranch, repository.IsPrivate, repository.IsArchived);

    private static ProjectGitRepositoryResponse ToResponse(ProjectGitRepository link, GitHubRepository repository) =>
        new(link.Id, link.ProjectId, new(repository.ProviderRepositoryId, repository.OwnerLogin, repository.Name,
                repository.FullName, repository.HtmlUrl, repository.Description, repository.DefaultBranch,
                repository.IsPrivate, repository.IsArchived),
            link.AccessStatus, link.SyncPaused, link.OAuthConnectionId, link.LinkedAt, link.UnlinkedAt,
            link.LastAccessCheckedAt);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
