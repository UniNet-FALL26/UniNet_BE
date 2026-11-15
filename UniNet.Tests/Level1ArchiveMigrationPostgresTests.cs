using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
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

// SQL identifiers below are fixed or generated exclusively by this fixture.
#pragma warning disable EF1002
public sealed class Level1ArchiveMigrationPostgresTests
{
    private const string Before = "20261108023501_AddAssessorPasswordTokenIndex";
    private const string After = "20261108142140_ArchiveAndRemoveLevel1";
    private sealed class Store : IAsyncDisposable
    {
        public readonly string Schema = "l1_test_" + Guid.NewGuid().ToString("N");
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        public string Archive => "l1_archive_" + Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(Schema))).ToLowerInvariant();
        public string ReportArchive => "verification_archive_" + Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(Schema))).ToLowerInvariant();
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
            new NpgsqlConnectionStringBuilder(connection) { SearchPath = Schema }.ConnectionString,
            o => o.MigrationsHistoryTable("__EFMigrationsHistory", Schema)).Options);
        public async Task Initialize()
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE SCHEMA {Schema}", admin).ExecuteNonQueryAsync();
            await using var db = Open(); await db.GetService<IMigrator>().MigrateAsync(Before);
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {Archive} CASCADE; DROP SCHEMA IF EXISTS {ReportArchive} CASCADE; DROP SCHEMA IF EXISTS {Schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }

    private sealed record Seed(Guid Project, Guid Submission, Guid Case);
    private static async Task<Seed> SeedData(UniNetDbContext db)
    {
        var now = new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero);
        Account User(string name, AccountRole role) => new() { Email = name + "@l1.local", Role = role,
            Profile = new() { FullName = name, Nickname = name } };
        var owner = User("Owner", AccountRole.Student); var admin = User("Admin", AccountRole.Admin); var assessor = User("Assessor", AccountRole.Assessor);
        var project = new Project { Creator = owner.Profile!, Title = "Preserved project", Description = "Completed",
            ProjectField = "IT", MemberTarget = 2, Status = ProjectStatus.Completed };
        var package = new Package { Id = Guid.NewGuid(), Code = "VERIFY-L1", Name = "Verification", Price = 15000,
            Type = PackageType.Verification, Role = TargetRole.Student, IsActive = true };
        var submission = new ProjectModeration { Project = project, Purpose = ProjectModerationPurpose.ProjectSubmission,
            AttemptNumber = 1, ContentHash = new('C', 64), Status = ProjectModerationStatus.Approved,
            CreatedAt = now, UpdatedAt = now, ReviewNote = "original submission decision", ReviewedBy = admin.Profile };
        db.Accounts.AddRange(owner, admin, assessor); db.Packages.Add(package); db.ProjectModerations.Add(submission);
        await db.SaveChangesAsync();
        var verification = new ProjectVerification { Id = Guid.NewGuid(), Project = project, RequestByUser = owner.Profile!,
            Package = package, Status = ProjectVerificationStatus.Verified, SubmittedAt = now, CreatedAt = now, UpdatedAt = now,
            FinalizedAt = now.AddHours(3), FinalizedByUserId = assessor.Profile!.Id };
        var assignment = new VerificationAssignment { Id = Guid.NewGuid(), ProjectVerification = verification,
            AssignedToUserId = assessor.Profile.Id, AdminId = admin.Profile!.Id, Status = VerificationAssignmentStatus.Completed,
            AssignedAt = now, CreatedAt = now, UpdatedAt = now, CompletedAt = now.AddHours(3) };
        assignment.Report = new VerificationReport { Id = Guid.NewGuid(), ProjectVerificationId = verification.Id,
            AssignmentId = assignment.Id, Summary = "Final report", Findings = "Preserve findings", Recommendations = "Preserve advice",
            Outcome = ProjectVerificationStatus.Verified, SubmittedByUserId = assessor.Profile.Id,
            SubmittetAt = now.AddHours(3), CreatedAt = now.AddHours(3), UpdatedAt = now.AddHours(3) };
        db.VerificationAssignments.Add(assignment);
        db.Transactions.Add(new() { Id = Guid.NewGuid(), UserId = owner.Profile!.Id, Package = package, Amount = 15000,
            Verification = verification, Status = PaymentStatus.Success, PaidAt = now, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        Guid linked = Guid.Empty;
        const string snapshot = "{\"original\":\"source\"}";
        const string policy = "{\"original\":\"policy\"}";
        const string results = "[{\"result\":\"original\"}]";
        short[] statuses = [0, 1, 6, 4, 7, 5];
        for (var i = 0; i < statuses.Length; i++)
        {
            var id = Guid.NewGuid(); if (i == 2) linked = id;
            DateTimeOffset? superseded = i == statuses.Length - 1 ? null : now.AddMinutes(i + 1);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ProjectModerations" ("Id","ProjectId","Purpose","Status","ContentResult","LinkResult","ContentHash","AttemptNumber","RetryCount","CreatedAt","UpdatedAt","Version","SourceHash","PolicyHash","SnapshotJson","PolicyJson","SupersededAt","AttemptCount","LastErrorCode","Summary")
                VALUES ({id},{project.Id},1,{statuses[i]},0,0,'',0,0,{now},{now},{i + 1},{new string('A',64)},{new string('B',64)},{snapshot}::jsonb,{policy}::jsonb,{superseded},2,'ORIGINAL_ERROR','Preserved summary');
                INSERT INTO "EvidenceModerationAttempts" ("Id","ModerationId","ModerationPurpose","Number","StartedAt","FinishedAt","ResultsJson","ErrorCode","Error","Transient")
                VALUES ({Guid.NewGuid()},{id},1,1,{now},{now},{results}::jsonb,'ERROR','Original error',true);
                """);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectVerifications\" SET \"EvidenceModerationId\"={linked} WHERE \"Id\"={verification.Id}");
        // Preserve retired non-default columns even on a submission record.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectModerations\" SET \"Summary\"='retired submission metadata',\"AttemptCount\"=9 WHERE \"Id\"={submission.Id}");
        return new(project.Id, submission.Id, verification.Id);
    }

    private static async Task<string> Rows(UniNetDbContext db, string table, string? schema = null, string projection = "to_jsonb(t)")
    {
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SET TIME ZONE 'UTC'; SELECT coalesce(jsonb_agg({projection} ORDER BY \"Id\"),'[]'::jsonb)::text FROM {(schema == null ? "" : '"' + schema + "\".")}\"{table}\" t";
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
    private static Task Migrate(UniNetDbContext db, string target) => db.GetService<IMigrator>().MigrateAsync(target);

    [IsolatedPostgresFact]
    public async Task ArchivesAllStatusesAndLinksWithoutChangingSubmissionOrPaidAssessmentAndRoundTrips()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        var seed = await SeedData(db);
        var originalReviews = await Rows(db, "ProjectModerations"); var attempts = await Rows(db, "EvidenceModerationAttempts");
        var originalCases = await Rows(db, "ProjectVerifications");
        string[] protectedTables = ["Projects", "Accounts", "Users", "Packages", "Transactions", "VerificationAssignments", "VerificationReports", "RefundTransactions"];
        var before = new Dictionary<string, string>(); foreach (var table in protectedTables) before[table] = await Rows(db, table);
        var casesWithoutL1 = await Rows(db, "ProjectVerifications", projection: "to_jsonb(t)-'EvidenceModerationId'-'EvidenceModerationPurpose'");
        await Migrate(db, After); db.ChangeTracker.Clear();
        Assert.Equal(originalReviews, await Rows(db, "ProjectModerations", store.Archive));
        Assert.Equal(attempts, await Rows(db, "EvidenceModerationAttempts", store.Archive));
        Assert.Single(await db.ProjectModerations.ToListAsync());
        Assert.Equal(seed.Submission, (await db.ProjectModerations.SingleAsync()).Id);
        Assert.Equal(casesWithoutL1, await Rows(db, "ProjectVerifications"));
        foreach (var table in protectedTables) Assert.Equal(before[table], await Rows(db, table));
        Assert.DoesNotContain("SnapshotJson", await Rows(db, "ProjectModerations"));
        var pending = new ProjectVerification { Id = Guid.NewGuid(), ProjectId = seed.Project,
            RequestByUserId = (await db.ProjectVerifications.SingleAsync()).RequestByUserId,
            PackageId = (await db.ProjectVerifications.SingleAsync()).PackageId, Status = ProjectVerificationStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.ProjectVerifications.Add(pending); await db.SaveChangesAsync(); db.ProjectVerifications.Remove(pending); await db.SaveChangesAsync();
        await Migrate(db, Before);
        Assert.Equal(originalReviews, await Rows(db, "ProjectModerations"));
        Assert.Equal(attempts, await Rows(db, "EvidenceModerationAttempts"));
        Assert.Equal(originalCases, await Rows(db, "ProjectVerifications"));
        foreach (var table in protectedTables) Assert.Equal(before[table], await Rows(db, table));
    }

    [IsolatedPostgresFact]
    public async Task ArchiveRejectsEveryWriteAndRollbackPreservesNewSubmissionDecision()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        var seed = await SeedData(db); await Migrate(db, After); db.ChangeTracker.Clear();
        foreach (var table in new[] { "ProjectModerations", "EvidenceModerationAttempts", "ProjectVerificationLevel1Links", "Manifest" })
        foreach (var operation in new[] { "UPDATE", "DELETE", "INSERT", "TRUNCATE" })
        {
            var sql = operation switch {
                "UPDATE" => $"UPDATE \"{store.Archive}\".\"{table}\" SET \"{(table == "Manifest" ? "TableName" : "Id")}\"=\"{(table == "Manifest" ? "TableName" : "Id")}\" WHERE false",
                "DELETE" => $"DELETE FROM \"{store.Archive}\".\"{table}\" WHERE false",
                "INSERT" => $"INSERT INTO \"{store.Archive}\".\"{table}\" SELECT * FROM \"{store.Archive}\".\"{table}\" WHERE false",
                _ => $"TRUNCATE \"{store.Archive}\".\"{table}\"" };
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Contains("read-only", error.MessageText);
        }
        var submission = await db.ProjectModerations.SingleAsync(); submission.ReviewNote = "Updated after removal";
        submission.Status = ProjectModerationStatus.Rejected; await db.SaveChangesAsync();
        await Migrate(db, Before); db.ChangeTracker.Clear();
        Assert.Equal("Updated after removal", (await db.ProjectModerations.SingleAsync(m => m.Id == seed.Submission)).ReviewNote);
        Assert.Equal(ProjectModerationStatus.Rejected, (await db.ProjectModerations.SingleAsync(m => m.Id == seed.Submission)).Status);
    }

    [IsolatedPostgresFact]
    public async Task TamperedArchiveRefusesRollbackAtomically()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await SeedData(db); await Migrate(db, After);
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{store.Archive}\".\"ProjectModerations\" DISABLE TRIGGER USER; UPDATE \"{store.Archive}\".\"ProjectModerations\" SET \"Summary\"='tampered'; ALTER TABLE \"{store.Archive}\".\"ProjectModerations\" ENABLE TRIGGER USER");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, Before));
        Assert.Contains("checksum/schema mismatch", error.MessageText);
        Assert.DoesNotContain("SnapshotJson", await Rows(db, "ProjectModerations"));
        Assert.Contains(After, await db.Database.GetAppliedMigrationsAsync());
    }

    [IsolatedPostgresFact]
    public async Task MissingArchiveRefusesRollbackWithoutRecreatingEmptyHistory()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await SeedData(db); await Migrate(db, After);
        await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA \"{store.Archive}\" CASCADE");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, Before));
        Assert.Contains("manifest missing", error.MessageText);
        Assert.DoesNotContain("SnapshotJson", await Rows(db, "ProjectModerations"));
        Assert.Contains(After, await db.Database.GetAppliedMigrationsAsync());
    }

    [IsolatedPostgresFact]
    public async Task UnknownInboundReferenceBlocksMigrationBeforeAnyDataChanges()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await SeedData(db); var before = await Rows(db, "ProjectModerations"); var cases = await Rows(db, "ProjectVerifications");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE unknown_l1_reference(id uuid REFERENCES \"ProjectModerations\"(\"Id\"))");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, After));
        Assert.Contains("Unexpected inbound", error.MessageText);
        Assert.Equal(before, await Rows(db, "ProjectModerations")); Assert.Equal(cases, await Rows(db, "ProjectVerifications"));
        Assert.DoesNotContain(After, await db.Database.GetAppliedMigrationsAsync());
    }

    [IsolatedPostgresFact]
    public async Task ExistingArchiveCannotBeOverwritten()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await SeedData(db); var before = await Rows(db, "ProjectModerations");
        await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA \"{store.Archive}\"");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, After));
        Assert.Contains("archive schema already exists", error.MessageText);
        Assert.Equal(before, await Rows(db, "ProjectModerations"));
    }

    [IsolatedPostgresFact]
    public async Task UnknownSourceTriggerBlocksMigrationBeforeAnyDataChanges()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await SeedData(db); var before = await Rows(db, "ProjectModerations");
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION unknown_l1_side_effect() RETURNS trigger LANGUAGE plpgsql AS $body$ BEGIN RETURN OLD; END; $body$;
            CREATE TRIGGER unknown_l1_side_effect BEFORE DELETE ON "ProjectModerations" FOR EACH ROW EXECUTE FUNCTION unknown_l1_side_effect();
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, After));
        Assert.Contains("Unexpected L1 source trigger", error.MessageText);
        Assert.Equal(before, await Rows(db, "ProjectModerations"));
    }

    [IsolatedPostgresFact]
    public async Task EmptyDatabaseCanMigrateRollbackAndMigrateAgain()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await Migrate(db, After); await Migrate(db, Before); await Migrate(db, After);
        Assert.Empty(await db.ProjectModerations.ToListAsync());
        Assert.Empty(await db.ProjectVerifications.ToListAsync());
        Assert.Equal("[]", await Rows(db, "ProjectModerations", store.Archive));
    }

    [IsolatedPostgresFact]
    public async Task ChangedCaseProjectRefusesRollbackWithoutOverwritingNewRelationship()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        var seed = await SeedData(db); await Migrate(db, After); db.ChangeTracker.Clear();
        var oldProject = await db.Projects.SingleAsync();
        var otherProject = new Project { CreatorId = oldProject.CreatorId, Title = "New owner relationship",
            Description = "Must not be overwritten by rollback", ProjectField = "IT", MemberTarget = 1, Status = ProjectStatus.Completed };
        db.Projects.Add(otherProject); await db.SaveChangesAsync();
        await db.ProjectVerifications.Where(v => v.Id == seed.Case).ExecuteUpdateAsync(s => s.SetProperty(v => v.ProjectId, otherProject.Id));
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, Before));
        Assert.Contains("linked verification missing or changed project", error.MessageText);
        Assert.Equal(otherProject.Id, await db.ProjectVerifications.Where(v => v.Id == seed.Case).Select(v => v.ProjectId).SingleAsync());
        Assert.DoesNotContain("SnapshotJson", await Rows(db, "ProjectModerations"));
    }

    private static async Task RunPostgresTool(UniNetDbContext db, string tool, params string[] arguments)
    {
        var bin = Environment.GetEnvironmentVariable("UNINET_POSTGRES_BIN") ?? @"C:\Program Files\PostgreSQL\18\bin";
        var executable = Path.Combine(bin, tool + ".exe");
        Assert.True(File.Exists(executable), "Set UNINET_POSTGRES_BIN to the PostgreSQL tools directory for dump/restore validation.");
        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["PGHOST"] = connection.Host;
        start.Environment["PGPORT"] = connection.Port.ToString();
        start.Environment["PGDATABASE"] = connection.Database;
        start.Environment["PGUSER"] = connection.Username;
        start.Environment["PGPASSWORD"] = connection.Password;
        start.Environment["PGSSLMODE"] = "disable";
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await output;
        Assert.True(process.ExitCode == 0, $"{tool} failed: {await error}");
    }

    [IsolatedPostgresFact]
    public async Task CustomFormatBackupRestoresArchiveAndAllowsVerifiedRollback()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await SeedData(db);
        var reviews = await Rows(db, "ProjectModerations"); var cases = await Rows(db, "ProjectVerifications");
        await Migrate(db, After);
        var dump = Path.Combine(Path.GetTempPath(), "uninet-l1-" + Guid.NewGuid().ToString("N") + ".dump");
        try
        {
            await RunPostgresTool(db, "pg_dump", "-w", "--format=custom", "--schema=" + store.Schema,
                "--schema=" + store.Archive, "--schema=" + store.ReportArchive, "--file=" + dump);
            var checksum = SHA256.HashData(await File.ReadAllBytesAsync(dump));
            // Only this test's generated schemas are replaced; never the database or sibling schemas.
            await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA \"{store.Archive}\" CASCADE; DROP SCHEMA \"{store.ReportArchive}\" CASCADE; DROP SCHEMA \"{store.Schema}\" CASCADE");
            await RunPostgresTool(db, "pg_restore", "-w", "--exit-on-error", "--no-owner", "--dbname=" + new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database, dump);
            Assert.Equal(checksum, SHA256.HashData(await File.ReadAllBytesAsync(dump)));
            Assert.Equal(reviews, await Rows(db, "ProjectModerations", store.Archive));
            await Migrate(db, Before);
            Assert.Equal(reviews, await Rows(db, "ProjectModerations"));
            Assert.Equal(cases, await Rows(db, "ProjectVerifications"));
        }
        finally { if (File.Exists(dump)) File.Delete(dump); }
    }
}
