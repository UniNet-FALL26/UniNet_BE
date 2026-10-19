using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class UnifiedModerationMigrationPostgresTests
{
    private const string Snapshot = "{\"unchanged\":\"snapshot\"}";
    private const string Policy = "{\"unchanged\":\"policy\"}";
    private const string Results = "[{\"preserved\":true}]";
    private const string Before = "20261018041023_SingleVerificationReportAndRefundAudit";

    private sealed class Store : IAsyncDisposable
    {
        private readonly string schema = "g1_migration_" + Guid.NewGuid().ToString("N");
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
            new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
        public async Task InitializeAsync()
        {
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
            await using var db = Open();
            await db.GetService<IMigrator>().MigrateAsync(Before);
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }

    private static async Task<(Guid Project, Guid User, Guid Package)> SeedAsync(UniNetDbContext db)
    {
        var account = new Account { Email = "g1@test.local", Role = AccountRole.Student,
            Profile = new() { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = account.Profile!, Title = "Project", Description = "Work",
            ProjectField = "IT", MemberTarget = 1, Status = ProjectStatus.Completed };
        var package = new Package { Id = Guid.NewGuid(), Code = "g1", Name = "Verification" };
        db.Accounts.Add(account); db.Projects.Add(project); db.Packages.Add(package);
        await db.SaveChangesAsync();
        return (project.Id, account.Profile!.Id, package.Id);
    }

    private static Task AddEvidenceAsync(UniNetDbContext db, Guid id, Guid project, short status, int version) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO "ProjectEvidenceModerations" ("Id","ProjectId","Status","Version","SourceHash","PolicyHash","SnapshotJson","PolicyJson","CreatedAt","SupersededAt","AttemptCount","LeaseToken","LeaseExpiresAt","LastErrorCode","Summary")
VALUES ({id},{project},{status},{version},{new string('A',64)},{new string('B',64)},{Snapshot}::jsonb,{Policy}::jsonb,'2026-10-17T01:00:00Z','2026-10-18T01:00:00Z',2,{Guid.NewGuid()},'2026-10-18T02:00:00Z','original-error','original-summary');
""");

    private static async Task<string> RowsAsync(UniNetDbContext db, string table)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY \"Id\"),'[]'::jsonb)::text FROM \"{table}\" t";
        return (string)(await command.ExecuteScalarAsync())!;
    }

    [EvidencePostgresFact]
    public async Task AllStatusesSnapshotsAttemptsPinnedCasesAndNotificationsSurviveUpAndDown()
    {
        await using var store = new Store(); await store.InitializeAsync();
        await using var db = store.Open(); var seed = await SeedAsync(db);
        var ids = new List<Guid>();
        for (short status = 0; status <= 5; status++)
        {
            var id = Guid.NewGuid(); ids.Add(id);
            await AddEvidenceAsync(db, id, seed.Project, status, status + 1);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO "EvidenceModerationAttempts" ("Id","ModerationId","Number","StartedAt","FinishedAt","ResultsJson","ErrorCode","Error","Transient")
VALUES ({Guid.NewGuid()},{id},1,'2026-10-17T01:00:00Z','2026-10-17T02:00:00Z',{Results}::jsonb,'ERROR','history',true);
INSERT INTO "VerificationNotification" ("Id","EvidenceModerationId","RecipientUserId","EventKey","Message","CreatedAt")
VALUES ({Guid.NewGuid()},{id},{seed.User},{"evidence:" + id},'historical','2026-10-17T02:00:00Z');
""");
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectEvidenceModerations\" SET \"SupersededAt\" = NULL WHERE \"Id\" = {ids[5]};");
        // Submission and evidence integers 2/3/4 have different meanings before migration.
        for (short status = 0; status <= 5; status++)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO "ProjectModerations" ("Id","ProjectId","Status","ContentResult","LinkResult","ContentHash","AttemptNumber","RetryCount","CreatedAt","UpdatedAt")
VALUES ({Guid.NewGuid()},{seed.Project},{status},0,0,'submission',{status+1},0,'2026-10-17T01:00:00Z','2026-10-17T01:00:00Z');
""");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO "ProjectVerifications" ("Id","ProjectId","RequestByUserId","PackageId","Status","SubmittedAt","CreatedAt","UpdatedAt","AiAdditionalEvidenceJson","AiAttemptCount","BookingStatus","EvidenceModerationId")
VALUES ({Guid.NewGuid()},{seed.Project},{seed.User},{seed.Package},0,'2026-10-17T01:00:00Z','2026-10-17T01:00:00Z','2026-10-17T01:00:00Z','[]',0,0,{ids[2]});
""");
        var oldEvidence = await RowsAsync(db, "ProjectEvidenceModerations");
        var oldSubmission = await RowsAsync(db, "ProjectModerations");
        var oldAttempts = await RowsAsync(db, "EvidenceModerationAttempts");
        var oldNotifications = await RowsAsync(db, "VerificationNotification");
        var oldCases = await RowsAsync(db, "ProjectVerifications");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var migrated = await db.ProjectModerations.Where(m => m.Purpose == ProjectModerationPurpose.ProjectEvidence).OrderBy(m => m.Version).ToListAsync();
        Assert.Equal(new[] { ProjectModerationStatus.Pending, ProjectModerationStatus.Processing, ProjectModerationStatus.Passed,
            ProjectModerationStatus.Rejected, ProjectModerationStatus.NeedsEvidence, ProjectModerationStatus.Error }, migrated.Select(m => m.Status));
        Assert.Equal(ids, migrated.Select(m => m.Id));
        Assert.All(migrated, m => { Assert.Equal(2, m.AttemptCount); Assert.Equal(0, m.AttemptNumber); });
        Assert.All(migrated.Take(5), m => Assert.NotNull(m.SupersededAt));
        Assert.Null(migrated.Last().SupersededAt);
        Assert.Equal(6, await db.EvidenceModerationAttempts.CountAsync());
        Assert.Equal(6, await db.Set<VerificationNotification>().CountAsync());
        Assert.Equal(ids[2], (await db.ProjectVerifications.Include(v => v.EvidenceModeration).SingleAsync()).EvidenceModeration!.Id);
        Assert.Equal(6, await db.ProjectModerations.CountAsync(m => m.Purpose == ProjectModerationPurpose.ProjectSubmission));
        await db.GetService<IMigrator>().MigrateAsync(Before); db.ChangeTracker.Clear();
        Assert.Equal(oldEvidence, await RowsAsync(db, "ProjectEvidenceModerations"));
        Assert.Equal(oldSubmission, await RowsAsync(db, "ProjectModerations"));
        Assert.Equal(oldAttempts, await RowsAsync(db, "EvidenceModerationAttempts"));
        Assert.Equal(oldNotifications, await RowsAsync(db, "VerificationNotification"));
        Assert.Equal(oldCases, await RowsAsync(db, "ProjectVerifications"));
    }

    [EvidencePostgresFact]
    public async Task IdCollisionStopsMigrationWithoutChangingEitherTable()
    {
        await using var store = new Store(); await store.InitializeAsync();
        await using var db = store.Open(); var seed = await SeedAsync(db); var id = Guid.NewGuid();
        await AddEvidenceAsync(db, id, seed.Project, 2, 1);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
INSERT INTO "ProjectModerations" ("Id","ProjectId","Status","ContentResult","LinkResult","ContentHash","AttemptNumber","RetryCount","CreatedAt","UpdatedAt")
VALUES ({id},{seed.Project},3,0,0,'original',1,0,now(),now());
""");
        var before = await RowsAsync(db, "ProjectEvidenceModerations");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("ID collision", error.MessageText);
        Assert.Equal(before, await RowsAsync(db, "ProjectEvidenceModerations"));
        Assert.DoesNotContain("20261019072056_UnifyProjectModerationPurposes", await db.Database.GetAppliedMigrationsAsync());
    }

    [EvidencePostgresFact]
    public async Task UnknownInboundForeignKeyStopsMigrationBeforeDroppingData()
    {
        await using var store = new Store(); await store.InitializeAsync();
        await using var db = store.Open();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE extra_evidence_reference (id uuid REFERENCES \"ProjectEvidenceModerations\"(\"Id\"));");
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
        Assert.Contains("Unexpected FK", error.MessageText);
        Assert.Equal("[]", await RowsAsync(db, "ProjectEvidenceModerations"));
    }
}
