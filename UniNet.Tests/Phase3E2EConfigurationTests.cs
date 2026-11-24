using Microsoft.Extensions.Configuration;
using UniNet.API.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class Phase3E2EConfigurationTests
{
    [Fact]
    public void LocalEnvConfigurationCanSkipRepositoryDotEnvForIsolatedE2E()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"uninet-e2e-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        var envPath = Path.Combine(contentRoot, ".env");
        File.WriteAllText(envPath, "Phase3E2ETest__Value=from-repository-env");
        var previousSkip = Environment.GetEnvironmentVariable("UNINET_SKIP_LOCAL_ENV_FILE");
        var previousValue = Environment.GetEnvironmentVariable("Phase3E2ETest__Value");
        try
        {
            Environment.SetEnvironmentVariable("UNINET_SKIP_LOCAL_ENV_FILE", "true");
            Environment.SetEnvironmentVariable("Phase3E2ETest__Value", null);
            var configuration = new ConfigurationManager();

            LocalEnvConfiguration.Apply(configuration, contentRoot, []);

            Assert.Null(configuration["Phase3E2ETest:Value"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNINET_SKIP_LOCAL_ENV_FILE", previousSkip);
            Environment.SetEnvironmentVariable("Phase3E2ETest__Value", previousValue);
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void LocalEnvConfigurationKeepsExistingBehaviorWhenSkipFlagIsAbsent()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"uninet-e2e-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        File.WriteAllText(Path.Combine(contentRoot, ".env"), "Phase3E2ETest__Value=from-repository-env");
        var previousSkip = Environment.GetEnvironmentVariable("UNINET_SKIP_LOCAL_ENV_FILE");
        var previousValue = Environment.GetEnvironmentVariable("Phase3E2ETest__Value");
        try
        {
            Environment.SetEnvironmentVariable("UNINET_SKIP_LOCAL_ENV_FILE", null);
            Environment.SetEnvironmentVariable("Phase3E2ETest__Value", null);
            var configuration = new ConfigurationManager();

            LocalEnvConfiguration.Apply(configuration, contentRoot, []);

            Assert.Equal("from-repository-env", configuration["Phase3E2ETest:Value"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNINET_SKIP_LOCAL_ENV_FILE", previousSkip);
            Environment.SetEnvironmentVariable("Phase3E2ETest__Value", previousValue);
            Directory.Delete(contentRoot, recursive: true);
        }
    }
}
