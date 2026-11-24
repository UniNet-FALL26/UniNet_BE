using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Application.DTOs.Community;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Community;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class CommunityNotificationTests
{
    [Theory]
    [InlineData(false, ReportStatus.Resolved, CommunityContentStatus.Published)]
    [InlineData(true, ReportStatus.Dismissed, CommunityContentStatus.Hidden)]
    public async Task StaffReviewSetsReportAndPostStatus(
        bool isViolation, ReportStatus expectedReportStatus, CommunityContentStatus expectedPostStatus)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var moderator = new Account { Email = "moderator@review.test", Role = AccountRole.Moderator };
        var student = new Account { Email = "student@review.test", Role = AccountRole.Student,
            Profile = new UserProfile { FullName = "Student", Nickname = "student" } };
        db.Accounts.AddRange(moderator, student);
        var post = new CommunityPost { Id = Guid.NewGuid(), AuthorUser = student.Profile!,
            Status = (short)CommunityContentStatus.Published, Description = "Reported post" };
        var report = new ContentReport { Id = Guid.NewGuid(), Post = post, ReporterUser = student.Profile!,
            Reason = "Spam", Status = (short)ReportStatus.Open };
        db.ContentReports.Add(report);
        await db.SaveChangesAsync();
        var service = new CommunityService(db, null!);

        var forbidden = await Assert.ThrowsAsync<CommunityException>(() =>
            service.ReviewContentReportAsync(student.Id, report.Id, isViolation, default));
        Assert.Equal(403, forbidden.Status);
        var result = await service.ReviewContentReportAsync(moderator.Id, report.Id, isViolation, default);
        Assert.Equal((short)expectedReportStatus, result.Status);
        Assert.Equal((short)expectedPostStatus, result.PostStatus);
        Assert.Equal(result.Status, (await service.GetContentReportAsync(moderator.Id, report.Id, default)).Status);
        Assert.Equal(result.Status, (await service.ReviewContentReportAsync(moderator.Id, report.Id, isViolation, default)).Status);
        var conflict = await Assert.ThrowsAsync<CommunityException>(() =>
            service.ReviewContentReportAsync(moderator.Id, report.Id, !isViolation, default));
        Assert.Equal(409, conflict.Status);
    }

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
