using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class GitHubOAuthFlowCleanupService(UniNetDbContext db)
{
    private const int BatchSize = 500;

    public async Task<int> ClearExpiredPendingCredentialsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredFlows = await db.GitHubOAuthAuthorizationFlows
            .Where(x => x.ExpiresAt <= now &&
                x.Status == GitHubOAuthFlowStatus.AwaitingCompletion &&
                x.PendingAccessTokenEncrypted != null)
            .OrderBy(x => x.ExpiresAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var flow in expiredFlows)
        {
            flow.Status = GitHubOAuthFlowStatus.Expired;
            flow.PendingProviderAccountId = null;
            flow.PendingLogin = null;
            flow.PendingDisplayName = null;
            flow.PendingAvatarUrl = null;
            flow.PendingAccessTokenEncrypted = null;
            flow.PendingRefreshTokenEncrypted = null;
            flow.PendingGrantedScopes = null;
            flow.PendingAccessTokenExpiresAt = null;
            flow.PendingRefreshTokenExpiresAt = null;
            flow.Version = Guid.NewGuid();
        }

        if (expiredFlows.Count > 0) await db.SaveChangesAsync(ct);
        return expiredFlows.Count;
    }
}
