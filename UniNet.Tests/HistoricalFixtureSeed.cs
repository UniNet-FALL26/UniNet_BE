using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Tests;

// Migration tests intentionally run against an older schema. Seed its existing
// account/profile/project columns instead of issuing current-model INSERTs that
// reference columns introduced after the migration under test. No DDL is changed.
internal static class HistoricalFixtureSeed
{
    public static async Task AccountAsync(UniNetDbContext db, Account account)
    {
        account.Profile!.AccountId = account.Id;
        await InsertAsync(db, account);
        await InsertAsync(db, account.Profile);
        db.Attach(account);
    }

    public static async Task ProjectAsync(UniNetDbContext db, Project project)
    {
        if (project.Creator is not null) project.CreatorId = project.Creator.Id;
        await InsertAsync(db, project);
        db.Attach(project);
    }

    public static async Task SaveAddedAsync(UniNetDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        string[] order = ["Account", "UserProfile", "Package", "Project", "UserVerification", "ProjectModeration",
            "ProjectVerification", "VerificationAssignment", "VerificationReport", "Transaction", "RefundTransaction", "ReportAuditLog"];
        var entries = db.ChangeTracker.Entries().Where(x => x.State == EntityState.Added)
            .OrderBy(x => Array.IndexOf(order, x.Metadata.ClrType.Name) is var i && i >= 0 ? i : 100).ToArray();
        foreach (var entry in entries)
        {
            await InsertAsync(db, entry.Entity);
            entry.State = EntityState.Unchanged;
        }
        if (db.ChangeTracker.Entries().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Historical seed only accepts INSERTs; use the real pipeline for mutations.");
    }

    private static async Task InsertAsync(UniNetDbContext db, object value)
    {
        await db.Database.OpenConnectionAsync();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.Host != "127.0.0.1" || connection.Port is 55432 or 55433 || connection.UserName != "postgres")
            throw new InvalidOperationException("Historical fixture seeding requires local disposable PostgreSQL.");
        var entry = db.Entry(value);
        var table = entry.Metadata.GetTableName()!;
        var store = StoreObjectIdentifier.Table(table, entry.Metadata.GetSchema());
        var existing = new HashSet<string>();
        await using (var catalog = new NpgsqlCommand("SELECT attname FROM pg_attribute WHERE attrelid=to_regclass(@table) AND attnum>0 AND NOT attisdropped", connection))
        {
            catalog.Parameters.AddWithValue("table", '"' + table + '"');
            await using var reader = await catalog.ExecuteReaderAsync();
            while (await reader.ReadAsync()) existing.Add(reader.GetString(0));
        }
        await using var command = connection.CreateCommand();
        var historicalRoles = false;
        if (value is Account)
        {
            await using var roleCheck = new NpgsqlCommand("SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid=to_regclass('\"Accounts\"') AND conname='CK_Accounts_Role'", connection);
            historicalRoles = ((string?)await roleCheck.ExecuteScalarAsync())?.Contains("4", StringComparison.Ordinal) == true;
        }
        var columns = new List<string>(); var parameters = new List<string>();
        foreach (var property in entry.Metadata.GetProperties())
        {
            var column = property.GetColumnName(store);
            if (column is null || !existing.Contains(column)) continue;
            var name = "p" + parameters.Count;
            columns.Add('"' + column.Replace("\"", "\"\"") + '"');
            parameters.Add("@" + name);
            var item = entry.Property(property.Name).CurrentValue;
            if (historicalRoles && property.Name == "Role" && item is AccountRole role)
                item = role switch { AccountRole.Admin => (AccountRole)2, AccountRole.Moderator => (AccountRole)3, AccountRole.Assessor => (AccountRole)4, _ => role };
            command.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(command, name, item));
        }
        // Internal was the explicit historical classification for these fixtures.
        if (value is Project && existing.Contains("Origin"))
        { columns.Add("\"Origin\""); parameters.Add("0"); }
        command.CommandText = $"INSERT INTO \"{table}\" ({string.Join(',', columns)}) VALUES ({string.Join(',', parameters)})";
        await command.ExecuteNonQueryAsync();
    }
}
