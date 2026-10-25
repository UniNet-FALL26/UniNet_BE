using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UniNet.Application.Interfaces;

namespace UniNet.Application.Services;

public sealed class LiveKitTokenService(
    IWorkspaceAccessService workspaceAccess,
    IConfiguration configuration) : ILiveKitTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);

    public async Task<LiveKitTokenResponse> CreateProjectRoomTokenAsync(
        Guid projectId, Guid accountId, CancellationToken ct = default)
    {
        var profile = await workspaceAccess.RequireMemberAsync(projectId, accountId, ct);
        var url = RequireLiveKitUrl();
        var apiKey = RequireConfiguration("LIVEKIT_API_KEY", "LiveKit:ApiKey");
        var apiSecret = RequireConfiguration("LIVEKIT_API_SECRET", "LiveKit:ApiSecret");
        var secretBytes = Encoding.UTF8.GetBytes(apiSecret);
        if (secretBytes.Length < 32)
            throw ConfigurationUnavailable();

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(TokenLifetime);
        var roomName = $"uninet-project-{projectId:N}";
        var identity = profile.Id.ToString("D");
        var payload = new JwtPayload
        {
            [JwtRegisteredClaimNames.Iss] = apiKey,
            [JwtRegisteredClaimNames.Sub] = identity,
            [JwtRegisteredClaimNames.Nbf] = now.ToUnixTimeSeconds(),
            [JwtRegisteredClaimNames.Exp] = expiresAt.ToUnixTimeSeconds(),
            ["video"] = new Dictionary<string, object>
            {
                ["room"] = roomName,
                ["roomJoin"] = true,
                ["canSubscribe"] = true,
                ["canPublish"] = true,
                ["canPublishData"] = false,
                ["canPublishSources"] = new[] { "camera", "microphone" }
            }
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(secretBytes),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(new JwtHeader(credentials), payload);
        var encodedToken = new JwtSecurityTokenHandler().WriteToken(token);

        return new(url, encodedToken, roomName, identity, expiresAt);
    }

    private string RequireLiveKitUrl()
    {
        var value = configuration["LIVEKIT_URL"] ?? configuration["LiveKit:Url"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeWss &&
             !(uri.Scheme == Uri.UriSchemeWs && uri.IsLoopback)) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 ||
            uri.AbsolutePath != "/")
            throw ConfigurationUnavailable();
        return value!;
    }

    private string RequireConfiguration(string environmentName, string sectionName)
    {
        var value = configuration[environmentName] ?? configuration[sectionName];
        return string.IsNullOrWhiteSpace(value) ? throw ConfigurationUnavailable() : value;
    }

    private static ProjectException ConfigurationUnavailable() => new(
        "LIVEKIT_NOT_CONFIGURED",
        "LiveKit is not configured with a valid secure URL, API key, and API secret.",
        503);
}
