using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppJwtTests
{
    [Fact]
    public void JwtIsRs256SignedAndUsesGitHubTimeBounds()
    {
        using var rsa = RSA.Create(2048);
        var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        var jwt = GitHubAppJwt.Create("test-client-id", rsa.ExportPkcs8PrivateKeyPem(), now);
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        Assert.Equal(SecurityAlgorithms.RsaSha256, token.Header.Alg);
        Assert.Equal("test-client-id", token.Issuer);
        Assert.Equal(now.AddSeconds(-60).ToUnixTimeSeconds(), Convert.ToInt64(token.Payload["iat"]));
        Assert.Equal(now.AddMinutes(9).ToUnixTimeSeconds(), Convert.ToInt64(token.Payload["exp"]));
        handler.ValidateToken(jwt, new TokenValidationParameters
        {
            IssuerSigningKey = new RsaSecurityKey(rsa), ValidateIssuerSigningKey = true,
            ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = false,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        }, out _);
    }

    [Fact]
    public void MissingIssuerOrInvalidKeyIsRejected()
    {
        using var rsa = RSA.Create(2048);
        Assert.Throws<ArgumentException>(() => GitHubAppJwt.Create(" ", rsa.ExportPkcs8PrivateKeyPem(), DateTimeOffset.UtcNow));
        Assert.ThrowsAny<Exception>(() => GitHubAppJwt.Create("test", "invalid", DateTimeOffset.UtcNow));
    }
}
