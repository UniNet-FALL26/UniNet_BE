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

public sealed class ProjectRefactorMigrationPostgresTests
{
    [IsolatedPostgresFact]
    public async Task BackfillPreservesSourcesAndInvitationHistoryWithoutInventingExternalRecruitment()
    {
        var database = "project_refactor_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        // Historical migrations create fixed archive schemas: isolate the whole database.
        await new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQueryAsync();
        try
        {
            await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connection) { Database = database, SearchPath = "public", Pooling = false }.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261116023634_PrepareSimplifiedProjectSchema");
            var owner = new Account { Email = "owner@project.local", Role = AccountRole.Student,
                Profile = new() { FullName = "Owner", Nickname = "Owner" } };
            var guest = new Account { Email = "guest@project.local", Role = AccountRole.Student,
                Profile = new() { FullName = "Guest", Nickname = "Guest" } };
            db.Accounts.AddRange(owner, guest);
            var skill = new Skill { Name = "C#" };
            db.Skills.Add(skill);
            var project = new Project { Creator = owner.Profile!, Title = "Community", Description = "Original",
                ProjectField = "IT", MemberTarget = 3, MinimumQuantity = 1, Status = ProjectStatus.Active,
                RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(3) };
            var positionId = Guid.NewGuid();
            var external = new Project { Creator = owner.Profile!, Title = "External", Description = "Original external",
                ProjectField = "IT", SubmitterIsLeader = false, Status = ProjectStatus.Completed,
                RecruitmentStatus = RecruitmentStatus.Closed, MemberTarget = null, MinimumQuantity = null,
                RecruitmentDeadline = null, ImplementationStartDate = "2026-02-30", ImplementationEndDate = "2026-11-15",
                ExternalDetailsJson = "{\"personalClaim\":\"preserve me\"}", CompletedAt = DateTimeOffset.UtcNow };
            db.Projects.Add(project);
            var invitations = Enumerable.Range(0, 5).Select(status => (Id: Guid.NewGuid(), Status: status)).ToArray();
            db.ProjectJoinRequests.Add(new() { Id = Guid.NewGuid(), Project = project, User = guest.Profile!,
                Role = "Backend", Status = ProjectJoinRequestStatus.Expired,
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            // Seed the historical column explicitly: the current EF model no longer maps it.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Projects" ("Id","CreatorId","Title","Description","ProjectField","Origin","Status",
                    "RecruitmentStatus","Visibility","SubmitterIsLeader","MemberTarget","MinimumQuantity","RecruitmentDeadline","CreatedAt","UpdatedAt",
                    "ImplementationStartDate","ImplementationEndDate","ExternalDetailsJson","CompletedAt")
                VALUES ({external.Id},{owner.Profile!.Id},{external.Title},{external.Description},'IT',1,2,2,1,false,NULL,NULL,NULL,
                    {external.CreatedAt},{external.UpdatedAt},{external.ImplementationStartDate},
                    {external.ImplementationEndDate},{external.ExternalDetailsJson}::jsonb,{external.CompletedAt});
                """);
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ProjectRoleRequirements" ("Id","ProjectId","Role","Quantity","Requirements","CreatedAt","UpdatedAt")
                VALUES ({positionId},{project.Id},'Backend',2,'Git',{now},{now});
                INSERT INTO "ProjectSkills" ("ProjectId","SkillId") VALUES ({project.Id},{skill.Id});
                """);
            foreach (var invite in invitations)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "ProjectInvitations" ("Id","ProjectId","InviteeId","InviterId","Role","Message","Status","CreatedAt","UpdatedAt")
                    VALUES ({invite.Id},{project.Id},{guest.Profile!.Id},{owner.Profile!.Id},'Backend','History',{(short)invite.Status},{now},{now});
                    """);
            const string draftData = "{\"important\":\"Retain draft\"}";
            var taskId = Guid.NewGuid(); var reportId = Guid.NewGuid(); var channelId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ProjectTasks" ("Id","ProjectId","CreatorId","Title","Description","Status","Deadline","CreatedAt","UpdatedAt")
                VALUES ({taskId},{project.Id},{owner.Profile!.Id},'Retain task','Original',2,{now},{now},{now});
                INSERT INTO "ProjectTaskAssignments" ("Id","ProjectTaskId","UserId","AssignedAt")
                VALUES ({Guid.NewGuid()},{taskId},{guest.Profile!.Id},{now});
                INSERT INTO "TaskReports" ("Id","ProjectTaskId","SubmittedById","ReportGroupId","Version","Content","Status","SubmittedAt","UpdatedAt","ConcurrencyToken","IsDeleted")
                VALUES ({reportId},{taskId},{guest.Profile!.Id},{Guid.NewGuid()},1,'Retain report',1,{now},{now},{Guid.NewGuid()},false);
                INSERT INTO "TaskEvidence" ("Id","TaskReportId","FileUrl","CreatedAt")
                VALUES ({Guid.NewGuid()},{reportId},'https://example.test/evidence',{now});
                INSERT INTO "TaskReportReviews" ("Id","TaskReportId","ReviewerId","Decision","Feedback","ReviewedAt")
                VALUES ({Guid.NewGuid()},{reportId},{owner.Profile!.Id},0,'Retain feedback',{now});
                INSERT INTO "WorkspaceChannels" ("Id","ProjectId","CreatedById","Name","IsDeleted","CreatedAt","UpdatedAt")
                VALUES ({channelId},{project.Id},{owner.Profile!.Id},'Retain channel',false,{now},{now});
                INSERT INTO "WorkspaceMessages" ("Id","ChannelId","SenderId","Content","CreatedAt")
                VALUES ({Guid.NewGuid()},{channelId},{guest.Profile!.Id},'Retain message',{now});
                INSERT INTO "ExternalProjectDrafts" ("Id","OwnerUserId","Status","EditedDataJson","Version","CreatedAt","UpdatedAt","ExpiresAt")
                VALUES ({Guid.NewGuid()},{owner.Profile!.Id},1,{draftData}::jsonb,1,{now},{now},{now.AddDays(7)});
                """);
            db.ChangeTracker.Clear();
            var originalJson = await db.Projects.Where(x => x.Id == external.Id).Select(x => x.ExternalDetailsJson).SingleAsync();

            await migrator.MigrateAsync("20261116023712_BackfillSimplifiedProjectSchema");

            foreach (var old in invitations)
            {
                var copied = await db.ProjectJoinRequests.SingleAsync(x => x.Id == old.Id);
                Assert.Equal(ProjectRequestType.Invite, copied.RequestType);
                Assert.Equal(guest.Profile!.Id, copied.UserId);
                Assert.Equal(owner.Profile!.Id, copied.InviterId);
                Assert.Equal((short)old.Status, (short)copied.Status);
                Assert.Equal("History", copied.Message);
                Assert.Equal(now.ToUnixTimeMilliseconds(), copied.CreatedAt.ToUnixTimeMilliseconds());
            }
            Assert.Equal(6, await db.ProjectJoinRequests.CountAsync());
            Assert.Equal("[\"C#\"]", (await db.Projects.SingleAsync(x => x.Id == project.Id)).Technologies);
            var recruitment = await db.ProjectRecruitments.SingleAsync();
            Assert.Equal(project.Id, recruitment.ProjectId);
            Assert.Contains(positionId.ToString(), recruitment.Positions);
            Assert.Equal(ProjectRecruitmentStatus.Open, recruitment.Status);
            Assert.Null((await db.ProjectDetails.SingleAsync(x => x.ProjectId == external.Id)).StartDate);
            Assert.Equal(new DateOnly(2026, 11, 15), (await db.ProjectDetails.SingleAsync(x => x.ProjectId == external.Id)).EndDate);
            Assert.Equal(originalJson, (await db.Projects.SingleAsync(x => x.Id == external.Id)).ExternalDetailsJson);
            Assert.Equal(0, await db.ProjectPersonalReviews.CountAsync());
            Assert.Equal(0, await db.ProjectMembers.CountAsync());
            string[] removed = ["ProjectRoleRequirements","ProjectSkills","ProjectInvitations","WorkspaceChannels","WorkspaceMessages",
                "ProjectTasks","ProjectTaskAssignments","TaskReports","TaskEvidence","TaskReportReviews","ExternalProjectDrafts"];
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'UTC'");
            async Task<string> Scalar(string sql)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = sql;
                return (string)(await command.ExecuteScalarAsync())!;
            }
            var originals = new Dictionary<string,string>();
            foreach (var table in removed)
                originals[table] = await Scalar($"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text COLLATE \"C\"), '[]'::jsonb)::text FROM \"{table}\" t");
            // Identity collisions must abort the archive/drop transaction atomically.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectJoinRequests\" SET \"RequestType\"=0 WHERE \"Id\"={invitations[0].Id}");
            await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261116030240_ArchiveAndRemoveLegacyProjectTables"));
            Assert.Equal("11", await Scalar("SELECT count(*)::text FROM information_schema.tables WHERE table_schema='public' AND table_name IN ('ProjectRoleRequirements','ProjectSkills','ProjectInvitations','WorkspaceChannels','WorkspaceMessages','ProjectTasks','ProjectTaskAssignments','TaskReports','TaskEvidence','TaskReportReviews','ExternalProjectDrafts')"));
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ProjectJoinRequests\" SET \"RequestType\"=1 WHERE \"Id\"={invitations[0].Id}");
            await migrator.MigrateAsync("20261116030240_ArchiveAndRemoveLegacyProjectTables");
            var hash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("public"))).ToLowerInvariant();
            var archive = "project_legacy_archive_" + hash;
            foreach (var table in removed)
            {
                Assert.Equal("", await Scalar($"SELECT COALESCE(to_regclass('public.\"{table}\"')::text,'')"));
                Assert.Equal(originals[table], await Scalar($"SELECT \"Data\"::text FROM {archive}.\"Snapshots\" WHERE \"TableName\"='{table}'"));
            }
            var readonlyError = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync($"TRUNCATE {archive}.\"Snapshots\""));
            Assert.Contains("read-only", readonlyError.MessageText);
            db.ChangeTracker.Clear();
            var migratedProject = await db.Projects.SingleAsync(p => p.Id == project.Id);
            Assert.Equal(skill.Id, Assert.Single(migratedProject.ProjectSkills).SkillId);
            Assert.Equal("C#", Assert.Single(migratedProject.ProjectSkills).Name);
            Assert.Equal(positionId.ToString(), Assert.Single(migratedProject.RoleRequirements).Id);
            Assert.Equal(6, await db.ProjectJoinRequests.CountAsync());
            Assert.Equal(1, await db.Skills.CountAsync(s => s.Id == skill.Id));
            await migrator.MigrateAsync("20261116030240_ArchiveAndRemoveLegacyProjectTables");
            await Assert.ThrowsAsync<NotSupportedException>(() => migrator.MigrateAsync("20261116023712_BackfillSimplifiedProjectSchema"));

        }
        finally
        {
            // Only the database created by this fixture is eligible for cleanup.
            await new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin) { CommandTimeout = 120 }.ExecuteNonQueryAsync();
        }
    }
}
