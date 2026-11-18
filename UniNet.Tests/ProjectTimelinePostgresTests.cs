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

public sealed class ProjectTimelinePostgresTests
{
    [IsolatedPostgresFact]
    public async Task MigrationKeepsExistingDatesEmptyAndNewProjectsUseAutomaticStart()
    {
        var database = "timeline_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQueryAsync();
        try
        {
            await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            var previous = db.Database.GetMigrations().TakeWhile(id => !id.EndsWith("_AddProjectTimelineDates")).Last();
            await migrator.MigrateAsync(previous);
            var owner = new Account { Email = "timeline@test.local", Role = AccountRole.Student,
                Profile = new() { FullName = "Owner", Nickname = "timeline-owner" } };
            var project = new Project { Creator = owner.Profile!, Title = "Existing", Description = "Dates", ProjectField = "IT", Status = ProjectStatus.Active };
            db.Accounts.Add(owner); db.Projects.Add(project); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ProjectDetails\" (\"ProjectId\", \"UpdatedAt\") VALUES ({project.Id}, {DateTimeOffset.UtcNow})");
            await migrator.MigrateAsync(); db.ChangeTracker.Clear();
            var service = new ProjectHubService(db);
            var existing = await service.GetAsync(project.Id, owner.Id, default);
            Assert.Null(existing.Details.StartDate); Assert.Null(existing.Details.EndDate);
            Assert.Null(existing.Details.ActualEndDate); Assert.False(existing.Details.StartDateIsCreationDate);
            var created = await service.CreateAsync(owner.Id, new() { Title = "New", Description = "Timeline", ExpectedEndDate = new(2099, 12, 31) }, default);
            Assert.Equal(DateOnly.FromDateTime(created.CreatedAt.ToOffset(TimeSpan.FromHours(7)).DateTime), created.Details.StartDate);
            Assert.True(created.Details.StartDateIsCreationDate);
            var updated = await service.UpdateAsync(project.Id, owner.Id,
                new() { Timeline = new(new(2020, 1, 1), new(2020, 3, 1), new(2020, 4, 1)) }, default);
            Assert.Equal(new DateOnly(2020, 4, 1), updated.Details.ActualEndDate);
        }
        finally
        {
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin).ExecuteNonQueryAsync();
        }
    }
}
