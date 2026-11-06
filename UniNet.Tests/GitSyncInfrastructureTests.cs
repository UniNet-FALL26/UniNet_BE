using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UniNet.API.Services;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitSyncInfrastructureTests
{
    [Fact]
    public async Task DisabledWorkerStopsWithoutCreatingWorkerScopes()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var worker = new GitSyncWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new GitSyncOptions { Enabled = false }),
            NullLogger<GitSyncWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void CheckpointUniquenessIsScopedToJobCategoryAndScope()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new UniNetDbContext(options);
        var entity = db.Model.FindEntityType(typeof(GitSyncCheckpoint));
        Assert.NotNull(entity);
        var uniqueIndex = Assert.Single(entity!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(x => x.Name)
                .SequenceEqual(["SyncJobId", "Category", "ScopeKey"]));
        Assert.NotNull(uniqueIndex);
    }

    [Fact]
    public void ActiveSyncJobsHaveDatabaseEnforcedPerAssociationExclusion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection)
            .Options;
        using var db = new UniNetDbContext(options);

        var entity = db.Model.FindEntityType(typeof(GitSyncJob));
        Assert.NotNull(entity);
        var activeIndex = Assert.Single(entity!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(x => x.Name)
                .SequenceEqual(["ProjectGitRepositoryId", "Status"]) &&
            index.GetFilter() is { } filter && filter.Contains("\"Status\" IN"));
        Assert.Contains("\"Status\" IN", activeIndex.GetFilter());
    }

    [Fact]
    public void SharedGitDataHasRepositoryDeduplicationAndProjectScopedVisibility()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection).Options);

        var objectType = db.Model.FindEntityType(typeof(GitHubProviderObject));
        var objectIndex = Assert.Single(objectType!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(x => x.Name)
                .SequenceEqual(["GitHubRepositoryId", "Category", "ExternalObjectId"]));
        Assert.NotNull(objectIndex);

        var grantType = db.Model.FindEntityType(typeof(ProjectGitActivityGrant));
        var grantIndex = Assert.Single(grantType!.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(x => x.Name)
                .SequenceEqual(["ProjectGitRepositoryId", "GitHubProviderObjectId"]));
        Assert.NotNull(grantIndex);
    }
}
