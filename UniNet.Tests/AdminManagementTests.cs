using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class AdminManagementTests
{
    [Fact]
    public async Task UserListsAndStatsExcludeStaffWhileStaffListRemainsSeparate()
    {
        await using var f = await Fixture.Create();
        var partner = new Account { Email = "partner@management.test", Role = AccountRole.Partner, PasswordHash = "private", Profile = new() { FullName = "Partner", Nickname = "partner", IsVerified = true } };
        var verifier = new Account { Email = "verifier@management.test", Role = AccountRole.Assessor, PasswordHash = "private" };
        f.Db.Accounts.AddRange(partner, verifier);
        await f.Db.SaveChangesAsync();

        var users = await f.Users.ListAsync(f.Admin.Id, new(), default);
        Assert.Equal(2, users.Total);
        Assert.All(users.Items, row => Assert.True(row.Role is AccountRole.Student or AccountRole.Partner));
        Assert.Empty((await f.Users.ListAsync(f.Admin.Id, new(Role: AccountRole.Admin), default)).Items);
        Assert.Empty((await f.Users.ListAsync(f.Admin.Id, new(Search: "staff@"), default)).Items);
        var stats = await f.Users.StatsAsync(f.Admin.Id, default);
        Assert.Equal(2, stats.Total);
        Assert.Equal(2, stats.Active);
        Assert.Equal(1, stats.Verified);
        var staff = await f.Users.ListAsync(f.Admin.Id, new(StaffOnly: true), default);
        Assert.Equal(3, staff.Total);
        Assert.All(staff.Items, row => Assert.True(row.Role is AccountRole.Admin or AccountRole.Moderator or AccountRole.Assessor));
    }

    [Fact]
    public async Task UsersAreFilteredPagedAndNeverExposeCredentials()
    {
        await using var f = await Fixture.Create();
        var result = await f.Users.ListAsync(f.Admin.Id, new(Search: "student", Role: AccountRole.Student, PageSize: 1), default);
        Assert.Equal(1, result.Total); Assert.Equal(f.Student.Id, Assert.Single(result.Items).Id);
        var json = JsonSerializer.Serialize(await f.Users.DetailAsync(f.Admin.Id, f.Student.Id, default));
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh", json, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => f.Users.ListAsync(f.Student.Id, new(), default));
        f.Db.Set<NotificationPermissionOverride>().Add(new() { AccountId = f.Admin.Id, Permission = NotificationPermissionPolicy.UsersView, IsGranted = false });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => f.Users.ListAsync(f.Admin.Id, new(), default));
    }

    [Fact]
    public async Task SuspensionRevokesSessionsAndUnlockDoesNotReviveOldTokens()
    {
        await using var f = await Fixture.Create();
        f.Db.RefreshTokens.Add(new() { AccountId = f.Student.Id, TokenHash = "private-fixture", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await f.Db.SaveChangesAsync();
        Assert.True(await AccountSessions.IsValidAsync(f.Db, f.Student.Id, 0, default));
        await f.Users.ChangeStatusAsync(f.Admin.Id, f.Student.Id, true, "Vi phạm nội quy", default);
        Assert.False(await AccountSessions.IsValidAsync(f.Db, f.Student.Id, 0, default));
        Assert.NotNull((await f.Db.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Single(await f.Db.Set<AccountStatusChange>().ToListAsync());
        Assert.Equal("account.locked", (await f.Db.Set<VerificationNotification>().SingleAsync()).Type);
        await f.Users.ChangeStatusAsync(f.Admin.Id, f.Student.Id, false, "Đã kiểm tra", default);
        Assert.False(await AccountSessions.IsValidAsync(f.Db, f.Student.Id, 0, default));
        f.Db.ChangeTracker.Clear();
        var account = await f.Db.Accounts.SingleAsync(a => a.Id == f.Student.Id);
        Assert.True(await AccountSessions.IsValidAsync(f.Db, account.Id, account.SessionVersion, default));
        Assert.Equal(2, await f.Db.Set<AccountStatusChange>().CountAsync());
        Assert.Equal(2, await f.Db.Set<VerificationNotification>().CountAsync());
    }

    [Fact]
    public async Task StaffAndSelfCannotBeLockedAndPermissionRevocationIsEnforced()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<UniNet.Application.AuthException>(() => f.Users.ChangeStatusAsync(f.Admin.Id, f.Admin.Id, true, "reason", default));
        await Assert.ThrowsAsync<UniNet.Application.AuthException>(() => f.Users.ChangeStatusAsync(f.Admin.Id, f.Staff.Id, true, "reason", default));
        await Assert.ThrowsAsync<UniNet.Application.AuthException>(() => f.Users.ChangeStatusAsync(f.Admin.Id, f.Student.Id, true, " ", default));
        f.Db.Set<NotificationPermissionOverride>().Add(new() { AccountId = f.Admin.Id, Permission = NotificationPermissionPolicy.UsersLock, IsGranted = false });
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => f.Users.ChangeStatusAsync(f.Admin.Id, f.Student.Id, true, "reason", default));
        Assert.Empty(await f.Db.Set<AccountStatusChange>().ToListAsync());
    }

    [Fact]
    public async Task PermissionOverridesCannotElevateTheActorAndGrantsApplyToStaff()
    {
        await using var f = await Fixture.Create();
        var service = new AdminPermissionService(f.Db);
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => f.Users.ListAsync(f.Staff.Id, new(StaffOnly: true), default));
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => f.Users.DetailAsync(f.Staff.Id, f.Admin.Id, default));
        await Assert.ThrowsAsync<UniNet.Application.AuthException>(() => service.SetAsync(f.Admin.Id, f.Admin.Id, NotificationPermissionPolicy.UsersView, true, default));
        await service.SetAsync(f.Admin.Id, f.Staff.Id, NotificationPermissionPolicy.UsersView, true, default);
        Assert.NotEmpty((await f.Users.ListAsync(f.Staff.Id, new(), default)).Items);
        await service.SetAsync(f.Admin.Id, f.Staff.Id, NotificationPermissionPolicy.UsersView, false, default);
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => f.Users.ListAsync(f.Staff.Id, new(), default));
        await Assert.ThrowsAsync<UniNet.Application.AuthException>(() => service.SetAsync(f.Admin.Id, f.Student.Id, NotificationPermissionPolicy.UsersView, true, default));
    }

    [Fact]
    public async Task VerificationQueriesSearchSubmittedFieldsAndKeepPrivateBytesOutOfJson()
    {
        await using var f = await Fixture.Create();
        var row = new UserVerification { UserId = f.Student.Profile!.Id, VerificationType = VerificationType.Student,
            VerificationData = JsonSerializer.Serialize(new { fullName = "Tên gửi xác minh", schoolName = "FPT University", studentCode = "MSSV-123",
                documentName = "private.pdf", documentContentType = "application/pdf", documentBase64 = Convert.ToBase64String("private-data"u8.ToArray()) }) };
        f.Db.UserVerifications.Add(row); await f.Db.SaveChangesAsync();
        var queries = new AdminVerificationQueries(f.Db);
        var result = await queries.ListAsync(f.Admin.Id, VerificationType.Student, new(Search: "mssv-123"), default);
        Assert.Equal(row.Id, Assert.Single(result.Items).Id);
        var detail = await queries.DetailAsync(f.Admin.Id, VerificationType.Student, row.Id, default);
        Assert.Equal("MSSV-123", detail.SubmittedData["studentCode"]);
        Assert.Single(detail.History); Assert.Single(detail.Documents);
        Assert.DoesNotContain("documentBase64", JsonSerializer.Serialize(detail), StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => queries.DetailAsync(f.Staff.Id, VerificationType.Student, row.Id, default));
        await Assert.ThrowsAsync<UniNet.Application.AuthException>(() => new AdminPermissionService(f.Db).SetAsync(f.Admin.Id, f.Staff.Id, NotificationPermissionPolicy.StudentVerificationReview, true, default));
        var verifier = new Account { Email = "verification-staff@management.test", Role = AccountRole.Verifier, PasswordHash = "fixture" };
        f.Db.Add(verifier); await f.Db.SaveChangesAsync();
        Assert.Equal(1, (await queries.StatsAsync(verifier.Id, VerificationType.Student, default)).Pending);
        await new AdminPermissionService(f.Db).SetAsync(f.Admin.Id, verifier.Id, NotificationPermissionPolicy.StudentVerificationReview, false, default);
        await Assert.ThrowsAsync<UniNet.Application.ProjectVerificationException>(() => queries.StatsAsync(verifier.Id, VerificationType.Student, default));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required UniNetDbContext Db { get; init; }
        public required Account Admin { get; init; }
        public required Account Student { get; init; }
        public required Account Staff { get; init; }
        public AdminUsersService Users => new(Db);
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await db.Database.EnsureCreatedAsync();
            Account Make(string name, AccountRole role) => new() { Email = name + "@management.test", Role = role, PasswordHash = "never-expose", Profile = new() { FullName = name, Nickname = name } };
            var admin = Make("admin", AccountRole.Admin); var student = Make("student", AccountRole.Student); var staff = Make("staff", AccountRole.Moderator);
            db.Accounts.AddRange(admin, student, staff); await db.SaveChangesAsync();
            return new() { Connection = connection, Db = db, Admin = admin, Student = student, Staff = staff };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
