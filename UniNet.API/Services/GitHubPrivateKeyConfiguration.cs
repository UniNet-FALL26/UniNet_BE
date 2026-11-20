using System.Security.Cryptography;
using System.Text;

namespace UniNet.API.Services;

public static class GitHubPrivateKeyConfiguration
{
    public static void Apply(ConfigurationManager configuration, string contentRoot)
    {
        // Existing E2E/hosted flows inject PEM directly. Never replace that explicit credential.
        if (!string.IsNullOrWhiteSpace(configuration["GitHubApp:PrivateKeyPem"])) return;
        var path = configuration["GitHubApp:PrivateKeyPath"];
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var resolved = Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(Path.Combine(contentRoot, path));
            using var file = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is < 1 or > 65536) throw new InvalidDataException();
            using var reader = new StreamReader(file, new UTF8Encoding(false, true));
            var pem = reader.ReadToEnd();
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048 || rsa.ExportParameters(true).D is null) throw new CryptographicException();
            configuration["GitHubApp:PrivateKeyPem"] = pem;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            CryptographicException or NotSupportedException or DecoderFallbackException)
        {
            // No path, PEM, password or inner exception enters startup logs.
            throw new InvalidOperationException("GitHub App private-key file is unavailable or invalid.");
        }
    }
}
