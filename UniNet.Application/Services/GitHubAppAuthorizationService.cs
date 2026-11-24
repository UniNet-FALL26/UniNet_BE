using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubAppAuthorizationService(
    UniNetDbContext db, IGitHubAppUserClient client, IGitHubTokenProtector protector,
    IOptions<GitHubAppOptions> options) : IGitHubAppAuthorizationService
{
    public async Task<StartGitHubAppAuthorizationResponse> StartAsync(Guid accountId, Guid projectId,
        StartGitHubAppAuthorizationRequest request, CancellationToken ct)
    {
        GitHubAppAvailability.Require(options);
        var settings = options.Value;
        ValidateConfiguration(settings);
        await ProjectGitAuthorization.RequireManagerAsync(db, projectId, accountId, ct);
        var connection = await db.GitHubOAuthConnections.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == request.OAuthConnectionId && x.AccountId == accountId && x.Status == GitHubOAuthConnectionStatus.Active, ct)
            ?? throw Error("GITHUB_APP_IDENTITY_REQUIRED", "An active GitHub identity connection is required.", 403);
        if (!long.TryParse(connection.ProviderAccountId, NumberStyles.None, CultureInfo.InvariantCulture, out var providerId) || providerId <= 0)
            throw Error("GITHUB_APP_IDENTITY_REQUIRED", "An active GitHub identity connection is required.", 403);
        _ = ReturnTarget(settings, request.ReturnTarget);
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var flow = new GitHubAppAuthorizationFlow { AccountId = accountId, ProjectId = projectId,
            OAuthConnectionId = connection.Id, StateHash = Hash(state), ReturnTarget = request.ReturnTarget,
            CreatedAt = now, ExpiresAt = now.AddMinutes(settings.FlowLifetimeMinutes) };
        db.Set<GitHubAppAuthorizationFlow>().Add(flow);
        await db.SaveChangesAsync(ct);
        var url = QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", new Dictionary<string, string?>
        { ["client_id"] = settings.ClientId, ["redirect_uri"] = settings.CallbackUrl, ["state"] = state });
        return new(flow.Id, url, flow.ExpiresAt);
    }

    public async Task<GitHubAppAuthorizationCallbackResult> CallbackAsync(string? state, string? code, string? providerError, CancellationToken ct)
    {
        GitHubAppAvailability.Require(options);
        var settings = options.Value;
        ValidateConfiguration(settings);
        if (string.IsNullOrWhiteSpace(state) || state.Length > 256) throw Error("INVALID_APP_STATE", "Invalid or expired authorization state.", 400);
        var hash = Hash(state);
        var flow = await db.Set<GitHubAppAuthorizationFlow>().SingleOrDefaultAsync(x => x.StateHash == hash, ct)
            ?? throw Error("INVALID_APP_STATE", "Invalid or expired authorization state.", 400);
        var target = ReturnTarget(settings, flow.ReturnTarget);
        if (flow.Status != GitHubAppAuthorizationStatus.Pending || flow.ExpiresAt <= DateTimeOffset.UtcNow)
            throw Error("INVALID_APP_STATE", "Authorization state is expired or already used.", 409);
        flow.Status = GitHubAppAuthorizationStatus.Processing;
        flow.Version = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Error("INVALID_APP_STATE", "Authorization state is already used.", 409); }

        try
        {
            if (!string.IsNullOrWhiteSpace(providerError) || string.IsNullOrWhiteSpace(code) || code.Length > 4096)
                throw Error("GITHUB_APP_AUTH_FAILED", "GitHub authorization did not complete.", 400);
            var tokens = await client.ExchangeAsync(code, ct);
            var user = await client.GetUserAsync(tokens.AccessToken, ct);
            await ProjectGitAuthorization.RequireManagerAsync(db, flow.ProjectId, flow.AccountId, ct);
            var matching = await db.GitHubOAuthConnections.AsNoTracking().AnyAsync(x => x.Id == flow.OAuthConnectionId &&
                x.AccountId == flow.AccountId && x.Status == GitHubOAuthConnectionStatus.Active &&
                x.ProviderAccountId == user.Id.ToString(CultureInfo.InvariantCulture), ct);
            if (!matching) throw Error("GITHUB_APP_USER_MISMATCH", "GitHub user does not match the linked identity.", 403);
            if (tokens.ExpiresIn is <= 0 || tokens.RefreshTokenExpiresIn is <= 0)
                throw Error("GITHUB_APP_INVALID_RESPONSE", "GitHub returned invalid token expiry.", 502);
            var credential = await db.Set<GitHubAppUserCredential>().SingleOrDefaultAsync(x =>
                x.AccountId == flow.AccountId && x.GitHubUserId == user.Id, ct);
            if (credential is null)
            {
                credential = new() { AccountId = flow.AccountId, GitHubUserId = user.Id };
                db.Add(credential);
            }
            var now = DateTimeOffset.UtcNow;
            credential.Login = user.Login;
            credential.AccessTokenEncrypted = protector.Protect(tokens.AccessToken);
            credential.RefreshTokenEncrypted = tokens.RefreshToken is null ? null : protector.Protect(tokens.RefreshToken);
            credential.AccessTokenExpiresAt = tokens.ExpiresIn is { } seconds ? now.AddSeconds(seconds) : null;
            credential.RefreshTokenExpiresAt = tokens.RefreshTokenExpiresIn is { } refreshSeconds ? now.AddSeconds(refreshSeconds) : null;
            credential.UpdatedAt = now;
            flow.Status = GitHubAppAuthorizationStatus.Completed;
            flow.CompletedAt = now;
            flow.Version = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            return Redirect(target, flow.Id, "completed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            var failed = await db.Set<GitHubAppAuthorizationFlow>().SingleAsync(x => x.Id == flow.Id, CancellationToken.None);
            if (failed.Status == GitHubAppAuthorizationStatus.Processing)
            {
                failed.Status = GitHubAppAuthorizationStatus.Failed;
                failed.Version = Guid.NewGuid();
                await db.SaveChangesAsync(CancellationToken.None);
            }
            return Redirect(target, flow.Id, "failed");
        }
    }

    private static GitHubAppAuthorizationCallbackResult Redirect(Uri target, Guid flow, string status) =>
        new(new Uri(QueryHelpers.AddQueryString(target.ToString(), new Dictionary<string, string?>
        { ["githubAppFlowId"] = flow.ToString("D"), ["githubAppStatus"] = status })));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static Uri ReturnTarget(GitHubAppOptions settings, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !settings.ReturnTargets.TryGetValue(name, out var value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw Error("INVALID_APP_RETURN_TARGET", "The requested return target is not allowed.", 400);
        return uri;
    }
    private static void ValidateConfiguration(GitHubAppOptions s)
    {
        if (string.IsNullOrWhiteSpace(s.ClientId) || string.IsNullOrWhiteSpace(s.ClientSecret) || s.FlowLifetimeMinutes is < 1 or > 30 ||
            !Uri.TryCreate(s.CallbackUrl, UriKind.Absolute, out var callback) ||
            (callback.Scheme != Uri.UriSchemeHttps && !(callback.Scheme == Uri.UriSchemeHttp && callback.IsLoopback)) ||
            callback.UserInfo.Length != 0 || callback.Fragment.Length != 0 || callback.Query.Length != 0)
            throw Error("GITHUB_APP_NOT_CONFIGURED", "GitHub App authorization is not configured.", 503);
    }
    private static GitHubIntegrationException Error(string code, string message, int status) => new(code, message, status);
}
