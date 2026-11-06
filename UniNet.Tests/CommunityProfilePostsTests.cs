using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities.Community;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class CommunityProfilePostsTests
{
    [Fact]
    public async Task OwnPostsUseProfileIdentityExcludeOtherAuthorsAndModeratedDeletedContent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = "owner@community.test", Role = AccountRole.Assessor, Profile = new() { FullName = "Expert", Nickname = "expert" } };
        var other = new Account { Email = "other@community.test", Role = AccountRole.Student, Profile = new() { FullName = "Other", Nickname = "other" } };
        db.AddRange(owner, other); await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        CommunityPost Post(Account author, short status, int minute, bool deleted = false) => new() { Id = Guid.NewGuid(), AuthorUser = author.Profile!, Description = $"Post {minute}", Status = status, IsDeleted = deleted, CreatedAt = now.AddMinutes(minute), UpdatedAt = now };
        var old = Post(owner, 1, -2); var newest = Post(owner, 1, -1);
        db.AddRange(old, newest, Post(other, 1, 0), Post(owner, 0, 1), Post(owner, 2, 2), Post(owner, 3, 3), Post(owner, 1, 4, true)); await db.SaveChangesAsync();
        var service = new CommunityService(db, null!);
        var first = Assert.Single(await service.GetOwnPostsAsync(owner.Id, 1, 1)); Assert.Equal(newest.Id, first.Id); Assert.Equal(owner.Profile!.Id, first.AuthorUserId);
        Assert.Equal(old.Id, Assert.Single(await service.GetOwnPostsAsync(owner.Id, 2, 1)).Id);
        Assert.Empty(await service.GetOwnPostsAsync(owner.Id, 3, 1));
        Assert.Equal(3, (await service.GetFeedAsync(owner.Id, 1, 10)).Count);
        var publicPosts = await service.GetFeedAsync(other.Id, 1, 10, owner.Profile.Id);
        Assert.Equal(2, publicPosts.Count);
        Assert.All(publicPosts, post => Assert.Equal(owner.Profile.Id, post.AuthorUserId));
        Assert.Equal(newest.Id, Assert.Single(await service.GetFeedAsync(other.Id, 1, 1, owner.Profile.Id)).Id);
        Assert.Equal(old.Id, Assert.Single(await service.GetFeedAsync(other.Id, 2, 1, owner.Profile.Id)).Id);
        Assert.Equal(400, (await Assert.ThrowsAsync<CommunityException>(() => service.GetOwnPostsAsync(owner.Id, 0, 10))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<CommunityException>(() => service.GetOwnPostsAsync(owner.Id, 1, 51))).Status);
        owner.Status = AccountStatus.Suspended; await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityException>(() => service.GetOwnPostsAsync(owner.Id, 1, 10))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<CommunityException>(() => service.GetOwnPostsAsync(Guid.NewGuid(), 1, 10))).Status);
    }
}
