using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UniNet.API.Services;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GitScopedManualSyncTests
{
    [Theory]
    [InlineData("global")]
    [InlineData("project")]
    [InlineData("association")]
    [InlineData("job")]
    [InlineData("deadline")]
    public async Task InvalidScopedConfigurationFailsBeforeCreatingWorkerScopes(string invalid)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var settings = new GitSyncOptions { ScopedManual = new() { Enabled = true,
            ProjectId = Guid.NewGuid(), AssociationId = Guid.NewGuid(), JobId = Guid.NewGuid() } };
        if (invalid == "global") settings.Enabled = true;
        if (invalid == "project") settings.ScopedManual.ProjectId = Guid.Empty;
        if (invalid == "association") settings.ScopedManual.AssociationId = Guid.Empty;
        if (invalid == "job") settings.ScopedManual.JobId = Guid.Empty;
        if (invalid == "deadline") settings.ScopedManual.ExecutionTimeoutSeconds = 0;
        using var worker = new GitSyncWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings), NullLogger<GitSyncWorker>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StartAsync(default));
    }

    [Fact]
    public async Task ScopedModeWithoutJobIdStopsWithoutGlobalWorkOrServiceScopes()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var worker = new GitSyncWorker(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new GitSyncOptions { ScopedManual = new() { Enabled = true,
                ProjectId = Guid.NewGuid(), AssociationId = Guid.NewGuid() } }), NullLogger<GitSyncWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("AwaitingJobId", worker.ScopedStopReason);
        await worker.StopAsync(default);
    }
}
