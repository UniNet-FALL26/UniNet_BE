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

public sealed class GitHubAppRepositoryLinkService(UniNetDbContext db, IGitHubTokenProtector protector,
    GitHubAppUserGrantVerifier verifier, GitHubAppRepositoryActivationService? activation = null, IOptions<GitHubAppOptions>? appOptions = null) : IGitHubAppRepositoryLinkService
{
    public async Task<GitHubAppRepositoryLinkResponse> LinkAsync(Guid accountId, Guid projectId, LinkGitHubAppRepositoryRequest request, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        var actor = await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        if (request.CredentialId == Guid.Empty || request.InstallationId <= 0 || request.RepositoryId <= 0)
            throw Error("GITHUB_APP_LINK_INVALID", "A credential, installation and repository are required.", 400);
        var credential = await db.Set<GitHubAppUserCredential>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CredentialId && x.AccountId == accountId, ct)
            ?? throw Error("GITHUB_APP_AUTH_REQUIRED", "GitHub App authorization is required.", 403);
        if (credential.AccessTokenExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow.AddMinutes(1))
            throw Error("GITHUB_APP_REAUTHORIZATION_REQUIRED", "GitHub App user authorization must be renewed.", 401);
        string token;
        try { token = protector.Unprotect(credential.AccessTokenEncrypted); }
        catch { throw Error("GITHUB_APP_AUTH_UNAVAILABLE", "GitHub App authorization is unavailable.", 503); }
        var verified = await verifier.VerifyForManagementAsync(token, credential.GitHubUserId, request.InstallationId, request.RepositoryId, ct);
        token = string.Empty;
        var remote = verified.Repository;
        if (remote.OwnerId <= 0 || string.IsNullOrWhiteSpace(remote.OwnerLogin) || string.IsNullOrWhiteSpace(remote.Name) ||
            string.IsNullOrWhiteSpace(remote.FullName) || !string.Equals(remote.FullName, $"{remote.OwnerLogin}/{remote.Name}", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(remote.HtmlUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(url.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(url.UserInfo) ||
            !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment))
            throw Error("GITHUB_APP_REPOSITORY_INVALID", "GitHub returned invalid repository metadata.", 502);

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
            var currentCredential = await db.Set<GitHubAppUserCredential>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == credential.Id && x.AccountId == accountId, ct);
            if (currentCredential is null || currentCredential.GitHubUserId != credential.GitHubUserId ||
                currentCredential.AccessTokenEncrypted != credential.AccessTokenEncrypted ||
                currentCredential.AccessTokenExpiresAt is { } currentExpiry && currentExpiry <= DateTimeOffset.UtcNow.AddMinutes(1))
                throw Error("GITHUB_APP_AUTH_CHANGED", "GitHub authorization changed; retry the request.", 409);
            var installation = await db.Set<GitHubAppInstallation>().SingleOrDefaultAsync(x => x.ProviderInstallationId == request.InstallationId, ct);
            if (installation?.VerificationUnknown == true)
                throw Error("GITHUB_APP_VERIFICATION_PENDING", "Installation verification must complete before linking.", 409);
            if (installation is not null && installation.Status != GitHubAppInstallationStatus.Active)
                throw Error("GITHUB_APP_INSTALLATION_UNAVAILABLE", "Installation is not active.", 403);
            if (installation is null)
            {
                installation = new() { ProviderInstallationId = request.InstallationId, OwnerAccountId = verified.Installation.OwnerId,
                    OwnerLogin = verified.Installation.OwnerLogin, OwnerType = verified.Installation.OwnerType, Status = GitHubAppInstallationStatus.Active, CreatedAt = now };
                db.Add(installation);
            }
            else if (installation.OwnerAccountId != verified.Installation.OwnerId || installation.OwnerType != verified.Installation.OwnerType)
                throw Error("GITHUB_APP_INSTALLATION_CHANGED", "Installation ownership changed; reconcile before linking.", 409);
            installation.OwnerLogin = verified.Installation.OwnerLogin; installation.LastVerifiedAt = now; installation.UpdatedAt = now;
            var accountGrant = await db.Set<GitHubAppInstallationAccount>().SingleOrDefaultAsync(x => x.InstallationId == installation.Id && x.AccountId == accountId, ct);
            if (accountGrant is not null && (accountGrant.RevokedAt != null || accountGrant.GitHubUserId != credential.GitHubUserId))
                throw Error("GITHUB_APP_AUTH_CHANGED", "Installation authorization changed; reconcile before linking.", 409);
            if (accountGrant is null) { accountGrant = new() { InstallationId = installation.Id, AccountId = accountId, GitHubUserId = credential.GitHubUserId }; db.Add(accountGrant); }
            accountGrant.VerifiedAt = now;
            var grant = await db.Set<GitHubAppRepositoryGrant>().SingleOrDefaultAsync(x => x.InstallationId == installation.Id && x.ProviderRepositoryId == remote.Id, ct);
            if (grant?.RevokedAt is not null) throw Error("GITHUB_APP_GRANT_REVOKED", "Repository grant requires reconciliation.", 409);
            if (grant is null) { grant = new() { InstallationId = installation.Id, ProviderRepositoryId = remote.Id, GrantedAt = now }; db.Add(grant); }
            grant.VerifiedAt = now;
            var repository = await db.GitHubRepositories.SingleOrDefaultAsync(x => x.ProviderRepositoryId == remote.Id, ct);
            if (repository is null)
            {
                repository = new() { ProviderRepositoryId = remote.Id, ProviderOwnerId = remote.OwnerId, OwnerLogin = remote.OwnerLogin,
                    Name = remote.Name, FullName = remote.FullName, HtmlUrl = remote.HtmlUrl!, Description = remote.Description,
                    DefaultBranch = remote.DefaultBranch, IsPrivate = remote.IsPrivate, IsArchived = remote.IsArchived, CreatedAt = now, UpdatedAt = now, LastVerifiedAt = now };
                db.Add(repository);
            }
            else
            {
                repository.ProviderOwnerId = remote.OwnerId;
                repository.OwnerLogin = remote.OwnerLogin;
                repository.Name = remote.Name;
                repository.FullName = remote.FullName;
                repository.HtmlUrl = remote.HtmlUrl!;
                repository.Description = remote.Description;
                repository.DefaultBranch = remote.DefaultBranch;
                repository.IsPrivate = remote.IsPrivate;
                repository.IsArchived = remote.IsArchived;
                repository.LastVerifiedAt = now;
                repository.UpdatedAt = now;
            }
            var existing = await db.ProjectGitRepositories.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.GitHubRepositoryId == repository.Id && x.UnlinkedAt == null, ct);
            if (existing is not null)
            {
                if (existing.CredentialSource != GitRepositoryCredentialSource.GitHubApp || existing.GitHubAppRepositoryGrantId != grant.Id)
                    throw Error("GITHUB_REPOSITORY_ALREADY_LINKED", "Repository is already linked using another credential source or installation.", 409);
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await transaction.DisposeAsync();
                return Response(existing, remote, request.InstallationId, true, await TryActivateAsync(existing.Id, ct));
            }
            var link = new ProjectGitRepository { ProjectId = projectId, GitHubRepositoryId = repository.Id,
                CredentialSource = GitRepositoryCredentialSource.GitHubApp, OAuthConnectionId = null, GitHubAppRepositoryGrantId = grant.Id,
                LinkedByProfileId = actor, AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization, SyncPaused = true,
                LinkedAt = now, CreatedAt = now, UpdatedAt = now, LastAccessCheckedAt = now };
            db.Add(link);
            db.Add(new ProjectGitRepositoryHistory { ProjectGitRepositoryId = link.Id, ActorProfileId = actor,
                Action = ProjectGitRepositoryHistoryAction.Linked, Details = "Linked GitHub App repository; activation pending." });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); await transaction.DisposeAsync();
            return Response(link, remote, request.InstallationId, false, await TryActivateAsync(link.Id, ct));
        }
        catch (Exception ex) when (ex is DbUpdateException || ex.GetBaseException() is PostgresException { SqlState: "40001" or "40P01" })
        { throw Error("GITHUB_APP_LINK_CONFLICT", "Repository link changed concurrently; retry the request.", 409); }
    }
    private async Task<bool> TryActivateAsync(Guid linkId, CancellationToken ct)
    {
        if (activation is null) return false;
        try { return await activation.ActivateAsync(linkId, ct); }
        catch (GitHubIntegrationException) { db.ChangeTracker.Clear(); return false; }
        catch (HttpRequestException) { db.ChangeTracker.Clear(); return false; }
        catch (System.Security.Cryptography.CryptographicException) { db.ChangeTracker.Clear(); return false; }
        catch (ArgumentException) { db.ChangeTracker.Clear(); return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { db.ChangeTracker.Clear(); return false; }
    }
    private static GitHubAppRepositoryLinkResponse Response(ProjectGitRepository link, GitHubAppUserRepository repository, long installationId, bool existing, bool activated) =>
        new(link.Id, repository.Id, repository.FullName, link.CredentialSource, installationId,
            activated ? ProjectGitRepositoryAccessStatus.Active : ProjectGitRepositoryAccessStatus.PendingAuthorization,
            activated ? "PendingWorker" : "PendingActivation", !activated, existing);
    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
