using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectGitIdentityModelTests
{
    [Fact]
    public void IdentityModelHasProjectScopedUniqueActiveOwnerIndex()
    {
        using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var entity = db.Model.FindEntityType(typeof(ProjectGitMemberIdentity))!;
        var ownerIndex = entity.GetIndexes().Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["ProjectId", "Provider", "ExternalAccountId"]));

        Assert.True(ownerIndex.IsUnique);
        Assert.Equal("\"Status\" = 0", ownerIndex.GetFilter());
    }

    [Fact]
    public void IdentityHistoryReferencesOwnerProjectAndIdentityWithoutCascadeDeletes()
    {
        using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var entity = db.Model.FindEntityType(typeof(ProjectGitMemberIdentityHistory))!;
        var foreignKeys = entity.GetForeignKeys();

        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior));
        Assert.Contains(foreignKeys, foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ProjectGitMemberIdentity));
        Assert.Contains(foreignKeys, foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Project));
        Assert.Contains(foreignKeys, foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(UserProfile));
        Assert.Contains(foreignKeys, foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(GitHubOAuthConnection));
    }
}
