using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace UniNet.Application.Services;

/// <summary>Creates a short-lived GitHub App authentication JWT. Never expose it to clients.</summary>
public static class GitHubAppJwt
{
    public static string Create(string clientId, string privateKeyPem, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signingKey = new RsaSecurityKey(rsa)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var header = new JwtHeader(new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        var payload = new JwtPayload
        {
            ["iss"] = clientId,
            ["iat"] = now.AddSeconds(-60).ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(9).ToUnixTimeSeconds()
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}
