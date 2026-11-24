using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UniNet.API;
using UniNet.API.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class RuntimeWorkerConfigurationTests
{
    [Fact]
    public void SharedDatabaseInspectionDisablesPersistentWorkersAndRetainsSessionRevocation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BackgroundWorkers:Enabled"] = "false",
            ["GitHubSync:Enabled"] = "true" // The registration gate must win even over a stale feature flag.
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<AccountSessionConnections>();
        services.AddRuntimeWorkers(configuration, isE2E: false);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<AccountSessionConnections>(Assert.Single(provider.GetServices<IHostedService>()));
    }

    [Fact]
    public void MissingMasterFlagPreservesAllExistingWorkerRegistrations()
    {
        var services = new ServiceCollection();
        services.AddRuntimeWorkers(new ConfigurationBuilder().Build(), isE2E: false);
        var workers = services.Where(x => x.ServiceType == typeof(IHostedService)).ToArray();
        Assert.Equal(8, workers.Length);
        Assert.Contains(workers, x => x.ImplementationType == typeof(GitSyncWorker));
        Assert.Contains(workers, x => x.ImplementationType == typeof(NotificationBackgroundWorker));
        Assert.Contains(workers, x => x.ImplementationType == typeof(ExpiredTokenCleanup));
        Assert.Contains(workers, x => x.ImplementationType == typeof(PortraitCleanup));
        Assert.Single(workers.Where(x => x.ImplementationFactory is not null));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ExistingE2EIsolationRetainsScopedGitRegistrationOnlyWhenMasterAllowsIt(bool enabled, int expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BackgroundWorkers:Enabled"] = enabled.ToString(),
            ["GitHubE2E:DisableUnrelatedWorkers"] = "true"
        }).Build();
        var services = new ServiceCollection();
        services.AddRuntimeWorkers(configuration, isE2E: true);
        var workers = services.Where(x => x.ServiceType == typeof(IHostedService)).ToArray();
        Assert.Equal(expected, workers.Length);
        if (enabled) Assert.Equal(typeof(GitSyncWorker), Assert.Single(workers).ImplementationType);
    }
}
