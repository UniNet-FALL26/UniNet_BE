using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using UniNet.API.Services;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubPrivateKeyConfigurationTests
{
    [Fact]
    public void MissingFileSettingLeavesDisabledAppConfigurationUntouched()
    {
        var configuration = new ConfigurationManager();
        GitHubPrivateKeyConfiguration.Apply(configuration, Path.GetTempPath());
        Assert.Null(configuration["GitHubApp:PrivateKeyPem"]);
    }

    [Fact]
    public void ExistingInjectedPemWinsWithoutReadingAnUnavailableFile()
    {
        var configuration = new ConfigurationManager();
        configuration["GitHubApp:PrivateKeyPem"] = "existing-injected-value";
        configuration["GitHubApp:PrivateKeyPath"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing");
        GitHubPrivateKeyConfiguration.Apply(configuration, Path.GetTempPath());
        Assert.Equal("existing-injected-value", configuration["GitHubApp:PrivateKeyPem"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingPemFileIsLoadedAndBoundWithoutChangingItsBytes(bool relative)
    {
        var directory = Path.Combine(Path.GetTempPath(), "uninet-pem-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "key.pem");
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(path, pem);
        try
        {
            var configuration = new ConfigurationManager();
            configuration["GitHubApp:PrivateKeyPath"] = relative ? "key.pem" : path;
            GitHubPrivateKeyConfiguration.Apply(configuration, directory);
            Assert.True(pem == configuration.GetSection("GitHubApp").Get<GitHubAppOptions>()!.PrivateKeyPem);
            Assert.True(pem == File.ReadAllText(path));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(65537)]
    public void InvalidFileFailsWithRedactedStartupError(int length)
    {
        var path = Path.Combine(Path.GetTempPath(), "uninet-private-fixture-" + Guid.NewGuid() + ".pem");
        File.WriteAllText(path, new string('x', length));
        try
        {
            var configuration = new ConfigurationManager();
            configuration["GitHubApp:PrivateKeyPath"] = path;
            var error = Assert.Throws<InvalidOperationException>(() =>
                GitHubPrivateKeyConfiguration.Apply(configuration, Path.GetTempPath()));
            Assert.Equal("GitHub App private-key file is unavailable or invalid.", error.Message);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain(path, error.ToString());
            Assert.Null(configuration["GitHubApp:PrivateKeyPem"]);
        }
        finally { File.Delete(path); }
    }
}
