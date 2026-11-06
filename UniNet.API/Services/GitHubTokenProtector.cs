using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using UniNet.Application.Common;
using UniNet.Application.Interfaces;

namespace UniNet.API.Services;

public sealed class GitHubTokenProtector(IDataProtectionProvider provider) : IGitHubTokenProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("UniNet.GitHubOAuth.Token.v1");

    public string Protect(string token)
    {
        try
        {
            return protector.Protect(token);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new GitHubIntegrationException(
                "GITHUB_KEYRING_UNAVAILABLE", "GitHub credential encryption is unavailable.", 503);
        }
    }

    public string Unprotect(string protectedToken)
    {
        try
        {
            return protector.Unprotect(protectedToken);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new GitHubIntegrationException(
                "GITHUB_KEYRING_UNAVAILABLE", "GitHub credential decryption is unavailable.", 503);
        }
    }
}
