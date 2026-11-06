using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.API.Hubs;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class NotificationsTests
{
    [Fact]
    public async Task ReadOnlyFilterRunsBeforePaginationAndKeepsGlobalUnreadCount()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 24; i++) await f.Service.SendToUsersAsync([f.Student.Id], Content($"read-filter-{i}"), default);
        var all = await f.Service.GetMineAsync(f.Student.Id, pageSize: 100);
        var readIds = all.Items.TakeLast(3).Select(n => n.Id).ToArray();
        foreach (var id in readIds) await f.Service.ReadAsync(f.Student.Id, id);
        var first = await f.Service.GetMineAsync(f.Student.Id, pageSize: 2, readOnly: true);
        var second = await f.Service.GetMineAsync(f.Student.Id, page: 2, pageSize: 2, readOnly: true);
        Assert.Equal(3, first.Total); Assert.Equal(21, first.UnreadCount);
        Assert.Equal(2, first.Items.Count); Assert.Single(second.Items);
        Assert.All(first.Items.Concat(second.Items), n => Assert.NotNull(n.ReadAt));
        Assert.Equal(readIds.OrderBy(id => id), first.Items.Concat(second.Items).Select(n => n.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task AccountIdsOwnershipExpiryFilteringAndReadIsolation()
    {
        await using var f = await Fixture.Create();
        Assert.NotEqual(f.Student.Id, f.Student.Profile!.Id);
        await f.Service.SendToUserAsync(f.Student.Id, Content("student"));
        await f.Service.SendToUserAsync(f.Partner.Id, Content("partner") with { Category = "recruitment" });
        await f.Service.SendToUserAsync(f.Student.Id, Content("expired") with { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) });
        var page = await f.Service.GetMineAsync(f.Student.Id);
        Assert.Single(page.Items); Assert.Equal(1, page.UnreadCount);
        var partner = Assert.Single((await f.Service.GetMineAsync(f.Partner.Id)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectVerificationException>(() => f.Service.ReadAsync(f.Student.Id, partner.Id))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectVerificationException>(() => f.Service.GetOwnedAsync(f.Admin.Id, partner.Id))).Status);
        await f.Service.ReadAsync(f.Student.Id, page.Items[0].Id); await f.Service.ReadAsync(f.Student.Id, page.Items[0].Id);
        Assert.Equal(0, await f.Service.UnreadCountAsync(f.Student.Id)); Assert.Equal(1, await f.Service.UnreadCountAsync(f.Partner.Id));
        Assert.Empty((await f.Service.GetMineAsync(f.Student.Id, unreadOnly: true)).Items);
        Assert.Empty((await f.Service.GetMineAsync(f.Partner.Id, category: "project")).Items);
        await f.Service.ReadAsync(f.Partner.Id, null); Assert.Equal(0, await f.Service.UnreadCountAsync(f.Partner.Id));
    }

    [Theory]
    [InlineData("JobApplication", AccountRole.Student)]
    [InlineData("JobApplication", AccountRole.Partner)]
    [InlineData("JobPosting", AccountRole.Student)]
    [InlineData("JobPosting", AccountRole.Partner)]
    public async Task MissingRecruitmentTargetsForAccountsWithoutProfilesAreUnavailable(string targetType, AccountRole role)
    {
        await using var f = await Fixture.Create();
        var account = new Account { Email = "no-profile@example.test", Role = role };
        f.Db.Accounts.Add(account);
        await f.Db.SaveChangesAsync();
        await f.Service.SendToUserAsync(account.Id, Content("deleted-recruitment") with {
            TargetType = targetType, TargetId = Guid.NewGuid()
        });
        var notice = Assert.Single((await f.Service.GetMineAsync(account.Id)).Items);
        var error = await Assert.ThrowsAsync<ProjectVerificationException>(() =>
            new NotificationTargets(f.Db, f.Service).ResolveAsync(account.Id, notice.Id, default));
        Assert.Equal(404, error.Status);
        Assert.Equal("TARGET_UNAVAILABLE", error.Code);
    }

    [Fact]
    public async Task DedupAndDurableOutboxAreAtomicAndOfflineNotificationsRemain()
    {
        await using var f = await Fixture.Create();
        await using (var transaction = await f.Db.Database.BeginTransactionAsync()) {
            await NotificationService.EnqueueAsync(f.Db, [f.Student.Id, f.Student.Id], Content("rollback"));
            await f.Db.SaveChangesAsync(); await transaction.RollbackAsync();
        }
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.Empty(await f.Db.Set<NotificationDelivery>().ToListAsync());
        await f.Service.SendToUsersAsync([f.Student.Id, f.Student.Id, f.Partner.Id], Content("same-event"));
        await f.Service.SendToUsersAsync([f.Student.Id, f.Partner.Id], Content("same-event"));
        Assert.Equal(2, await f.Db.Set<VerificationNotification>().CountAsync());
        Assert.Equal(2, await f.Db.Set<NotificationDelivery>().CountAsync());
        Assert.All(await f.Db.Set<NotificationDelivery>().ToListAsync(), d => Assert.Null(d.DeliveredAt));
    }

    [Fact]
    public async Task PermissionRecipientsAndRevocationAreEnforcedOnReadAndTargets()
    {
        await using var f = await Fixture.Create();
        var content = Content("permission") with { TargetType = null, TargetId = null, TargetUrl = "/admin/projects" };
        await f.Service.SendToPermissionAsync(NotificationPermissionPolicy.StudentVerificationReview, content);
        Assert.Single((await f.Service.GetMineAsync(f.Admin.Id)).Items);
        Assert.Empty((await f.Service.GetMineAsync(f.Student.Id)).Items);
        Assert.Empty((await f.Service.GetMineAsync(f.Moderator.Id)).Items);
        var notice = Assert.Single((await f.Service.GetMineAsync(f.Admin.Id)).Items);
        f.Db.Set<NotificationPermissionOverride>().Add(new() { AccountId = f.Admin.Id, Permission = NotificationPermissionPolicy.StudentVerificationReview, IsGranted = false });
        await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Service.GetMineAsync(f.Admin.Id)).Items);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectVerificationException>(() => new NotificationTargets(f.Db, f.Service).ResolveAsync(f.Admin.Id, notice.Id, default))).Status);
        await f.Service.SendToPermissionAsync(NotificationPermissionPolicy.StudentVerificationReview, Content("after-revoke"));
        Assert.Single(await f.Db.Set<VerificationNotification>().ToListAsync());
    }

    [Fact]
    public async Task BroadcastUsesWorkerBatchesSharedIdAndRetryDoesNotDuplicate()
    {
        await using var f = await Fixture.Create();
        var service = new NotificationBroadcastService(f.Db);
        var draft = await service.CreateAsync(f.Admin.Id, new("Bảo trì", "Nội dung thông báo", "Selected", RecipientAccountIds: [f.Student.Id, f.Student.Id, f.Partner.Id]), default);
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.Equal(2, await service.PreviewAsync(f.Admin.Id, draft.Id, default));
        await service.PublishAsync(f.Admin.Id, draft.Id, default);
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.True(await service.ProcessNextBatchAsync(default));
        Assert.False(await service.ProcessNextBatchAsync(default));
        var notices = await f.Db.Set<VerificationNotification>().ToListAsync();
        Assert.Equal(2, notices.Count); Assert.All(notices, n => Assert.Equal(draft.Id, n.BroadcastId));
        var job = await f.Db.Set<NotificationBroadcast>().SingleAsync();
        job.Status = "Queued"; await f.Db.SaveChangesAsync();
        await service.ProcessNextBatchAsync(default);
        Assert.Equal(2, await f.Db.Set<VerificationNotification>().CountAsync());
        Assert.Equal(2, (await service.GetAsync(f.Admin.Id, draft.Id, default)).ProcessedCount);
    }

    [Fact]
    public async Task BroadcastCancellationPermissionAndRoleSelection()
    {
        await using var f = await Fixture.Create(); var service = new NotificationBroadcastService(f.Db);
        await Assert.ThrowsAsync<ProjectVerificationException>(() => service.CreateAsync(f.Student.Id, new("Title", "Message", "All"), default));
        var draft = await service.CreateAsync(f.Admin.Id, new("Title", "Message", "Role", "Partner"), default);
        Assert.Equal(1, await service.PreviewAsync(f.Admin.Id, draft.Id, default));
        await service.PublishAsync(f.Admin.Id, draft.Id, default);
        await service.CancelAsync(f.Admin.Id, draft.Id, default);
        Assert.False(await service.ProcessNextBatchAsync(default));
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        f.Db.Set<NotificationPermissionOverride>().Add(new() { AccountId = f.Admin.Id, Permission = NotificationPermissionPolicy.Manage, IsGranted = false });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ProjectVerificationException>(() => service.ListAsync(f.Admin.Id, default));
    }

    [Theory]
    [InlineData("All", null)]
    [InlineData("Staff", null)]
    [InlineData("Role", "Student")]
    [InlineData("Role", "Partner")]
    [InlineData("Role", "Assessor")]
    public async Task BroadcastAudiencePreviewAndDeliveryUseOnlyIntendedRoles(string audience, string? role)
    {
        await using var f = await Fixture.Create();
        foreach (var missing in new[] { AccountRole.Assessor, AccountRole.Verifier, AccountRole.Accountant })
            f.Db.Accounts.Add(new Account { Email = $"audience-{missing}@example.test", Role = missing });
        f.Db.Accounts.Add(new Account { Email = "inactive-audience@example.test", Role = AccountRole.Student, Status = AccountStatus.Suspended });
        f.Db.Accounts.Add(new Account { Email = "inactive-staff@example.test", Role = AccountRole.Moderator, Status = AccountStatus.Suspended });
        await f.Db.SaveChangesAsync();
        var userRoles = new[] { AccountRole.Student, AccountRole.Partner, AccountRole.Assessor };
        var expected = (await f.Db.Accounts.Where(a => a.Status == AccountStatus.Active).ToListAsync())
            .Where(a => audience == "All" ? userRoles.Contains(a.Role) : audience == "Staff" ? !userRoles.Contains(a.Role) : a.Role.ToString() == role)
            .Select(a => a.Id).OrderBy(id => id).ToArray();
        var service = new NotificationBroadcastService(f.Db);
        var draft = await service.CreateAsync(f.Admin.Id, new("Audience", "Message", audience, role), default);
        Assert.Equal(expected.Length, await service.PreviewAsync(f.Admin.Id, draft.Id, default));
        Assert.Equal(expected.Length, (await service.PublishAsync(f.Admin.Id, draft.Id, default)).RecipientCount);
        Assert.True(await service.ProcessNextBatchAsync(default));
        var delivered = await f.Db.Set<VerificationNotification>().Where(n => n.BroadcastId == draft.Id).Select(n => n.RecipientUserId).ToListAsync();
        Assert.Equal(expected, delivered.OrderBy(id => id).ToArray());
    }

    [Theory]
    [InlineData("Admin")][InlineData("Verifier")][InlineData("Moderator")][InlineData("Accountant")]
    [InlineData("0")][InlineData("6")][InlineData("Unknown")][InlineData(null)]
    public async Task BroadcastRoleAudienceRejectsNonUserRoles(string? role)
    {
        await using var f = await Fixture.Create();
        var service = new NotificationBroadcastService(f.Db);
        await Assert.ThrowsAsync<ProjectVerificationException>(() => service.CreateAsync(f.Admin.Id, new("Title", "Message", "Role", role), default));
    }

    [Fact]
    public async Task HistoricalStaffRoleAndUnknownAudienceDoNotFallBackToAllAccounts()
    {
        await using var f = await Fixture.Create();
        var service = new NotificationBroadcastService(f.Db);
        Assert.Empty(await service.Eligible(new() { Audience = "Role", Role = "Admin" }).ToListAsync());
        Assert.Empty(await service.Eligible(new() { Audience = "Unknown" }).ToListAsync());
    }

    [Fact]
    public async Task LargeBroadcastRunsInBoundedBatchesAndFailedBatchRetriesAtomically()
    {
        await using var f = await Fixture.Create();
        f.Db.Accounts.AddRange(Enumerable.Range(0, 251).Select(i => new Account { Email = $"batch-{i}@example.test", Role = AccountRole.Student }));
        await f.Db.SaveChangesAsync();
        var service = new NotificationBroadcastService(f.Db);
        var job = await service.CreateAsync(f.Admin.Id, new("Batch", "Message", "Role", "Student"), default);
        await service.PublishAsync(f.Admin.Id, job.Id, default);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_notification BEFORE INSERT ON Notifications BEGIN SELECT RAISE(ABORT, 'temporary test failure'); END");
        await Assert.ThrowsAsync<NotificationBroadcastProcessingException>(() => service.ProcessNextBatchAsync(default));
        f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.Empty(await f.Db.Set<NotificationDelivery>().ToListAsync());
        Assert.Equal("Queued", (await service.GetAsync(f.Admin.Id, job.Id, default)).Status);
        await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_notification");
        await service.ProcessNextBatchAsync(default);
        Assert.Equal(250, await f.Db.Set<VerificationNotification>().CountAsync());
        Assert.Equal("Processing", (await service.GetAsync(f.Admin.Id, job.Id, default)).Status);
        var failed = await f.Db.Set<NotificationBroadcast>().SingleAsync();
        failed.Status = "Failed"; failed.Attempts = 10; failed.LastError = "Temporary delivery failure";
        await f.Db.SaveChangesAsync();
        var retry = await service.PublishAsync(f.Admin.Id, job.Id, default);
        Assert.Equal(job.Id, retry.Id); Assert.Equal("Queued", retry.Status); Assert.Equal(250, retry.ProcessedCount);
        Assert.Null(retry.LastError); Assert.Equal(0, failed.Attempts);
        await service.ProcessNextBatchAsync(default);
        Assert.Equal(252, await f.Db.Set<VerificationNotification>().CountAsync());
        Assert.Equal(252, await f.Db.Set<NotificationDelivery>().CountAsync());
        Assert.Equal(252, (await service.GetAsync(f.Admin.Id, job.Id, default)).ProcessedCount);
        Assert.Equal("Completed", (await service.GetAsync(f.Admin.Id, job.Id, default)).Status);
    }

    [Theory]
    [InlineData("https://evil.test")][InlineData("//evil.test")][InlineData("/\\evil.test")][InlineData("/%2f%2fevil.test")][InlineData("/unknown")]
    public void UnsafeTargetsAreRejected(string target) => Assert.False(NotificationService.IsSafeUrl(target));

    [Fact]
    public void SignalRIdentifierUsesJwtAccountSubject()
    {
        var accountId = Guid.NewGuid();
        Assert.Equal(accountId.ToString(), AccountUserIdProvider.Identifier(accountId.ToString("N")));
        Assert.Null(AccountUserIdProvider.Identifier("profile:123")); Assert.Null(AccountUserIdProvider.Identifier(Guid.Empty.ToString()));
    }

    private static NotificationContent Content(string key) => new("Test", "verification", "Thông báo", "Nội dung", EventKey: key);
    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required UniNetDbContext Db { get; init; }
        public required Account Student { get; init; }
        public required Account Partner { get; init; }
        public required Account Admin { get; init; }
        public required Account Moderator { get; init; }
        public NotificationService Service => new(Db);
        public static async Task<Fixture> Create() {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await db.Database.EnsureCreatedAsync();
            var accounts = new[] { AccountRole.Student, AccountRole.Partner, AccountRole.Admin, AccountRole.Moderator }.Select((role, i) =>
                new Account { Email = $"notifications-{i}@example.test", Role = role, Profile = new() { FullName = "Test", Nickname = $"notice{i}" } }).ToArray();
            db.Accounts.AddRange(accounts); await db.SaveChangesAsync();
            return new() { Connection = connection, Db = db, Student = accounts[0], Partner = accounts[1], Admin = accounts[2], Moderator = accounts[3] };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
