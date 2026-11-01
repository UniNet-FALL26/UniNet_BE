using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.DTOs.Community;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Community;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class CommunityNotificationTests
{
    [Fact]
    public async Task ReportNotifiesOnlyStaffWithCommunityReviewPermission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        Account User(string name, AccountRole role) => new() { Email = $"{name}@community-notice.test", Role = role,
            Profile = new UserProfile { FullName = name, Nickname = name } };
        var reporter = User("reporter", AccountRole.Student);
        var moderator = User("moderator", AccountRole.Moderator);
        var deniedAdmin = User("admin", AccountRole.Admin);
        db.Accounts.AddRange(reporter, moderator, deniedAdmin);
        db.Set<NotificationPermissionOverride>().Add(new() { AccountId = deniedAdmin.Id,
            Permission = NotificationPermissionPolicy.CommunityReview, IsGranted = false });
        var post = new CommunityPost { Id = Guid.NewGuid(), AuthorUser = reporter.Profile!, Description = "Post" };
        db.CommunityPosts.Add(post);
        await db.SaveChangesAsync();
        await new CommunityService(db, null!).ReportPostAsync(post.Id, reporter.Id, new CreateReportRequest { Reason = "Spam" });
        var report = await db.ContentReports.SingleAsync();
        var notice = await db.Set<VerificationNotification>().SingleAsync();
        Assert.Equal(moderator.Id, notice.RecipientUserId);
        Assert.Equal("ContentReport", notice.TargetType);
        Assert.Equal(report.Id, notice.TargetId);
        Assert.Equal(NotificationPermissionPolicy.CommunityReview, notice.RequiredPermission);
        Assert.Equal(reporter.Id, notice.SenderUserId);
    }
}
