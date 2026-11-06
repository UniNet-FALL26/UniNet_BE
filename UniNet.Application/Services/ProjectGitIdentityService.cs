using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectGitIdentityService(
    UniNetDbContext db,
    GitHubCredentialService credentials,
    IGitHubProviderClient provider) : IProjectGitIdentityService
{
    private const string GitHubProvider = "github";

    public async Task<ProjectGitMemberIdentityPage> GetAsync(
        Guid accountId, Guid projectId, int page, int pageSize, Guid? userProfileId,
        ProjectGitMemberIdentityStatus? status, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        ValidatePagination(page, pageSize);
        if (status is not null && !Enum.IsDefined(status.Value))
            throw Error("INVALID_GIT_IDENTITY_STATUS", "The requested Git identity status is invalid.", 400);
        var query = db.ProjectGitMemberIdentities.AsNoTracking()
            .Where(x => x.ProjectId == projectId);
        if (userProfileId is not null) query = query.Where(x => x.UserProfileId == userProfileId);
        if (status is not null) query = query.Where(x => x.Status == status);

        var total = await query.CountAsync(ct);
        var identities = await query.OrderBy(x => x.ExternalUsername).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.UserProfiles.AsNoTracking(), identity => identity.UserProfileId, user => user.Id,
                (identity, user) => new { Identity = identity, user.FullName })
            .ToListAsync(ct);
        return new(identities.Select(x => ToResponse(x.Identity, x.FullName)).ToArray(), page, pageSize, total);
    }

    public async Task<ProjectGitMemberIdentityResponse> GetAsync(
        Guid accountId, Guid projectId, Guid identityId, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        var item = await db.ProjectGitMemberIdentities.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Id == identityId)
            .Join(db.UserProfiles.AsNoTracking(), identity => identity.UserProfileId, user => user.Id,
                (identity, user) => new { Identity = identity, user.FullName })
            .SingleOrDefaultAsync(ct)
            ?? throw Error("GIT_IDENTITY_NOT_FOUND", "Project Git identity not found.", 404);
        return ToResponse(item.Identity, item.FullName);
    }

    public async Task<ProjectGitMemberIdentityResponse> LinkAsync(
        Guid accountId, Guid projectId, LinkProjectGitIdentityRequest request, CancellationToken ct)
    {
        if (request.ConnectionId == Guid.Empty)
            throw Error("GIT_OAUTH_CONNECTION_NOT_FOUND", "A valid GitHub OAuth connection is required.", 400);
        var actorId = await RequireEligibleParticipantAsync(accountId, projectId, ct);
        var verification = await VerifyConnectionAsync(accountId, actorId, request.ConnectionId, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await RequireEligibleParticipantAsync(accountId, projectId, ct);
        var active = await db.ProjectGitMemberIdentities.SingleOrDefaultAsync(x =>
            x.ProjectId == projectId && x.Provider == GitHubProvider &&
            x.ExternalAccountId == verification.ProviderAccountId &&
            x.Status == ProjectGitMemberIdentityStatus.Active, ct);
        if (active is not null)
        {
            if (active.UserProfileId != actorId)
                throw OwnershipConflict();
            if (!string.Equals(active.ExternalUsername, verification.Username, StringComparison.Ordinal))
            {
                var previousUsername = active.ExternalUsername;
                active.ExternalUsername = verification.Username;
                active.UpdatedAt = DateTimeOffset.UtcNow;
                active.Version = Guid.NewGuid();
                AddHistory(active, ProjectGitMemberIdentityHistoryAction.UsernameUpdated,
                    active.Status, active.Status, actorId, verification.ConnectionId, previousUsername);
            }
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw ConcurrencyConflict();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                throw ConcurrencyConflict();
            }
            return await GetResponseAsync(active, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var identity = await db.ProjectGitMemberIdentities.SingleOrDefaultAsync(x =>
            x.ProjectId == projectId && x.UserProfileId == actorId && x.Provider == GitHubProvider &&
            x.ExternalAccountId == verification.ProviderAccountId &&
            x.Status == ProjectGitMemberIdentityStatus.Inactive, ct);
        if (identity is null)
        {
            identity = new ProjectGitMemberIdentity
            {
                ProjectId = projectId,
                UserProfileId = actorId,
                Provider = GitHubProvider,
                ExternalAccountId = verification.ProviderAccountId,
                ExternalUsername = verification.Username,
                VerifiedByConnectionId = verification.ConnectionId,
                Status = ProjectGitMemberIdentityStatus.Active,
                LinkedAt = now,
                LastActivatedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.ProjectGitMemberIdentities.Add(identity);
            AddHistory(identity, ProjectGitMemberIdentityHistoryAction.Linked, null,
                ProjectGitMemberIdentityStatus.Active, actorId, verification.ConnectionId);
        }
        else
        {
            var previousStatus = identity.Status;
            identity.Status = ProjectGitMemberIdentityStatus.Active;
            identity.ExternalUsername = verification.Username;
            identity.VerifiedByConnectionId = verification.ConnectionId;
            identity.UnlinkedAt = null;
            identity.LastActivatedAt = now;
            identity.UpdatedAt = now;
            identity.Version = Guid.NewGuid();
            AddHistory(identity, ProjectGitMemberIdentityHistoryAction.Reactivated,
                previousStatus, identity.Status, actorId, verification.ConnectionId);
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var currentOwner = await FindActiveIdentityAsync(projectId, verification.ProviderAccountId, ct);
            if (currentOwner is { } owner && owner.Identity.UserProfileId == actorId)
                return ToResponse(owner.Identity, owner.FullName);
            if (currentOwner is not null) throw OwnershipConflict();
            throw ConcurrencyConflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict();
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var currentOwner = await FindActiveIdentityAsync(projectId, verification.ProviderAccountId, ct);
            if (currentOwner is { } owner && owner.Identity.UserProfileId == actorId)
                return ToResponse(owner.Identity, owner.FullName);
            if (currentOwner is not null) throw OwnershipConflict();
            throw ConcurrencyConflict();
        }
        return await GetResponseAsync(identity, ct);
    }

    public async Task<ProjectGitMemberIdentityResponse> UnlinkAsync(
        Guid accountId, Guid projectId, Guid identityId, CancellationToken ct)
    {
        var actorId = await RequireEligibleParticipantAsync(accountId, projectId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await RequireEligibleParticipantAsync(accountId, projectId, ct);
        var identity = await db.ProjectGitMemberIdentities.SingleOrDefaultAsync(
            x => x.Id == identityId && x.ProjectId == projectId, ct)
            ?? throw Error("GIT_IDENTITY_NOT_FOUND", "Project Git identity not found.", 404);
        if (identity.UserProfileId != actorId)
            throw Error("GIT_IDENTITY_NOT_OWNER", "Only the identity owner can unlink it.", 403);
        if (identity.Status == ProjectGitMemberIdentityStatus.Active)
        {
            var now = DateTimeOffset.UtcNow;
            identity.Status = ProjectGitMemberIdentityStatus.Inactive;
            identity.UnlinkedAt = now;
            identity.UpdatedAt = now;
            identity.Version = Guid.NewGuid();
            AddHistory(identity, ProjectGitMemberIdentityHistoryAction.Unlinked,
                ProjectGitMemberIdentityStatus.Active, ProjectGitMemberIdentityStatus.Inactive,
                actorId, identity.VerifiedByConnectionId);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw ConcurrencyConflict();
            }
            catch (Exception ex) when (IsSerializationFailure(ex))
            {
                throw ConcurrencyConflict();
            }
        }
        else
        {
            await transaction.CommitAsync(ct);
        }
        return await GetResponseAsync(identity, ct);
    }

    public async Task<ProjectGitMemberIdentityResponse> ReactivateAsync(
        Guid accountId, Guid projectId, Guid identityId,
        ReactivateProjectGitIdentityRequest request, CancellationToken ct)
    {
        if (request.ConnectionId == Guid.Empty)
            throw Error("GIT_OAUTH_CONNECTION_NOT_FOUND", "A valid GitHub OAuth connection is required.", 400);
        var actorId = await RequireEligibleParticipantAsync(accountId, projectId, ct);
        var identityOwner = await db.ProjectGitMemberIdentities.AsNoTracking()
            .Where(x => x.Id == identityId && x.ProjectId == projectId)
            .Select(x => new { x.UserProfileId, x.ExternalAccountId, x.Provider })
            .SingleOrDefaultAsync(ct)
            ?? throw Error("GIT_IDENTITY_NOT_FOUND", "Project Git identity not found.", 404);
        if (identityOwner.UserProfileId != actorId)
            throw Error("GIT_IDENTITY_NOT_OWNER", "Only the identity owner can reactivate it.", 403);
        var verification = await VerifyConnectionAsync(accountId, actorId, request.ConnectionId, ct);
        if (identityOwner.Provider != GitHubProvider || identityOwner.ExternalAccountId != verification.ProviderAccountId)
            throw Error("GIT_OAUTH_ACCOUNT_MISMATCH",
                "The verified GitHub account does not match this identity.", 409);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await RequireEligibleParticipantAsync(accountId, projectId, ct);
        var identity = await db.ProjectGitMemberIdentities.SingleAsync(
            x => x.Id == identityId && x.ProjectId == projectId, ct);
        if (identity.Status == ProjectGitMemberIdentityStatus.Active)
        {
            await transaction.CommitAsync(ct);
            return await GetResponseAsync(identity, ct);
        }
        var activeOwner = await db.ProjectGitMemberIdentities.AnyAsync(x =>
            x.ProjectId == projectId && x.Provider == identity.Provider &&
            x.ExternalAccountId == identity.ExternalAccountId &&
            x.Status == ProjectGitMemberIdentityStatus.Active && x.Id != identity.Id, ct);
        if (activeOwner) throw OwnershipConflict();

        var now = DateTimeOffset.UtcNow;
        var previousStatus = identity.Status;
        identity.Status = ProjectGitMemberIdentityStatus.Active;
        identity.ExternalUsername = verification.Username;
        identity.VerifiedByConnectionId = verification.ConnectionId;
        identity.UnlinkedAt = null;
        identity.LastActivatedAt = now;
        identity.UpdatedAt = now;
        identity.Version = Guid.NewGuid();
        AddHistory(identity, ProjectGitMemberIdentityHistoryAction.Reactivated,
            previousStatus, identity.Status, actorId, verification.ConnectionId);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw OwnershipConflict();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict();
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            throw ConcurrencyConflict();
        }
        return await GetResponseAsync(identity, ct);
    }

    public async Task<ProjectGitMemberIdentityHistoryPage> GetHistoryAsync(
        Guid accountId, Guid projectId, Guid identityId, int page, int pageSize, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireProjectViewerAsync(db, projectId, accountId, ct);
        ValidatePagination(page, pageSize);
        var exists = await db.ProjectGitMemberIdentities.AsNoTracking()
            .AnyAsync(x => x.Id == identityId && x.ProjectId == projectId, ct);
        if (!exists) throw Error("GIT_IDENTITY_NOT_FOUND", "Project Git identity not found.", 404);

        var query = db.ProjectGitMemberIdentityHistory.AsNoTracking()
            .Where(x => x.IdentityId == identityId && x.ProjectId == projectId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ProjectGitMemberIdentityHistoryResponse(
                x.Id, x.IdentityId, x.ProjectId, x.UserProfileId, x.Provider,
                x.ExternalAccountId, x.ExternalUsername, x.PreviousExternalUsername,
                x.Action, x.PreviousStatus, x.NewStatus, x.PerformedByProfileId,
                x.VerifiedByConnectionId, x.OccurredAt))
            .ToListAsync(ct);
        return new(items, page, pageSize, total);
    }

    private async Task<(long ProviderAccountId, string Username, Guid ConnectionId)> VerifyConnectionAsync(
        Guid accountId, Guid actorProfileId, Guid connectionId, CancellationToken ct)
    {
        string accessToken;
        try
        {
            accessToken = await credentials.GetValidAccessTokenAsync(accountId, connectionId, actorProfileId, ct);
        }
        catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_CONNECTION_NOT_FOUND")
        {
            throw Error("GIT_OAUTH_CONNECTION_NOT_FOUND",
                "The GitHub OAuth connection is not owned by this UniNet account.", 404);
        }
        catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_REAUTH_REQUIRED")
        {
            throw Error("GIT_OAUTH_REAUTHORIZATION_REQUIRED",
                "Reconnect the GitHub account before linking this identity.", 409);
        }

        try
        {
            var user = await provider.GetAuthenticatedUserAsync(accessToken, ct);
            var connectionAccount = await db.GitHubOAuthConnections.AsNoTracking()
                .Where(x => x.Id == connectionId && x.AccountId == accountId &&
                    x.Status == GitHubOAuthConnectionStatus.Active)
                .Select(x => x.ProviderAccountId)
                .SingleOrDefaultAsync(ct);
            if (connectionAccount is null)
                throw Error("GIT_OAUTH_REAUTHORIZATION_REQUIRED",
                    "Reconnect the GitHub account before linking this identity.", 409);
            if (!string.Equals(connectionAccount,
                    user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
                throw Error("GIT_OAUTH_ACCOUNT_MISMATCH",
                    "The authenticated GitHub account does not match the saved OAuth connection.", 409);
            return (user.Id, user.Login, connectionId);
        }
        catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_CREDENTIAL_INVALID")
        {
            await credentials.MarkReauthorizationRequiredAsync(connectionId, actorProfileId, ct);
            throw Error("GIT_OAUTH_REAUTHORIZATION_REQUIRED",
                "Reconnect the GitHub account before linking this identity.", 409);
        }
    }

    private async Task<Guid> RequireEligibleParticipantAsync(Guid accountId, Guid projectId, CancellationToken ct)
    {
        var profileId = await ProjectGitAuthorization.RequireActorProfileAsync(db, accountId, ct);
        var project = await db.Projects.AsNoTracking().Where(x => x.Id == projectId)
            .Select(x => new { x.CreatorId }).SingleOrDefaultAsync(ct)
            ?? throw Error("PROJECT_NOT_FOUND", "Project not found.", 404);
        if (project.CreatorId == profileId) return profileId;
        var activeMember = await db.ProjectMembers.AsNoTracking().AnyAsync(x =>
            x.ProjectId == projectId && x.UserId == profileId &&
            x.Status == ProjectMemberStatus.Active, ct);
        if (!activeMember)
            throw Error("GIT_PROJECT_MEMBERSHIP_REQUIRED",
                "The project creator or an active project member is required.", 403);
        return profileId;
    }

    private async Task<ProjectGitMemberIdentityResponse> GetResponseAsync(
        ProjectGitMemberIdentity identity, CancellationToken ct)
    {
        var fullName = await db.UserProfiles.AsNoTracking()
            .Where(x => x.Id == identity.UserProfileId)
            .Select(x => x.FullName)
            .SingleAsync(ct);
        return ToResponse(identity, fullName);
    }

    private async Task<(ProjectGitMemberIdentity Identity, string FullName)?> FindActiveIdentityAsync(
        Guid projectId, long externalAccountId, CancellationToken ct)
    {
        return await db.ProjectGitMemberIdentities.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Provider == GitHubProvider &&
                x.ExternalAccountId == externalAccountId && x.Status == ProjectGitMemberIdentityStatus.Active)
            .Join(db.UserProfiles.AsNoTracking(), identity => identity.UserProfileId, user => user.Id,
                (identity, user) => new { Identity = identity, user.FullName })
            .Select(x => new { x.Identity, x.FullName })
            .SingleOrDefaultAsync(ct) is { } found
                ? (found.Identity, found.FullName)
                : null;
    }

    private void AddHistory(
        ProjectGitMemberIdentity identity,
        ProjectGitMemberIdentityHistoryAction action,
        ProjectGitMemberIdentityStatus? previousStatus,
        ProjectGitMemberIdentityStatus? newStatus,
        Guid actorProfileId,
        Guid? connectionId,
        string? previousUsername = null)
    {
        db.ProjectGitMemberIdentityHistory.Add(new ProjectGitMemberIdentityHistory
        {
            IdentityId = identity.Id,
            ProjectId = identity.ProjectId,
            UserProfileId = identity.UserProfileId,
            Provider = identity.Provider,
            ExternalAccountId = identity.ExternalAccountId,
            ExternalUsername = identity.ExternalUsername,
            PreviousExternalUsername = previousUsername,
            Action = action,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            PerformedByProfileId = actorProfileId,
            VerifiedByConnectionId = connectionId
        });
    }

    private static ProjectGitMemberIdentityResponse ToResponse(ProjectGitMemberIdentity identity, string fullName) =>
        new(identity.Id, identity.ProjectId, identity.UserProfileId, fullName, identity.Provider,
            identity.ExternalAccountId, identity.ExternalUsername, identity.Status,
            identity.LinkedAt, identity.UnlinkedAt, identity.LastActivatedAt,
            identity.CreatedAt, identity.UpdatedAt);

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page is < 1 or > 1000 || pageSize is < 1 or > 100)
            throw Error("INVALID_PAGINATION", "Page must be between 1 and 1000 and page size between 1 and 100.", 400);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsSerializationFailure(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } ||
        exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure } };

    private static GitHubIntegrationException OwnershipConflict() =>
        Error("GIT_IDENTITY_OWNERSHIP_CONFLICT",
            "This GitHub account already has an active owner in the project.", 409);

    private static GitHubIntegrationException ConcurrencyConflict() =>
        Error("GIT_IDENTITY_CONCURRENCY_CONFLICT",
            "The Git identity changed concurrently. Retry the request.", 409);

    private static GitHubIntegrationException Error(string code, string message, int status) =>
        new(code, message, status);
}
