using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubOAuthService(
    UniNetDbContext db,
    IGitHubProviderClient provider,
    IGitHubTokenProtector tokenProtector,
    IOptions<GitHubIntegrationOptions> options) : IGitHubOAuthService
{
    private readonly GitHubIntegrationOptions settings = options.Value;

    public async Task<GitHubOAuthStartResponse> StartAsync(
        Guid accountId, StartGitHubOAuthRequest request, CancellationToken ct)
    {
        await RequireAccountAsync(accountId, ct);
        EnsureOAuthConfigured();
        if (string.IsNullOrWhiteSpace(request.ReturnTarget) || settings.ReturnTargets is null ||
            !settings.ReturnTargets.TryGetValue(request.ReturnTarget, out var returnUri))
            throw Error("INVALID_RETURN_TARGET", "The requested OAuth return target is not allowed.", 400);
        ValidateReturnUri(returnUri);

        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var flow = new GitHubOAuthAuthorizationFlow
        {
            AccountId = accountId,
            StateHash = HashState(state),
            ReturnTarget = request.ReturnTarget,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(settings.FlowLifetimeMinutes)
        };
        db.GitHubOAuthAuthorizationFlows.Add(flow);
        await db.SaveChangesAsync(ct);

        var authorizeUri = BuildAuthorizationUri(state);
        return new(flow.Id, authorizeUri.ToString(), flow.ExpiresAt);
    }

    public async Task<GitHubOAuthCallbackResult> ProcessCallbackAsync(
        string? code, string? state, string? providerError, CancellationToken ct)
    {
        EnsureOAuthConfigured();
        if (string.IsNullOrWhiteSpace(state) || state.Length > 256)
            throw Error("INVALID_OAUTH_STATE", "The OAuth state is invalid or expired.", 400);

        var stateHash = HashState(state);
        var now = DateTimeOffset.UtcNow;
        var flow = await db.GitHubOAuthAuthorizationFlows
            .FirstOrDefaultAsync(x => x.StateHash == stateHash, ct)
            ?? throw Error("INVALID_OAUTH_STATE", "The OAuth state is invalid or expired.", 400);
        var target = GetReturnUri(flow.ReturnTarget);
        if (flow.ExpiresAt <= now)
        {
            if (flow.Status is GitHubOAuthFlowStatus.AwaitingCallback or GitHubOAuthFlowStatus.AwaitingCompletion)
            {
                flow.Status = GitHubOAuthFlowStatus.Expired;
                flow.Version = Guid.NewGuid();
                ClearPendingCredential(flow);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    var persistedStatus = await db.GitHubOAuthAuthorizationFlows.AsNoTracking()
                        .Where(x => x.Id == flow.Id)
                        .Select(x => x.Status)
                        .SingleAsync(ct);
                    if (persistedStatus != GitHubOAuthFlowStatus.Expired) throw;
                }
            }
            return RedirectResult(target, flow.Id, "expired");
        }

        if (flow.Status != GitHubOAuthFlowStatus.AwaitingCallback)
            throw Error("OAUTH_FLOW_ALREADY_USED", "This OAuth flow has already been processed.", 409);
        flow.Status = GitHubOAuthFlowStatus.Processing;
        flow.Version = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error("OAUTH_FLOW_ALREADY_USED", "This OAuth flow has already been processed.", 409);
        }

        if (!string.IsNullOrWhiteSpace(providerError))
        {
            await SetFlowStatusAsync(flow.Id, GitHubOAuthFlowStatus.Denied, ct);
            return RedirectResult(target, flow.Id, "denied");
        }
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096)
        {
            await SetFlowStatusAsync(flow.Id, GitHubOAuthFlowStatus.Failed, ct);
            return RedirectResult(target, flow.Id, "failed");
        }

        try
        {
            var tokens = await provider.ExchangeCodeAsync(code, ct);
            var user = await provider.GetAuthenticatedUserAsync(tokens.AccessToken, ct);
            var completedAt = DateTimeOffset.UtcNow;
            var pendingFlow = await db.GitHubOAuthAuthorizationFlows.SingleAsync(x => x.Id == flow.Id, ct);
            pendingFlow.Status = GitHubOAuthFlowStatus.AwaitingCompletion;
            pendingFlow.PendingProviderAccountId = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            pendingFlow.PendingLogin = user.Login;
            pendingFlow.PendingDisplayName = user.Name;
            pendingFlow.PendingAvatarUrl = user.AvatarUrl;
            pendingFlow.PendingAccessTokenEncrypted = tokenProtector.Protect(tokens.AccessToken);
            pendingFlow.PendingRefreshTokenEncrypted = tokens.RefreshToken is null ? null : tokenProtector.Protect(tokens.RefreshToken);
            pendingFlow.PendingGrantedScopes = tokens.GrantedScopes;
            pendingFlow.PendingAccessTokenExpiresAt = Expiration(completedAt, tokens.ExpiresInSeconds);
            pendingFlow.PendingRefreshTokenExpiresAt = Expiration(completedAt, tokens.RefreshTokenExpiresInSeconds);
            await db.SaveChangesAsync(ct);
            return RedirectResult(target, flow.Id, "ready");
        }
        catch (GitHubIntegrationException)
        {
            await SetFlowStatusAsync(flow.Id, GitHubOAuthFlowStatus.Failed, ct);
            return RedirectResult(target, flow.Id, "failed");
        }
    }

    public async Task<GitHubOAuthConnectionResponse> CompleteAsync(
        Guid accountId, CompleteGitHubOAuthRequest request, CancellationToken ct)
    {
        await RequireAccountAsync(accountId, ct);
        var flow = await db.GitHubOAuthAuthorizationFlows
            .SingleOrDefaultAsync(x => x.Id == request.FlowId, ct)
            ?? throw Error("OAUTH_FLOW_NOT_FOUND", "OAuth flow not found.", 404);
        if (flow.AccountId != accountId)
            throw Error("FORBIDDEN", "This OAuth flow belongs to another account.", 403);
        if (flow.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            if (flow.Status is GitHubOAuthFlowStatus.AwaitingCallback or GitHubOAuthFlowStatus.AwaitingCompletion)
            {
                flow.Status = GitHubOAuthFlowStatus.Expired;
                flow.Version = Guid.NewGuid();
                ClearPendingCredential(flow);
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    var persistedStatus = await db.GitHubOAuthAuthorizationFlows.AsNoTracking()
                        .Where(x => x.Id == flow.Id)
                        .Select(x => x.Status)
                        .SingleAsync(ct);
                    if (persistedStatus != GitHubOAuthFlowStatus.Expired) throw;
                }
            }
            throw Error("OAUTH_FLOW_EXPIRED", "The OAuth flow has expired.", 410);
        }
        if (flow.Status != GitHubOAuthFlowStatus.AwaitingCompletion ||
            flow.PendingAccessTokenEncrypted is null || flow.PendingProviderAccountId is null)
            throw Error("OAUTH_FLOW_NOT_READY", "The OAuth flow is not ready to be completed.", 409);

        var accessToken = tokenProtector.Unprotect(flow.PendingAccessTokenEncrypted);
        var providerUser = await provider.GetAuthenticatedUserAsync(accessToken, ct);
        if (!string.Equals(providerUser.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                flow.PendingProviderAccountId, StringComparison.Ordinal))
            throw Error("GITHUB_ACCOUNT_CHANGED", "The authenticated GitHub account does not match the OAuth flow.", 409);

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existingConnections = await db.GitHubOAuthConnections
            .Where(x => x.AccountId == accountId &&
                x.ProviderAccountId == flow.PendingProviderAccountId)
            .ToListAsync(ct);
        var connection = existingConnections
            .OrderByDescending(x => x.Status == GitHubOAuthConnectionStatus.Active)
            .ThenByDescending(x => x.UpdatedAt)
            .FirstOrDefault();
        if (connection is null)
        {
            connection = new GitHubOAuthConnection
            {
                AccountId = accountId,
                ProviderAccountId = flow.PendingProviderAccountId,
                ConnectedAt = now
            };
            db.GitHubOAuthConnections.Add(connection);
        }
        connection.Login = providerUser.Login;
        connection.DisplayName = providerUser.Name;
        connection.AvatarUrl = providerUser.AvatarUrl;
        connection.AccessTokenEncrypted = flow.PendingAccessTokenEncrypted;
        connection.RefreshTokenEncrypted = flow.PendingRefreshTokenEncrypted;
        connection.GrantedScopes = flow.PendingGrantedScopes ?? "";
        connection.AccessTokenExpiresAt = flow.PendingAccessTokenExpiresAt;
        connection.RefreshTokenExpiresAt = flow.PendingRefreshTokenExpiresAt;
        connection.Status = GitHubOAuthConnectionStatus.Active;
        connection.RevokedAt = null;
        connection.UpdatedAt = now;
        connection.Version = Guid.NewGuid();

        flow.Status = GitHubOAuthFlowStatus.Completed;
        flow.CompletedAt = now;
        flow.Version = Guid.NewGuid();
        ClearPendingCredential(flow);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when
            (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(ct);
            throw Error("GITHUB_CONNECTION_CONFLICT",
                "This GitHub account is already connected to this UniNet account.", 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            throw Error("OAUTH_FLOW_ALREADY_USED", "This OAuth flow has already been completed.", 409);
        }
        return ToResponse(connection);
    }

    public async Task<IReadOnlyList<GitHubOAuthConnectionResponse>> GetConnectionsAsync(
        Guid accountId, CancellationToken ct)
    {
        await RequireAccountAsync(accountId, ct);
        var connections = await db.GitHubOAuthConnections.AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderByDescending(x => x.ConnectedAt)
            .ToListAsync(ct);
        return connections.Select(ToResponse).ToArray();
    }

    public async Task DisconnectAsync(Guid accountId, Guid connectionId, CancellationToken ct)
    {
        var profileId = await RequireAccountAsync(accountId, ct);
        var connection = await db.GitHubOAuthConnections
            .SingleOrDefaultAsync(x => x.Id == connectionId && x.AccountId == accountId, ct)
            ?? throw Error("GITHUB_CONNECTION_NOT_FOUND", "GitHub connection not found.", 404);
        if (connection.Status == GitHubOAuthConnectionStatus.Revoked) return;

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        connection.Status = GitHubOAuthConnectionStatus.Revoked;
        connection.RevokedAt = now;
        connection.UpdatedAt = now;
        connection.Version = Guid.NewGuid();
        connection.AccessTokenEncrypted = "";
        connection.RefreshTokenEncrypted = null;

        var affectedLinks = await db.ProjectGitRepositories
            .Where(x => x.OAuthConnectionId == connectionId && x.UnlinkedAt == null &&
                x.AccessStatus != ProjectGitRepositoryAccessStatus.PendingAuthorization)
            .ToListAsync(ct);
        foreach (var link in affectedLinks)
        {
            link.AccessStatus = ProjectGitRepositoryAccessStatus.PendingAuthorization;
            link.UpdatedAt = now;
            link.Version = Guid.NewGuid();
            db.ProjectGitRepositoryHistory.Add(new ProjectGitRepositoryHistory
            {
                ProjectGitRepositoryId = link.Id,
                ActorProfileId = profileId,
                Action = ProjectGitRepositoryHistoryAction.AuthorizationPending,
                PreviousOAuthConnectionId = connectionId,
                NewOAuthConnectionId = connectionId,
                Details = "OAuth connection disconnected."
            });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<Guid> RequireAccountAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.Account.Status == AccountStatus.Active)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);
        return profile ?? throw Error("ACCOUNT_NOT_AVAILABLE", "An active UniNet account profile is required.", 403);
    }

    private void EnsureOAuthConfigured()
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret) ||
            string.IsNullOrWhiteSpace(settings.KeyRingPath) || settings.FlowLifetimeMinutes is < 1 or > 30 ||
            settings.Scopes is null || settings.Scopes.Length == 0 || settings.Scopes.Any(string.IsNullOrWhiteSpace) ||
            string.Join(' ', settings.Scopes).Length > 1000 ||
            settings.ReturnTargets is null || settings.ReturnTargets.Count == 0 ||
            settings.ReturnTargets.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Key.Length > 64 ||
                string.IsNullOrWhiteSpace(x.Value)) ||
            settings.MobileRedirectSchemes is null ||
            settings.MobileRedirectSchemes.Any(scheme => !Uri.CheckSchemeName(scheme) ||
                scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw Error("GITHUB_NOT_CONFIGURED", "GitHub OAuth is not configured for this environment.", 503);
        if (!Directory.Exists(settings.KeyRingPath))
            throw Error("GITHUB_NOT_CONFIGURED", "The persistent GitHub Data Protection key-ring directory is unavailable.", 503);
        ValidateCallbackUri(settings.CallbackUrl);
        ValidateHttpsUri(settings.AuthorizationEndpoint, "AuthorizationEndpoint", "github.com");
        ValidateHttpsUri(settings.TokenEndpoint, "TokenEndpoint", "github.com");
        ValidateHttpsUri(settings.ApiBaseUrl, "ApiBaseUrl", "api.github.com");
    }

    private Uri BuildAuthorizationUri(string state)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = settings.CallbackUrl,
            ["scope"] = string.Join(' ', settings.Scopes),
            ["state"] = state,
            ["allow_signup"] = "false"
        };
        return new Uri(QueryHelpers.AddQueryString(settings.AuthorizationEndpoint!, parameters));
    }

    private Uri GetReturnUri(string target)
    {
        if (!settings.ReturnTargets.TryGetValue(target, out var value))
            throw Error("GITHUB_NOT_CONFIGURED", "The configured OAuth return target is unavailable.", 503);
        ValidateReturnUri(value);
        return new Uri(value);
    }

    private static void ValidateCallbackUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw Error("GITHUB_NOT_CONFIGURED",
                "GitHub OAuth setting 'CallbackUrl' must be an absolute HTTPS URL (or loopback HTTP for development).", 503);
    }

    private void ValidateReturnUri(string? value)
    {
        var unsafeSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "javascript", "data", "file", "vbscript", "about" };
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) &&
             (unsafeSchemes.Contains(uri.Scheme) || settings.MobileRedirectSchemes is null ||
              !settings.MobileRedirectSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw Error("GITHUB_NOT_CONFIGURED",
                "Each GitHub OAuth return target must be an allowlisted HTTPS, loopback HTTP, or mobile deep-link URL.", 503);
    }

    private static void ValidateHttpsUri(string? value, string setting, string allowedHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.Equals(uri.IdnHost, allowedHost, StringComparison.OrdinalIgnoreCase))
            throw Error("GITHUB_NOT_CONFIGURED",
                $"GitHub OAuth setting '{setting}' must use the official {allowedHost} HTTPS host.", 503);
    }

    private static GitHubOAuthCallbackResult RedirectResult(Uri target, Guid flowId, string status)
    {
        var redirect = new Uri(QueryHelpers.AddQueryString(target.ToString(), new Dictionary<string, string?>
        {
            ["githubFlowId"] = flowId.ToString("D"),
            ["githubStatus"] = status
        }));
        return new(redirect);
    }

    private async Task SetFlowStatusAsync(Guid id, GitHubOAuthFlowStatus status, CancellationToken ct)
    {
        var flow = await db.GitHubOAuthAuthorizationFlows.SingleAsync(x => x.Id == id, ct);
        if (flow.Status != GitHubOAuthFlowStatus.Processing) return;
        flow.Status = status;
        flow.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }

    private static string HashState(string state) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static DateTimeOffset? Expiration(DateTimeOffset now, int? seconds) =>
        seconds is > 0 ? now.AddSeconds(seconds.Value) : null;

    private static void ClearPendingCredential(GitHubOAuthAuthorizationFlow flow)
    {
        flow.PendingProviderAccountId = null;
        flow.PendingLogin = null;
        flow.PendingDisplayName = null;
        flow.PendingAvatarUrl = null;
        flow.PendingAccessTokenEncrypted = null;
        flow.PendingRefreshTokenEncrypted = null;
        flow.PendingGrantedScopes = null;
        flow.PendingAccessTokenExpiresAt = null;
        flow.PendingRefreshTokenExpiresAt = null;
    }

    private static GitHubOAuthConnectionResponse ToResponse(GitHubOAuthConnection connection) =>
        new(connection.Id, connection.ProviderAccountId, connection.Login, connection.DisplayName, connection.AvatarUrl,
            connection.GrantedScopes.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            connection.Status, connection.AccessTokenExpiresAt, connection.RefreshTokenExpiresAt,
            connection.ConnectedAt, connection.UpdatedAt);

    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
