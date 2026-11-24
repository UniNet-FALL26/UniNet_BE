using System.Globalization;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
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
    IOptions<GitSyncOptions>? syncOptions = null,
    IGitHubAppInstallationTokenProvider? appTokens = null,
    IOptions<GitHubAppOptions>? appOptions = null) : IGitSyncJobProcessor
{
    private int PageSize => Math.Clamp(syncOptions?.Value.BatchSize ?? 100, 1, 100);

    public async Task ProcessAsync(GitSyncJob job, CancellationToken ct)
    {
        if (job.CredentialSource == GitRepositoryCredentialSource.GitHubApp) GitHubAppAvailability.Require(appOptions);
        if (db.Database.IsNpgsql())
        {
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToHashSet(StringComparer.Ordinal);
            if (!applied.Contains("20261113165539_AddGitEvidenceVersioning") ||
                !applied.Contains("20261113232726_HardenGitEvidenceCrossRepositoryIntegrity") ||
                !applied.Contains("20261114070301_AddAppAuthorizationGeneration"))
                throw Error("GITHUB_SYNC_EVIDENCE_SCHEMA_REQUIRED", "Git synchronization requires the evidence schema migrations.", 503);
        }
        if (db.Database.IsNpgsql() && job.LeaseId is null)
            throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job has no active lease.", 409);
        if (job.LeaseId is { } initialLease) await RequireLeaseAsync(job.Id, initialLease, false, ct);
        if (await db.ProjectGitRepositories.AsNoTracking().AnyAsync(x => x.Id == job.ProjectGitRepositoryId &&
            x.CredentialSource == GitRepositoryCredentialSource.GitHubApp && x.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization, ct))
            throw Error("GITHUB_APP_PENDING_ACTIVATION", "GitHub App synchronization is not active yet.", 409);
        if (job.CredentialSource == GitRepositoryCredentialSource.OAuth && (job.GitHubAppRepositoryGrantId is not null || job.OAuthConnectionId is null) ||
            job.CredentialSource == GitRepositoryCredentialSource.GitHubApp && (job.OAuthConnectionId is not null || job.GitHubAppRepositoryGrantId is null || job.SyncType is not (GitSyncType.Initial or GitSyncType.Manual or GitSyncType.Scheduled or GitSyncType.Webhook)) ||
            job.CredentialSource is not (GitRepositoryCredentialSource.OAuth or GitRepositoryCredentialSource.GitHubApp))
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Job credential source does not match the repository association.", 409);
        var link = await LoadEligibleLinkAsync(job.ProjectGitRepositoryId, ct,
            includeRecoverable: job.SyncType == GitSyncType.Scheduled);
        if (link.GitHubRepositoryId != job.GitHubRepositoryId || link.Source != job.CredentialSource ||
            (link.Source == GitRepositoryCredentialSource.OAuth && link.OAuthConnectionId != job.OAuthConnectionId) ||
            (link.Source == GitRepositoryCredentialSource.GitHubApp && link.AppGrantId != job.GitHubAppRepositoryGrantId))
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed before synchronization.", 409);
        if (link.AccessStatus != ProjectGitRepositoryAccessStatus.Active)
            link = await ReconcileAccessAsync(link, ct);

        await ProcessCategoryAsync(job.Id, job.LeaseId, link, "branches", null, ct);
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
                await ProcessCategoryAsync(job.Id, job.LeaseId, link, "commits", name.GetString(), ct);
        }

        await ProcessCategoryAsync(job.Id, job.LeaseId, link, "pull-requests", null, ct);
        var pullRequests = await ReadObjectScopesAsync(link.Id, link.GitHubRepositoryId, "pull-requests", ct);
        foreach (var number in pullRequests)
        {
            await ProcessCategoryAsync(job.Id, job.LeaseId, link, "pull-request-commits", number, ct);
            await ProcessCategoryAsync(job.Id, job.LeaseId, link, "pull-request-files", number, ct);
            await ProcessCategoryAsync(job.Id, job.LeaseId, link, "pull-request-reviews", number, ct);
            await ProcessCategoryAsync(job.Id, job.LeaseId, link, "pull-request-review-comments", number, ct);
        }

        await ProcessCategoryAsync(job.Id, job.LeaseId, link, "issues", null, ct);
        var issues = await ReadObjectScopesAsync(link.Id, link.GitHubRepositoryId, "issues", ct);
        foreach (var number in issues)
        {
            await ProcessCategoryAsync(job.Id, job.LeaseId, link, "issue-comments", number, ct);
            await ProcessCategoryAsync(job.Id, job.LeaseId, link, "issue-events", number, ct);
        }

        await ProcessCategoryAsync(job.Id, job.LeaseId, link, "releases", null, ct);
    }

    private async Task ProcessCategoryAsync(
        Guid jobId,
        Guid? leaseId,
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
            if (leaseId is { } currentLease) await RequireLeaseAsync(jobId, currentLease, false, ct);
            var eligible = await LoadEligibleLinkAsync(link.Id, ct);
            var fetchAuthorization = eligible;
            if (eligible.GitHubRepositoryId != link.GitHubRepositoryId || eligible.Source != link.Source ||
                eligible.OAuthConnectionId != link.OAuthConnectionId || eligible.AppGrantId != link.AppGrantId || eligible.InstallationId != link.InstallationId)
                throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed during synchronization.", 409);
            string accessToken;
            if (eligible.Source == GitRepositoryCredentialSource.GitHubApp)
            {
                if (appTokens is null || eligible.InstallationId is null)
                    throw Error("GITHUB_APP_NOT_CONFIGURED", "GitHub App synchronization is not configured.", 503);
                try { accessToken = await appTokens.GetAsync(eligible.InstallationId.Value, eligible.ProviderRepositoryId, ct); }
                catch (GitHubIntegrationException ex) when (ex.Code is "GITHUB_APP_ACCESS_DENIED" or "GITHUB_APP_INSTALLATION_UNAVAILABLE")
                {
                    appTokens.InvalidateInstallation(eligible.InstallationId.Value);
                    throw;
                }
                try
                {
                    var remote = await provider.GetInstallationRepositoryAsync(accessToken, eligible.ProviderRepositoryId, ct);
                    if (remote.Id != eligible.ProviderRepositoryId || !remote.CanRead)
                        throw Error("GITHUB_REPOSITORY_ACCESS_LOST", "GitHub App cannot read this repository.", 409);
                }
                catch (GitHubIntegrationException ex) when (ex.Code is "GITHUB_CREDENTIAL_INVALID" or "GITHUB_ACCESS_DENIED" or "GITHUB_REPOSITORY_ACCESS_LOST")
                {
                    appTokens.InvalidateInstallation(eligible.InstallationId.Value);
                    if (ex.Code == "GITHUB_REPOSITORY_ACCESS_LOST")
                    {
                        await MarkAppAccessLostAsync(eligible, ct);
                        throw;
                    }
                    throw Error(ex.Code == "GITHUB_CREDENTIAL_INVALID" ? "GITHUB_APP_TOKEN_INVALID" : "GITHUB_APP_ACCESS_UNVERIFIED", "GitHub App repository access could not be verified.", 503);
                }
                catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_RESOURCE_UNAVAILABLE")
                {
                    appTokens.InvalidateInstallation(eligible.InstallationId.Value);
                    throw;
                }
            }
            else accessToken = await credentials.GetValidAccessTokenAsync(
                eligible.AccountId, eligible.OAuthConnectionId, eligible.LinkedByProfileId, ct);
            GitHubSyncPage result;
            try
            {
                result = await provider.GetSyncPageAsync(accessToken, eligible.FullName,
                    category, scope, pageNumber, PageSize, ct);
            }
            catch (GitHubIntegrationException exception) when (eligible.Source == GitRepositoryCredentialSource.GitHubApp && (exception.Code is "GITHUB_CREDENTIAL_INVALID" or "GITHUB_ACCESS_DENIED"))
            {
                appTokens!.InvalidateInstallation(eligible.InstallationId!.Value);
                throw Error(exception.Code == "GITHUB_CREDENTIAL_INVALID" ? "GITHUB_APP_TOKEN_INVALID" : "GITHUB_APP_ACCESS_UNVERIFIED", "GitHub App repository access could not be verified.", 503);
            }
            catch (GitHubIntegrationException exception) when (eligible.Source == GitRepositoryCredentialSource.GitHubApp && exception.Code == "GITHUB_RESOURCE_UNAVAILABLE")
            {
                appTokens!.InvalidateInstallation(eligible.InstallationId!.Value);
                throw;
            }
            catch (GitHubIntegrationException exception) when (eligible.Source == GitRepositoryCredentialSource.OAuth && exception.Code == "GITHUB_CREDENTIAL_INVALID")
            {
                await credentials.MarkReauthorizationRequiredAsync(
                    eligible.OAuthConnectionId, eligible.LinkedByProfileId, ct);
                throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect the selected GitHub account before synchronizing.", 409);
            }
            catch (GitHubIntegrationException exception) when (eligible.Source == GitRepositoryCredentialSource.OAuth && exception.Code == "GITHUB_ACCESS_DENIED")
            {
                await MarkAccessLostAsync(eligible, ct);
                throw Error("GITHUB_REPOSITORY_ACCESS_LOST", "GitHub denied access to this repository.", 409);
            }

            var observedAt = DateTimeOffset.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (leaseId is { } lockedLease) await RequireLeaseAsync(jobId, lockedLease, true, ct);
            await LockAuthorizationRowsAsync(link, ct);
            eligible = await LoadEligibleLinkAsync(link.Id, ct);
            if (eligible.Source == GitRepositoryCredentialSource.GitHubApp &&
                (eligible.GrantGeneration != fetchAuthorization.GrantGeneration || eligible.InstallationGeneration != fetchAuthorization.InstallationGeneration))
                throw Error("GITHUB_SYNC_AUTHORIZATION_CHANGED", "GitHub authorization changed during synchronization; retry.", 409);
            if (eligible.Source == GitRepositoryCredentialSource.OAuth && eligible.CredentialVersion != link.CredentialVersion)
                throw Error("GITHUB_SYNC_CREDENTIAL_CHANGED", "GitHub authorization changed during synchronization; retry.", 409);
            if (eligible.GitHubRepositoryId != link.GitHubRepositoryId || eligible.AccessStatus != ProjectGitRepositoryAccessStatus.Active || eligible.Source != link.Source ||
                eligible.AppGrantId != link.AppGrantId || eligible.InstallationId != link.InstallationId || eligible.OAuthConnectionId != link.OAuthConnectionId)
                throw Error("GITHUB_REPOSITORY_NOT_AUTHORIZED", "Repository access changed during synchronization.", 409);
            if (!await db.GitSyncJobs.AsNoTracking().AnyAsync(x => x.Id == jobId && x.ProjectGitRepositoryId == eligible.Id &&
                x.GitHubRepositoryId == eligible.GitHubRepositoryId && x.CredentialSource == eligible.Source, ct))
                throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Job repository association changed during synchronization.", 409);

            foreach (var item in result.Items)
                await PersistItemAsync(jobId, pageNumber, eligible, category, item, observedAt,
                    scope, result.RateLimitRemaining, result.RateLimitResetAt, ct);

            if (!result.HasMore)
            {
                await db.ProjectEvidenceObservations
                    .Where(x => x.GitSyncJobId == jobId && x.Category == category && x.ScopeKey == checkpointScope)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.RetrievalCompleteness, "Complete"), ct);
                foreach (var observation in db.ChangeTracker.Entries<ProjectEvidenceObservation>()
                    .Where(x => x.State == EntityState.Added && x.Entity.GitSyncJobId == jobId &&
                        x.Entity.Category == category && x.Entity.ScopeKey == checkpointScope))
                    observation.Entity.RetrievalCompleteness = "Complete";
            }

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
            if (leaseId is { } finalLease) await RequireLeaseAsync(jobId, finalLease, true, ct);
            await transaction.CommitAsync(ct);
            if (!result.HasMore) return;
            pageNumber++;
        }
    }

    private async Task LockAuthorizationRowsAsync(EligibleLink link, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        // Keep the rows that authorize this page stable until commit. A concurrent
        // revocation must either precede this lock or wait until the page commits.
        var rows = link.Source == GitRepositoryCredentialSource.GitHubApp
            ? await db.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM "ProjectGitRepositories" l
                JOIN "GitHubRepositories" r ON r."Id" = l."GitHubRepositoryId"
                JOIN "GitHubAppRepositoryGrants" g ON g."Id" = l."GitHubAppRepositoryGrantId"
                JOIN "GitHubAppInstallations" i ON i."Id" = g."InstallationId"
                WHERE l."Id" = {link.Id}
                FOR SHARE OF l, r, g, i
                """).ToListAsync(ct)
            : await db.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM "ProjectGitRepositories" l
                JOIN "GitHubRepositories" r ON r."Id" = l."GitHubRepositoryId"
                JOIN "GitHubOAuthConnections" c ON c."Id" = l."OAuthConnectionId"
                WHERE l."Id" = {link.Id}
                FOR SHARE OF l, r, c
                """).ToListAsync(ct);
        if (rows.Count != 1)
            throw Error("GITHUB_REPOSITORY_NOT_AUTHORIZED", "Repository access changed during synchronization.", 409);
    }

    private async Task RequireLeaseAsync(Guid jobId, Guid leaseId, bool lockRow, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return; // SQLite test provider has no durable worker claiming.
        var now = DateTimeOffset.UtcNow;
        bool valid;
        if (lockRow)
        {
            var rows = await db.GitSyncJobs.FromSqlInterpolated($"SELECT * FROM \"GitSyncJobs\" WHERE \"Id\" = {jobId} AND \"Status\" = {(short)GitSyncJobStatus.Running} AND \"LeaseId\" = {leaseId} AND \"LeaseExpiresAt\" > {now} FOR UPDATE").AsNoTracking().ToListAsync(ct);
            valid = rows.Count == 1;
        }
        else valid = await db.GitSyncJobs.AsNoTracking().AnyAsync(x => x.Id == jobId && x.Status == GitSyncJobStatus.Running && x.LeaseId == leaseId && x.LeaseExpiresAt > now, ct);
        if (!valid) throw Error("GITHUB_SYNC_LEASE_LOST", "The synchronization job lease expired or was replaced.", 409);
    }

    private async Task PersistItemAsync(
        Guid jobId, int pageNumber, EligibleLink link, string category, JsonElement payload,
        DateTimeOffset observedAt, string? scope, int? rateLimitRemaining,
        DateTimeOffset? rateLimitResetAt, CancellationToken ct)
    {
        if (!TryGetObjectId(category, payload, scope, out var externalId))
            throw Error("GITHUB_EVIDENCE_ID_MISSING", "A fetched item has no stable identity.", 503);
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
        var storedPayload = GitEvidenceFactsExtractor.SanitizeFetchedPayload(category, payload);
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
                PayloadJson = storedPayload,
                RetrievalMetadataJson = retrievalMetadata
            };
            db.GitHubProviderObjects.Add(providerObject);
        }
        else
        {
            providerObject.OccurredAt = occurredAt ?? providerObject.OccurredAt;
            providerObject.LastObservedAt = observedAt;
            providerObject.PayloadJson = storedPayload;
            providerObject.RetrievalMetadataJson = retrievalMetadata;
        }
        var facts = category == "pull-request-files"
            ? GitEvidenceFactsExtractor.ExtractV3(category, externalId, payload, scope)
            : category == "issue-comments" ? GitEvidenceFactsExtractor.ExtractV5IssueComment(externalId, payload, scope, observedAt)
            : GitEvidenceFactsExtractor.ExtractV4(category, externalId, payload, scope);
        var canonical = GitFrozenFactsCanonicalizer.Canonicalize(facts);
        var version = db.GitEvidenceVersions.Local.FirstOrDefault(x => x.GitHubProviderObjectId == providerObject.Id && x.EvidenceSchemaVersion == facts.SchemaVersion && x.CanonicalizationVersion == GitFrozenFactsCanonicalizer.CanonicalizationVersion && x.ContentHash == canonical.Sha256)
            ?? await db.GitEvidenceVersions.SingleOrDefaultAsync(x => x.GitHubProviderObjectId == providerObject.Id && x.EvidenceSchemaVersion == facts.SchemaVersion && x.CanonicalizationVersion == GitFrozenFactsCanonicalizer.CanonicalizationVersion && x.ContentHash == canonical.Sha256, ct);
        if (version is not null)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<GitFrozenFacts>(version.FrozenFactsJson, new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
                if (stored is null || !GitFrozenFactsCanonicalizer.SameContent(canonical, GitFrozenFactsCanonicalizer.Canonicalize(stored)))
                    throw Error("GITHUB_EVIDENCE_HASH_CONFLICT", "Evidence content does not match its identity.", 409);
            }
            catch (JsonException) { throw Error("GITHUB_EVIDENCE_HASH_CONFLICT", "Evidence content is invalid.", 409); }
            catch (ArgumentException) { throw Error("GITHUB_EVIDENCE_HASH_CONFLICT", "Evidence content is invalid.", 409); }
        }
        else
        {
            version = new GitEvidenceVersion { GitHubProviderObjectId = providerObject.Id, GitHubRepositoryId = link.GitHubRepositoryId,
                EvidenceSchemaVersion = facts.SchemaVersion, CanonicalizationVersion = GitFrozenFactsCanonicalizer.CanonicalizationVersion,
                HashAlgorithm = "SHA-256", ContentHash = canonical.Sha256, FrozenFactsJson = Encoding.UTF8.GetString(canonical.Bytes), EvidenceStatus = facts.Completeness };
            db.GitEvidenceVersions.Add(version);
        }
        var scopeKey = scope ?? "";
        var seen = db.ProjectEvidenceObservations.Local.Any(x => x.ProjectGitRepositoryId == link.Id && x.GitSyncJobId == jobId && x.Category == category && x.ScopeKey == scopeKey && x.GitEvidenceVersionId == version.Id) ||
            await db.ProjectEvidenceObservations.AnyAsync(x => x.ProjectGitRepositoryId == link.Id && x.GitSyncJobId == jobId && x.Category == category && x.ScopeKey == scopeKey && x.GitEvidenceVersionId == version.Id, ct);
        if (!seen)
            db.ProjectEvidenceObservations.Add(new ProjectEvidenceObservation { ProjectGitRepositoryId = link.Id, GitSyncJobId = jobId,
                GitEvidenceVersionId = version.Id, GitHubRepositoryId = link.GitHubRepositoryId, Category = category, ScopeKey = scopeKey,
                ObservedAt = observedAt, PageCursor = pageNumber.ToString(CultureInfo.InvariantCulture), AuthorizationSource = link.Source.ToString(), RetrievalCompleteness = "Incomplete" });
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
        Guid linkId, CancellationToken ct, bool includeRecoverable = false)
    {
        var oauth = await (
            from link in db.ProjectGitRepositories.AsNoTracking()
            join repository in db.GitHubRepositories.AsNoTracking()
                on link.GitHubRepositoryId equals repository.Id
            join connection in db.GitHubOAuthConnections.AsNoTracking()
                on link.OAuthConnectionId equals connection.Id
            where link.Id == linkId && link.CredentialSource == GitRepositoryCredentialSource.OAuth && link.UnlinkedAt == null &&
                !link.SyncPaused &&
                (link.AccessStatus == ProjectGitRepositoryAccessStatus.Active ||
                 includeRecoverable &&
                    (link.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization ||
                     link.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost)) &&
                connection.Status == GitHubOAuthConnectionStatus.Active
            select new EligibleLink(
                link.Id, link.GitHubRepositoryId, link.OAuthConnectionId!.Value,
                link.LinkedByProfileId, connection.AccountId, repository.ProviderRepositoryId, repository.FullName,
                link.AccessStatus, GitRepositoryCredentialSource.OAuth, null, null, connection.Version, null, null))
            .SingleOrDefaultAsync(ct);
        if (oauth is not null) return oauth;
        var app = await (
            from link in db.ProjectGitRepositories.AsNoTracking()
            join repository in db.GitHubRepositories.AsNoTracking() on link.GitHubRepositoryId equals repository.Id
            join grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on link.GitHubAppRepositoryGrantId equals grant.Id
            join installation in db.Set<GitHubAppInstallation>().AsNoTracking() on grant.InstallationId equals installation.Id
            where link.Id == linkId && link.CredentialSource == GitRepositoryCredentialSource.GitHubApp && link.OAuthConnectionId == null &&
                link.UnlinkedAt == null && !link.SyncPaused && link.AccessStatus == ProjectGitRepositoryAccessStatus.Active &&
                grant.RevokedAt == null && grant.ProviderRepositoryId == repository.ProviderRepositoryId && installation.Status == GitHubAppInstallationStatus.Active && !installation.VerificationUnknown
            select new EligibleLink(link.Id, link.GitHubRepositoryId, Guid.Empty, link.LinkedByProfileId, Guid.Empty,
                repository.ProviderRepositoryId, repository.FullName, link.AccessStatus, GitRepositoryCredentialSource.GitHubApp,
                grant.Id, installation.ProviderInstallationId, null, grant.AuthorizationGeneration, installation.AuthorizationGeneration)).SingleOrDefaultAsync(ct);
        if (app is not null) return app;
        if (await (from association in db.ProjectGitRepositories.AsNoTracking()
                   join grant in db.Set<GitHubAppRepositoryGrant>().AsNoTracking() on association.GitHubAppRepositoryGrantId equals grant.Id
                   join installation in db.Set<GitHubAppInstallation>().AsNoTracking() on grant.InstallationId equals installation.Id
                   where association.Id == linkId && association.CredentialSource == GitRepositoryCredentialSource.GitHubApp && installation.VerificationUnknown
                   select association.Id).AnyAsync(ct))
            throw Error("GITHUB_APP_VERIFICATION_PENDING", "Installation verification is pending; synchronization is deferred.", 409);
        if (await db.ProjectGitRepositories.AsNoTracking().AnyAsync(x => x.Id == linkId &&
            x.CredentialSource == GitRepositoryCredentialSource.GitHubApp && x.AccessStatus == ProjectGitRepositoryAccessStatus.PendingAuthorization, ct))
            throw Error("GITHUB_APP_PENDING_ACTIVATION", "GitHub App synchronization is not active yet.", 409);
        throw Error("GITHUB_REPOSITORY_NOT_AUTHORIZED", "Repository association is no longer eligible for synchronization.", 409);
    }

    private async Task MarkAppAccessLostAsync(EligibleLink eligible, CancellationToken ct)
    {
        var link = await db.ProjectGitRepositories.SingleOrDefaultAsync(x => x.Id == eligible.Id && x.UnlinkedAt == null, ct);
        if (link is null || link.CredentialSource != GitRepositoryCredentialSource.GitHubApp || link.GitHubAppRepositoryGrantId != eligible.AppGrantId)
            throw Error("GITHUB_SYNC_ASSOCIATION_CHANGED", "Repository authorization changed during synchronization.", 409);
        if (link.AccessStatus == ProjectGitRepositoryAccessStatus.AccessLost) return;
        var now = DateTimeOffset.UtcNow;
        link.AccessStatus = ProjectGitRepositoryAccessStatus.AccessLost; link.LastAccessCheckedAt = now; link.UpdatedAt = now; link.Version = Guid.NewGuid();
        db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory { ProjectGitRepositoryId = link.Id, ActorProfileId = link.LinkedByProfileId,
            Action = ProjectGitRepositoryHistoryAction.AccessLost, Details = "GitHub App repository read access could not be confirmed." });
        await db.SaveChangesAsync(ct);
        GitHubAppDiagnostics.AccessLost.Add(1);
    }

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

    private static bool TryGetObjectId(string category, JsonElement payload, string? scope, out string id)
    {
        id = "";
        if (category == "pull-request-files")
        {
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("filename", out var filename) ||
                filename.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(filename.GetString()) ||
                filename.GetString()!.Length > 4096)
                return false;
            id = GitEvidenceFactsExtractor.GetPullRequestFileExternalId(filename.GetString()!, scope);
            return true;
        }
        var propertyName = category == "branches" ? "name" :
            category is "commits" or "pull-request-commits" ? "sha" :
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
        Guid AccountId, long ProviderRepositoryId, string FullName, ProjectGitRepositoryAccessStatus AccessStatus,
        GitRepositoryCredentialSource Source, Guid? AppGrantId, long? InstallationId, Guid? CredentialVersion,
        long? GrantGeneration, long? InstallationGeneration);
    private sealed record ActivityActor(string Role, string? ExternalId, string? Login);
    private sealed record ActivityDefinition(
        string Type, DateTimeOffset OccurredAt, IReadOnlyList<ActivityActor> Actors);
}
