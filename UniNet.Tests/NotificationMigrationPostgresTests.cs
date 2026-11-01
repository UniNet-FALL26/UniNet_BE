using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class NotificationPostgresFactAttribute : FactAttribute
{
    public NotificationPostgresFactAttribute() {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES"))) Skip = "Set UNINET_TEST_POSTGRES to an isolated PostgreSQL test database.";
    }
}

public sealed class NotificationMigrationPostgresTests
{
    private const string Before = "20261024095804_AddWorkspaceChat";
    [NotificationPostgresFact]
    public async Task AccountManagementMigrationBackfillsSessionsAndRefusesUnsafeAuditRollback()
    {
        await using var f = await Store.Create(); await using var db = f.Open();
        var seed = await SeedAsync(db);
        await db.Database.MigrateAsync();
        Assert.Equal(0, await db.Accounts.Where(a => a.Id == seed.Account).Select(a => a.SessionVersion).SingleAsync());
        var actor = new Account { Email = "migration-admin@test.test", Role = AccountRole.Admin };
        db.Accounts.Add(actor); await db.SaveChangesAsync();
        db.Set<AccountStatusChange>().Add(new() { AccountId = seed.Account, ActorAccountId = actor.Id, PreviousStatus = AccountStatus.Active, Status = AccountStatus.Suspended, Reason = "Migration rollback safety" });
        await db.Accounts.Where(a => a.Id == seed.Account).ExecuteUpdateAsync(s => s.SetProperty(a => a.SessionVersion, 1));
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261025072442_UnifiedNotifications"));
        Assert.Contains("revoked sessions", error.MessageText);
        Assert.Single(await db.Set<AccountStatusChange>().ToListAsync());
    }
    [NotificationPostgresFact]
    public async Task AllLegacyTargetsAndDistinctProfileIdsSurviveUpAndDown()
    {
        await using var f = await Store.Create(); await using var db = f.Open();
        var seed = await SeedAsync(db);
        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var notices = await db.Set<VerificationNotification>().AsNoTracking().ToListAsync();
        Assert.Equal(3, notices.Count);
        Assert.All(notices, n => { Assert.Equal(seed.Account, n.RecipientUserId); Assert.Equal("unchanged-content", n.Message); Assert.Equal(seed.Created, n.CreatedAt); Assert.Equal("verification", n.Category); Assert.Null(n.SenderUserId); Assert.Null(n.BroadcastId); Assert.Null(n.ReadAt); });
        Assert.Equal(seed.Targets.OrderBy(x => x), notices.Select(n => n.TargetId!.Value).OrderBy(x => x));
        Assert.Equal(new[] { "EvidenceModeration", "ProjectVerification", "UserVerification" }, notices.Select(n => n.TargetType!).OrderBy(x => x));
        Assert.Equal(seed.Ids.OrderBy(x => x), notices.Select(n => n.Id).OrderBy(x => x));
        await db.GetService<IMigrator>().MigrateAsync(Before);
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"VerificationNotification\"").SingleAsync());
        Assert.Equal(3, await db.Database.SqlQuery<Guid>($"SELECT \"RecipientUserId\" AS \"Value\" FROM \"VerificationNotification\" WHERE \"RecipientUserId\" = {seed.Profile}").CountAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(3, await db.Set<VerificationNotification>().CountAsync());
        // Actual PostgreSQL inbox queries and bulk update, not only SQLite equivalents.
        var service = new NotificationService(db);
        Assert.Equal(3, await service.UnreadCountAsync(seed.Account));
        await service.ReadAsync(seed.Account, null);
        Assert.Equal(0, await service.UnreadCountAsync(seed.Account));
    }

    [NotificationPostgresFact]
    public async Task InvalidLegacyTargetsAbortWithoutChangingOriginalTable()
    {
        await using var f = await Store.Create(); await using var db = f.Open(); var seed = await SeedAsync(db);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"VerificationNotification\" DROP CONSTRAINT \"CK_VerificationNotification_Target\"");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"VerificationNotification\" SET \"VerificationId\" = NULL, \"EvidenceModerationId\" = NULL, \"UserVerificationId\" = NULL WHERE \"Id\" = {seed.Ids[0]}");
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Equal(3, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"VerificationNotification\"").SingleAsync());
    }

    [NotificationPostgresFact]
    public async Task BroadcastOnPostgresIsBatchedAndRollbackRefusesNewHistory()
    {
        await using var f = await Store.Create(); await using var db = f.Open(); var seed = await SeedAsync(db);
        await db.Database.MigrateAsync();
        var admin = new Account { Email = "notice-admin@test.local", Role = AccountRole.Admin };
        db.Accounts.Add(admin); await db.SaveChangesAsync();
        var service = new NotificationBroadcastService(db);
        var job = await service.CreateAsync(admin.Id, new("Maintenance", "Window", "Role", "Student"), default);
        await service.PublishAsync(admin.Id, job.Id, default); await service.ProcessNextBatchAsync(default);
        Assert.Single(await db.Set<VerificationNotification>().Where(n => n.BroadcastId == job.Id).ToListAsync());
        await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(Before));
        Assert.Equal(4, await db.Set<VerificationNotification>().CountAsync());
    }

    [NotificationPostgresFact]
    public async Task ConcurrentSameEventHasOneNotificationAndOneOutboxWithoutAbortingEitherWriter()
    {
        await using var f = await Store.Create(); Guid recipient;
        await using (var db = f.Open()) { var seed = await SeedAsync(db); recipient = seed.Account; await db.Database.MigrateAsync(); }
        await using var first = f.Open(); await using var second = f.Open();
        var content = new NotificationContent("Concurrent", "system", "Title", "Content", EventKey: "concurrent-idempotent-event");
        await NotificationService.EnqueueAsync(first, [recipient], content);
        await NotificationService.EnqueueAsync(second, [recipient], content);
        await Task.WhenAll(first.SaveChangesAsync(), second.SaveChangesAsync());
        await using var check = f.Open();
        var notice = Assert.Single(await check.Set<VerificationNotification>().Where(n => n.EventKey == content.EventKey).ToListAsync());
        Assert.Single(await check.Set<NotificationDelivery>().Where(d => d.NotificationId == notice.Id).ToListAsync());
    }

    private static async Task<(Guid Account, Guid Profile, Guid[] Ids, Guid[] Targets, DateTimeOffset Created)> SeedAsync(UniNetDbContext db)
    {
        var now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var account = new Account { Email = "notice-migration@test.local", Role = AccountRole.Student, Profile = new() { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = account.Profile!, Title = "Project", Description = "Work", ProjectField = "IT", MemberTarget = 1, Status = ProjectStatus.Completed };
        var package = new Package { Id = Guid.NewGuid(), Code = "notice", Name = "Verification" };
        var verification = new UserVerification { User = account.Profile!, VerificationType = VerificationType.Student };
        var evidence = new ProjectModeration { Project = project, Purpose = ProjectModerationPurpose.ProjectEvidence, Version = 1, SourceHash = new('A',64), PolicyHash = new('B',64), SnapshotJson = "{}", PolicyJson = "{}", CreatedAt = now, UpdatedAt = now };
        var projectVerification = new ProjectVerification { Id = Guid.NewGuid(), Project = project, RequestByUser = account.Profile!, Package = package, SubmittedAt = now, CreatedAt = now, UpdatedAt = now };
        db.Accounts.Add(account); db.Projects.Add(project); db.Packages.Add(package); db.UserVerifications.Add(verification); db.ProjectModerations.Add(evidence); db.ProjectVerifications.Add(projectVerification);
        await db.SaveChangesAsync();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "VerificationNotification" ("Id","RecipientUserId","EventKey","Message","CreatedAt","UserVerificationId") VALUES ({ids[0]},{account.Profile!.Id},'legacy-user','unchanged-content',{now},{verification.Id});
            INSERT INTO "VerificationNotification" ("Id","RecipientUserId","EventKey","Message","CreatedAt","VerificationId") VALUES ({ids[1]},{account.Profile!.Id},'legacy-project','unchanged-content',{now},{projectVerification.Id});
            INSERT INTO "VerificationNotification" ("Id","RecipientUserId","EventKey","Message","CreatedAt","EvidenceModerationId") VALUES ({ids[2]},{account.Profile!.Id},'legacy-evidence','unchanged-content',{now},{evidence.Id});
            """);
        return (account.Id, account.Profile!.Id, ids, [verification.Id, projectVerification.Id, evidence.Id], now);
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string schema = "notification_test_" + Guid.NewGuid().ToString("N");
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
            new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
        public static async Task<Store> Create() {
            var store = new Store(); await using var db = store.Open();
            // Schema identifier is generated exclusively from a GUID.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA {store.schema}");
#pragma warning restore EF1002
            await db.GetService<IMigrator>().MigrateAsync(Before); return store;
        }
        public async ValueTask DisposeAsync() {
            await using var db = Open();
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE");
#pragma warning restore EF1002
        }
    }
}
