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

public sealed class RemoveProjectExpectedEndDateMigrationTests
{
    [IsolatedPostgresFact]
    public async Task RemovingPlannedDatesPreservesActualAndConfirmedImportDates()
    {
        var database = "remove_expected_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQueryAsync();
        try
        {
            await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            var previous = db.Database.GetMigrations().TakeWhile(id => !id.EndsWith("_RemoveProjectExpectedEndDates")).Last();
            await migrator.MigrateAsync(previous);
            var owner = new Account { Email = "dates@migration.test", Role = AccountRole.Student,
                Profile = new() { FullName = "Owner", Nickname = "dates" } };
            Project Make(string title, DateOnly? actual, string? importedEnd = null)
            {
                var project = new Project { Creator = owner.Profile!, Title = title, Description = "Dates",
                    ProjectField = "IT", Status = ProjectStatus.Completed, ImplementationEndDate = importedEnd };
                project.Details = new() { ProjectId = project.Id, StartDate = new(2020, 1, 1), ActualEndDate = actual };
                return project;
            }
            var actual = new DateOnly(2020, 5, 5);
            var confirmed = Make("Actual", actual);
            var imported = Make("Imported", null, "2021-06-02");
            var plannedOnly = Make("Planned only", null);
            db.Accounts.Add(owner); db.Projects.AddRange(confirmed, imported, plannedOnly);
            await db.SaveChangesAsync();
            foreach (var project in new[] { confirmed, imported, plannedOnly })
            {
                var oldEnd = project == imported ? new DateOnly(2021, 6, 2) : new DateOnly(2030, 1, 1);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectDetails\" SET \"EndDate\" = {oldEnd} WHERE \"ProjectId\" = {project.Id}");
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Projects\" SET \"ExpectedOutput\" = {DateTimeOffset.UtcNow.AddDays(30)} WHERE \"Id\" = {project.Id}");
            }
            await migrator.MigrateAsync(); db.ChangeTracker.Clear();
            Assert.Equal(actual, (await db.ProjectDetails.SingleAsync(d => d.ProjectId == confirmed.Id)).ActualEndDate);
            Assert.Equal(new DateOnly(2021, 6, 2), (await db.ProjectDetails.SingleAsync(d => d.ProjectId == imported.Id)).ActualEndDate);
            Assert.Null((await db.ProjectDetails.SingleAsync(d => d.ProjectId == plannedOnly.Id)).ActualEndDate);
            Assert.Equal(3, await db.Projects.CountAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND ((table_name = 'ProjectDetails' AND column_name = 'EndDate')
                    OR (table_name = 'Projects' AND column_name = 'ExpectedOutput'))
                """).SingleAsync());
        }
        finally
        {
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin).ExecuteNonQueryAsync();
        }
    }
}
