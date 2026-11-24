using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubAppGrantService(UniNetDbContext db, IGitHubAppUserClient provider, IGitHubTokenProtector protector, IOptions<GitHubAppOptions>? appOptions = null) : IGitHubAppGrantService
{
    public async Task<IReadOnlyList<GitHubAppCredentialItem>> GetCredentialsAsync(Guid accountId, Guid projectId, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        var credentials = await db.Set<GitHubAppUserCredential>().AsNoTracking().Where(x => x.AccountId == accountId)
            .Select(x => new { x.Id, x.GitHubUserId, x.Login, x.AccessTokenExpiresAt }).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        return credentials.Select(x => new GitHubAppCredentialItem(x.Id, x.GitHubUserId, x.Login, x.AccessTokenExpiresAt,
            x.AccessTokenExpiresAt is { } expiry && expiry <= now)).ToArray();
    }
    public async Task<GitHubAppGrantPage<GitHubAppInstallationItem>> GetInstallationsAsync(Guid accountId, Guid projectId, Guid credentialId, int page, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        var token = await VerifiedTokenAsync(accountId, projectId, credentialId, page, ct);
        var response = await provider.GetInstallationsAsync(token.Value, page, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var remote in response.Items) await UpsertInstallationAsync(accountId, token.UserId, remote, now, ct);
        await SaveAsync(ct);
        return new(response.Items.Select(x => new GitHubAppInstallationItem(x.Id, x.OwnerLogin, x.OwnerType, x.Suspended ? "Suspended" : "Active", x.ConfigurationUrl)).ToArray(), page, response.HasMore);
    }

    public async Task<GitHubAppGrantPage<GitHubAppRepositoryItem>> GetRepositoriesAsync(Guid accountId, Guid projectId, Guid credentialId, long installationId, int page, CancellationToken ct)
    {
        GitHubAppAvailability.Require(appOptions);
        if (installationId <= 0) throw Error("GITHUB_APP_INSTALLATION_INVALID", "Invalid installation identifier.", 400);
        var token = await VerifiedTokenAsync(accountId, projectId, credentialId, page, ct);
        GitHubAppUserInstallation? installation = null;
        for (var n = 1; n <= 1000; n++)
        {
            var list = await provider.GetInstallationsAsync(token.Value, n, ct);
            installation = list.Items.SingleOrDefault(x => x.Id == installationId);
            if (installation is not null || !list.HasMore) break;
            if (n == 1000) throw Error("GITHUB_APP_PAGE_LIMIT", "Installation listing is incomplete.", 502);
        }
        if (installation is null || installation.Suspended)
            throw Error("GITHUB_APP_INSTALLATION_UNAVAILABLE", "Installation is not available to this user.", 403);
        var repositories = await provider.GetRepositoriesAsync(token.Value, installationId, page, ct);
        var now = DateTimeOffset.UtcNow;
        var local = await UpsertInstallationAsync(accountId, token.UserId, installation, now, ct);
        foreach (var repo in repositories.Items)
        {
            if (!repo.CanRead) continue;
            var grant = await db.Set<GitHubAppRepositoryGrant>().SingleOrDefaultAsync(x => x.InstallationId == local.Id && x.ProviderRepositoryId == repo.Id, ct);
            if (grant is null) { grant = new() { InstallationId = local.Id, ProviderRepositoryId = repo.Id, GrantedAt = now }; db.Add(grant); }
            grant.VerifiedAt = now;
            grant.RevokedAt = null;
        }
        await SaveAsync(ct);
        return new(repositories.Items.Select(x => new GitHubAppRepositoryItem(x.Id, x.FullName, x.IsPrivate,
            x.CanRead && x.CanAdmin, !x.CanRead ? "GitHub read permission unavailable" :
            !x.CanAdmin ? "GitHub repository administrator access required" : null)).ToArray(), page, repositories.HasMore);
    }

    private async Task<(string Value, long UserId)> VerifiedTokenAsync(Guid accountId, Guid projectId, Guid credentialId, int page, CancellationToken ct)
    {
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        if (page is < 1 or > 1000) throw Error("GITHUB_APP_INVALID_PAGE", "Invalid page.", 400);
        var credential = await db.Set<GitHubAppUserCredential>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == credentialId && x.AccountId == accountId, ct)
            ?? throw Error("GITHUB_APP_AUTH_REQUIRED", "GitHub App authorization is required.", 403);
        if (credential.AccessTokenExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow.AddMinutes(1))
            throw Error("GITHUB_APP_REAUTHORIZATION_REQUIRED", "GitHub App user authorization must be renewed.", 401);
        string token;
        try { token = protector.Unprotect(credential.AccessTokenEncrypted); }
        catch { throw Error("GITHUB_APP_AUTH_UNAVAILABLE", "GitHub App authorization is unavailable.", 503); }
        var user = await provider.GetUserAsync(token, ct);
        if (user.Id != credential.GitHubUserId) throw Error("GITHUB_APP_USER_MISMATCH", "GitHub user does not match the authorized account.", 403);
        return (token, user.Id);
    }

    private async Task<GitHubAppInstallation> UpsertInstallationAsync(Guid accountId, long userId, GitHubAppUserInstallation remote, DateTimeOffset now, CancellationToken ct)
    {
        if (remote.Id <= 0 || remote.OwnerId <= 0 || string.IsNullOrWhiteSpace(remote.OwnerLogin) || remote.OwnerType is not ("User" or "Organization"))
            throw Error("GITHUB_APP_INVALID_RESPONSE", "GitHub returned invalid installation data.", 502);
        var installation = await db.Set<GitHubAppInstallation>().SingleOrDefaultAsync(x => x.ProviderInstallationId == remote.Id, ct);
        if (installation?.VerificationUnknown == true)
            throw Error("GITHUB_APP_VERIFICATION_PENDING", "Installation verification is pending; retry after reconciliation.", 409);
        if (installation is null) { installation = new() { ProviderInstallationId = remote.Id, CreatedAt = now }; db.Add(installation); }
        installation.OwnerAccountId = remote.OwnerId; installation.OwnerLogin = remote.OwnerLogin; installation.OwnerType = remote.OwnerType;
        installation.Status = remote.Suspended ? GitHubAppInstallationStatus.Suspended : GitHubAppInstallationStatus.Active;
        installation.LastVerifiedAt = now; installation.UpdatedAt = now;
        var association = await db.Set<GitHubAppInstallationAccount>().SingleOrDefaultAsync(x => x.InstallationId == installation.Id && x.AccountId == accountId, ct);
        if (association is null) { association = new() { InstallationId = installation.Id, AccountId = accountId }; db.Add(association); }
        association.GitHubUserId = userId; association.VerifiedAt = now; association.RevokedAt = remote.Suspended ? now : null;
        return installation;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw Error("GITHUB_APP_GRANT_CONFLICT", "Installation state changed concurrently; retry the request.", 409); }
    }
    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
