using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application.Common;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class CommunityOpportunityTests
{
    [Fact]
    public async Task NewestSortSelectsThreeLatestOpenJobsBeforePagination()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var student = new Account { Email = "student@opportunities.test", Role = AccountRole.Student, Profile = new UserProfile { FullName = "Student", Nickname = "student" } };
        var partner = new Account { Email = "partner@opportunities.test", Role = AccountRole.Partner, Profile = new UserProfile { FullName = "Partner", Nickname = "partner" } };
        db.Accounts.AddRange(student, partner);
        var now = DateTimeOffset.UtcNow;
        Job Job(string title, int age) => new() { Partner = partner.Profile!, Title = title, Status = (short)PartnerJobStatus.Published, CreatedAt = now.AddDays(-age) };
        var oldest = Job("Old but closing soon", 10); oldest.Deadline = now.AddDays(1);
        var first = Job("Newest", 1);
        var second = Job("Second", 2);
        var third = Job("Third", 3);
        var expired = Job("Expired", 0); expired.Deadline = now.AddHours(-1);
        var future = Job("Scheduled", 0); future.StartAt = now.AddDays(1);
        var draft = Job("Draft", 0); draft.Status = (short)PartnerJobStatus.Draft;
        db.Jobs.AddRange(oldest, first, second, third, expired, future, draft);
        await db.SaveChangesAsync();
        var service = new StudentOpportunityService(db, new PortfolioService(db));

        var newest = await service.ListAsync(student.Id, "all", null, null, 1, 3, default, "newest");
        Assert.Equal(new[] { first.Id, second.Id, third.Id }, newest.Jobs.Select(job => job.Id));
        Assert.Equal(4, newest.TotalItems);
        var nextPage = await service.ListAsync(student.Id, "all", null, null, 2, 3, default, "newest");
        Assert.Equal(oldest.Id, Assert.Single(nextPage.Jobs).Id);
        var existing = await service.ListAsync(student.Id, "all", null, null, 1, 3, default);
        Assert.Equal(oldest.Id, existing.Jobs[0].Id);
        var invalid = await Assert.ThrowsAsync<PartnerException>(() => service.ListAsync(student.Id, "all", null, null, 1, 3, default, "random"));
        Assert.Equal("INVALID_SORT", invalid.Code);
    }
}
