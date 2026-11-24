using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitHubAppPersistenceModelTests
{
    [Fact]
    public void AppPersistenceUsesUniqueKeysRestrictedForeignKeysAndStateConcurrency()
    {
        using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=design;Username=design;Password=design").Options);
        var installation = db.Model.FindEntityType(typeof(GitHubAppInstallation))!;
        var account = db.Model.FindEntityType(typeof(GitHubAppInstallationAccount))!;
        var grant = db.Model.FindEntityType(typeof(GitHubAppRepositoryGrant))!;
        var flow = db.Model.FindEntityType(typeof(GitHubAppAuthorizationFlow))!;
        Assert.Contains(installation.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["ProviderInstallationId"]));
        Assert.Contains(account.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["InstallationId", "AccountId"]));
        Assert.Contains(grant.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["InstallationId", "ProviderRepositoryId"]));
        Assert.Contains(flow.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["StateHash"]));
        Assert.True(flow.FindProperty("Version")!.IsConcurrencyToken);
        Assert.All(account.GetForeignKeys().Concat(grant.GetForeignKeys()).Concat(flow.GetForeignKeys()),
            fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
    }
}
