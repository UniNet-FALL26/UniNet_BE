using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Services;

public static class AccountTokenValidation
{
    public static async Task<bool> ValidateAsync(ClaimsPrincipal? user, UniNetDbContext db, CancellationToken ct)
    {
        return TrySession(user, out var accountId, out var version) && await AccountSessions.IsValidAsync(db, accountId, version, ct);
    }
    public static bool TrySession(ClaimsPrincipal? user, out Guid accountId, out int version)
    {
        version = 0;
        if (!Guid.TryParse(user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out accountId) || accountId == Guid.Empty) return false;
        var claim = user?.FindFirst("session_version")?.Value;
        return claim == null || int.TryParse(claim, out version) && version >= 0;
    }
}
