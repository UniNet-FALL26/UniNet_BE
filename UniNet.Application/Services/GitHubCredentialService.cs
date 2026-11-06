using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubCredentialService(
    UniNetDbContext db,
    IGitHubProviderClient provider,
    IGitHubTokenProtector tokenProtector)
{
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);

    public async Task<string> GetValidAccessTokenAsync(
        Guid accountId, Guid connectionId, Guid actorProfileId, CancellationToken ct)
    {
        var connection = await db.GitHubOAuthConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == connectionId && x.AccountId == accountId, ct)
            ?? throw Error("GITHUB_CONNECTION_NOT_FOUND", "GitHub connection not found.", 404);
        if (connection.Status != GitHubOAuthConnectionStatus.Active)
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect this GitHub account before using it.", 409);

        var now = DateTimeOffset.UtcNow;
        if (connection.AccessTokenExpiresAt is null || connection.AccessTokenExpiresAt > now + RefreshWindow)
            return tokenProtector.Unprotect(connection.AccessTokenEncrypted);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
        {
            var lockId = BinaryPrimitives.ReadInt64LittleEndian(
                SHA256.HashData(Encoding.UTF8.GetBytes($"github-refresh:{connectionId:N}")));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        }

        var current = await db.GitHubOAuthConnections.SingleAsync(x => x.Id == connectionId, ct);
        now = DateTimeOffset.UtcNow;
        if (current.Status != GitHubOAuthConnectionStatus.Active)
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect this GitHub account before using it.", 409);
        if (current.AccessTokenExpiresAt is null || current.AccessTokenExpiresAt > now + RefreshWindow)
        {
            var currentToken = tokenProtector.Unprotect(current.AccessTokenEncrypted);
            await transaction.CommitAsync(ct);
            return currentToken;
        }

        if (current.RefreshTokenEncrypted is null ||
            current.RefreshTokenExpiresAt is { } refreshExpiresAt && refreshExpiresAt <= now)
        {
            await MarkReauthorizationRequiredAsync(current, actorProfileId, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect this GitHub account before using it.", 409);
        }

        GitHubOAuthTokens refreshed;
        try
        {
            refreshed = await provider.RefreshAsync(
                tokenProtector.Unprotect(current.RefreshTokenEncrypted), ct);
        }
        catch (GitHubIntegrationException ex) when (ex.Code == "GITHUB_REAUTH_REQUIRED")
        {
            await MarkReauthorizationRequiredAsync(current, actorProfileId, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw Error("GITHUB_REAUTH_REQUIRED", "Reconnect this GitHub account before using it.", 409);
        }

        current.AccessTokenEncrypted = tokenProtector.Protect(refreshed.AccessToken);
        current.AccessTokenExpiresAt = Expiration(now, refreshed.ExpiresInSeconds);
        if (refreshed.RefreshToken is not null)
            current.RefreshTokenEncrypted = tokenProtector.Protect(refreshed.RefreshToken);
        if (refreshed.RefreshTokenExpiresInSeconds is > 0)
            current.RefreshTokenExpiresAt = Expiration(now, refreshed.RefreshTokenExpiresInSeconds);
        if (!string.IsNullOrWhiteSpace(refreshed.GrantedScopes))
            current.GrantedScopes = refreshed.GrantedScopes;
        current.UpdatedAt = now;
        current.Version = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error("GITHUB_CONNECTION_CONFLICT", "GitHub authorization changed concurrently. Retry the request.", 409);
        }
        await transaction.CommitAsync(ct);
        return refreshed.AccessToken;
    }

    public async Task MarkReauthorizationRequiredAsync(
        Guid connectionId, Guid actorProfileId, CancellationToken ct)
    {
        var connection = await db.GitHubOAuthConnections
            .SingleOrDefaultAsync(x => x.Id == connectionId, ct);
        if (connection is null || connection.Status != GitHubOAuthConnectionStatus.Active) return;
        await MarkReauthorizationRequiredAsync(connection, actorProfileId, DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkReauthorizationRequiredAsync(
        GitHubOAuthConnection connection, Guid actorProfileId, DateTimeOffset now, CancellationToken ct)
    {
        connection.Status = GitHubOAuthConnectionStatus.ReauthorizationRequired;
        connection.AccessTokenEncrypted = "";
        connection.RefreshTokenEncrypted = null;
        connection.AccessTokenExpiresAt = null;
        connection.RefreshTokenExpiresAt = null;
        connection.UpdatedAt = now;
        connection.Version = Guid.NewGuid();

        var links = await db.ProjectGitRepositories
            .Where(x => x.OAuthConnectionId == connection.Id && x.UnlinkedAt == null &&
                x.AccessStatus != ProjectGitRepositoryAccessStatus.PendingAuthorization)
            .ToListAsync(ct);
        foreach (var link in links)
        {
            link.AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization;
            link.LastAccessCheckedAt = now;
            link.UpdatedAt = now;
            link.Version = Guid.NewGuid();
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
            {
                ProjectGitRepositoryId = link.Id,
                ActorProfileId = actorProfileId,
                Action = ProjectGitRepositoryHistoryAction.AuthorizationPending,
                PreviousOAuthConnectionId = connection.Id,
                NewOAuthConnectionId = connection.Id,
                Details = "GitHub authorization requires renewal."
            });
        }
    }

    private static DateTimeOffset? Expiration(DateTimeOffset now, int? seconds) =>
        seconds is > 0 ? now.AddSeconds(seconds.Value) : null;

    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
