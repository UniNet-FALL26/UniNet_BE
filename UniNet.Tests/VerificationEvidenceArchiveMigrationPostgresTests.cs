using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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

#pragma warning disable EF1002 // Identifiers are generated exclusively by this isolated fixture.
public sealed class VerificationEvidenceArchiveMigrationPostgresTests
{
    const string Before = "20261108142140_ArchiveAndRemoveLevel1";
    const string After = "20261109013212_ArchiveAndRemoveVerificationEvidence";
    sealed class Store : IAsyncDisposable
    {
        public readonly string Schema = "evidence_test_" + Guid.NewGuid().ToString("N");
        readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        string Hash => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(Schema))).ToLowerInvariant();
        public string Archive => "verification_evidence_archive_" + Hash;
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
            new NpgsqlConnectionStringBuilder(connection) { SearchPath = Schema }.ConnectionString,
            o => o.MigrationsHistoryTable("__EFMigrationsHistory", Schema)).Options);
        public async Task Initialize()
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE SCHEMA {Schema}", admin).ExecuteNonQueryAsync();
            await using var db = Open(); await Migrate(db, Before);
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            foreach (var schema in new[] { Archive, "l1_archive_" + Hash, "verification_archive_" + Hash, Schema })
                await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", admin).ExecuteNonQueryAsync();
        }
    }
    static Task Migrate(UniNetDbContext db, string target) => db.GetService<IMigrator>().MigrateAsync(target);
    static async Task<string> Rows(UniNetDbContext db, string table, string? schema = null)
    {
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SET TIME ZONE 'UTC'; SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY \"Id\"),'[]'::jsonb)::text FROM {(schema == null ? "" : '"' + schema + "\".")}\"{table}\" t";
        return (string)(await cmd.ExecuteScalarAsync())!;
    }
    static async Task<Guid> Seed(UniNetDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var owner = new Account { Email = "owner@evidence.local", Role = AccountRole.Student,
            Profile = new() { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Preserved", Description = "Completed",
            ProjectField = "IT", MemberTarget = 2, Status = ProjectStatus.Completed };
        var package = new Package { Id = Guid.NewGuid(), Code = "VERIFY", Name = "Verification", Price = 15000,
            Type = PackageType.Verification, Role = TargetRole.Student, IsActive = true };
        var verification = new ProjectVerification { Id = Guid.NewGuid(), Project = project, RequestByUser = owner.Profile!,
            Package = package, Status = ProjectVerificationStatus.Pending, SubmittedAt = now, CreatedAt = now, UpdatedAt = now };
        db.Accounts.Add(owner); db.ProjectVerifications.Add(verification);
        db.Transactions.Add(new() { Id = Guid.NewGuid(), UserId = owner.Profile!.Id, Package = package, Amount = 15000,
            Verification = verification, Status = PaymentStatus.Success, PaidAt = now, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        db.Set<VerificationNotification>().Add(new() { Id = Guid.NewGuid(), RecipientUserId = owner.Id, Message = "Retain inbox", CreatedAt = now });
        await db.SaveChangesAsync();
        const string payload = "[{\"url\":\"https://example.com/demo\",\"note\":\"Bằng chứng gốc\"}]";
        for (var version = 1; version <= 3; version++)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ProjectVerificationEvidence" ("Id","ProjectVerificationId","SubmittedByUserId","EvidenceJson","SubmittedAt","Version")
                VALUES ({Guid.NewGuid()},{verification.Id},{owner.Profile.Id},{payload}::jsonb,{now.AddMinutes(version)},{version})
                """);
        return verification.Id;
    }

    [IsolatedPostgresFact]
    public async Task ArchiveRoundTripsAndPreservesCurrentWorkflow()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await Seed(db); var original = await Rows(db, "ProjectVerificationEvidence");
        string[] tables = ["Projects", "ProjectVerifications", "Transactions", "Notifications", "VerificationReports", "VerificationAssignments"];
        var before = new Dictionary<string,string>(); foreach (var table in tables) before[table] = await Rows(db, table);
        await Migrate(db, After);
        Assert.Equal(original, await Rows(db, "ProjectVerificationEvidence", store.Archive));
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.GetTableName() == "ProjectVerificationEvidence");
        Assert.Null(db.Model.FindEntityType(typeof(ProjectVerification))!.FindNavigation("EvidenceSubmissions"));
        foreach (var table in tables) Assert.Equal(before[table], await Rows(db, table));
        foreach (var table in new[] { "ProjectVerificationEvidence", "VerificationParents", "SubmitterParents", "Manifest" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync($"TRUNCATE \"{store.Archive}\".\"{table}\""));
            Assert.Contains("read-only", error.MessageText);
        }
        await Migrate(db, Before);
        Assert.Equal(original, await Rows(db, "ProjectVerificationEvidence"));
        foreach (var table in tables) Assert.Equal(before[table], await Rows(db, table));
        await Migrate(db, After);
    }

    [IsolatedPostgresFact]
    public async Task TamperedArchiveRefusesRollbackAtomically()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await Seed(db); await Migrate(db, After);
        await db.Database.ExecuteSqlRawAsync($"ALTER TABLE \"{store.Archive}\".\"ProjectVerificationEvidence\" DISABLE TRIGGER USER; UPDATE \"{store.Archive}\".\"ProjectVerificationEvidence\" SET \"Version\"=\"Version\"+10; ALTER TABLE \"{store.Archive}\".\"ProjectVerificationEvidence\" ENABLE TRIGGER USER");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, Before));
        Assert.Contains("checksum/schema mismatch", error.MessageText);
        Assert.Contains(After, await db.Database.GetAppliedMigrationsAsync());
    }

    [IsolatedPostgresFact]
    public async Task MissingParentPreservesArchiveAndRefusesRollback()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        var id = await Seed(db); await Migrate(db, After);
        var original = await Rows(db, "ProjectVerificationEvidence", store.Archive);
        await db.Transactions.Where(t => t.VerificationId == id).ExecuteDeleteAsync();
        await db.ProjectVerifications.Where(v => v.Id == id).ExecuteDeleteAsync();
        Assert.Equal(original, await Rows(db, "ProjectVerificationEvidence", store.Archive));
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, Before));
        Assert.Contains("parent case missing or reassociated", error.MessageText);
    }

    [IsolatedPostgresFact]
    public async Task MissingArchiveRefusesRollback()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await Migrate(db, After); await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA \"{store.Archive}\" CASCADE");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, Before));
        Assert.Contains("manifest missing", error.MessageText);
    }

    [IsolatedPostgresFact]
    public async Task UnexpectedInboundDependencyBlocksRemoval()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await Seed(db); var original = await Rows(db, "ProjectVerificationEvidence");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE unexpected_dependency (id uuid REFERENCES \"ProjectVerificationEvidence\"(\"Id\"))");
        var error = await Assert.ThrowsAsync<PostgresException>(() => Migrate(db, After));
        Assert.Contains("Unexpected inbound", error.MessageText);
        Assert.Equal(original, await Rows(db, "ProjectVerificationEvidence"));
    }

    [IsolatedPostgresFact]
    public async Task EmptyDatabaseRoundTrips()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await Migrate(db, After); await Migrate(db, Before); await Migrate(db, After);
        Assert.Equal("[]", await Rows(db, "ProjectVerificationEvidence", store.Archive));
    }
    [IsolatedPostgresFact]
    public async Task BackupRestoresArchiveAndVerifiedRollback()
    {
        await using var store = new Store(); await store.Initialize(); await using var db = store.Open();
        await Seed(db); var original = await Rows(db, "ProjectVerificationEvidence"); await Migrate(db, After);
        var dump = Path.Combine(Path.GetTempPath(), "uninet-evidence-" + Guid.NewGuid().ToString("N") + ".dump");
        try
        {
            await RunPostgresTool(db, "pg_dump", "-w", "--format=custom", "--schema=" + store.Archive, "--file=" + dump);
            await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA \"{store.Archive}\" CASCADE");
            await RunPostgresTool(db, "pg_restore", "-w", "--exit-on-error", "--no-owner", "--dbname=" + new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database, dump);
            Assert.Equal(original, await Rows(db, "ProjectVerificationEvidence", store.Archive));
            await Migrate(db, Before); Assert.Equal(original, await Rows(db, "ProjectVerificationEvidence"));
        }
        finally { if (File.Exists(dump)) File.Delete(dump); }
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

}
