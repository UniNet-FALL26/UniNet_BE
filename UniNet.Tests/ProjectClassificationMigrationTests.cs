using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UniNet.Application;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectClassificationMigrationTests
{
    [IsolatedPostgresFact]
    public async Task DroppingClassificationPreservesProjectsImportDetailsAndActualLeaderIdentity()
    {
        var name = "project_classification_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {name}", admin).ExecuteNonQueryAsync();
        try
        {
            await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261117032216_AddFriendships");
            var owner = new Account { Email = "owner@classification.test", Role = AccountRole.Student,
                Profile = new() { FullName = "Owner", Nickname = "owner" } };
            db.Accounts.Add(owner);
            Project Make(string title, bool leader) => new() { Creator = owner.Profile!, Title = title, Description = "Preserve",
                ProjectField = "IT", Status = ProjectStatus.Completed, SubmitterIsLeader = leader,
                RecruitmentStatus = RecruitmentStatus.Closed, CompletedAt = DateTimeOffset.UtcNow };
            var internalProject = Make("Old internal", false);
            var importedLeader = Make("Imported leader", true);
            importedLeader.SubmitterRole = "Architect";
            var importedNonleader = Make("Imported member", false);
            importedNonleader.ExternalLeaderName = "Original leader";
            importedNonleader.ExternalDetailsJson = "{\"submitterRole\":\"Developer\"}";
            db.Projects.AddRange(internalProject, importedLeader, importedNonleader); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Projects\" SET \"Origin\"=1 WHERE \"Id\"={importedLeader.Id} OR \"Id\"={importedNonleader.Id}");
            await migrator.MigrateAsync(); db.ChangeTracker.Clear();
            var projects = await db.Projects.Include(p => p.Creator).ToDictionaryAsync(p => p.Id);
            Assert.Equal(3, projects.Count);
            Assert.Equal(owner.Profile!.Id, ProjectIdentityMapping.LeaderId(projects[internalProject.Id]));
            Assert.Equal(owner.Profile.Id, ProjectIdentityMapping.LeaderId(projects[importedLeader.Id]));
            Assert.Null(ProjectIdentityMapping.LeaderId(projects[importedNonleader.Id]));
            Assert.Equal("Original leader", ProjectIdentityMapping.Leader(projects[importedNonleader.Id])!.FullName);
            Assert.Equal("Developer", ProjectIdentityMapping.ExternalDetails(projects[importedNonleader.Id])!.SubmitterRole);
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name='Projects' AND column_name IN ('Origin','Mode');";
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
            var explicitNonleader = Make("New explicit nonleader", false);
            explicitNonleader.Creator = projects[internalProject.Id].Creator;
            db.Projects.Add(explicitNonleader); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            Assert.False((await db.Projects.SingleAsync(p => p.Id == explicitNonleader.Id)).SubmitterIsLeader);
        }
        finally { await new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin).ExecuteNonQueryAsync(); }
    }
}
