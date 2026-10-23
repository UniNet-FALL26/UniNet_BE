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

public sealed class VerificatorAvailabilityMigrationPostgresTests
{
    [EvidencePostgresFact]
    public async Task MigrationRoundTripPreservesExistingAssignmentAndBackfillsReservedResource()
    {
        var connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        var schema = "availability_migration_" + Guid.NewGuid().ToString("N");
        var archive = "verification_archive_" + Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(schema))).ToLowerInvariant();
        await using var adminConnection = new NpgsqlConnection(connection);
        await adminConnection.OpenAsync();
        // Identifiers are generated here, never supplied by a caller or configuration.
#pragma warning disable EF1002
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", adminConnection).ExecuteNonQueryAsync();
        try
        {
            var options = new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
                new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString,
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options;
            Guid verificationId, staffId, assignmentId;
            await using (var db = new UniNetDbContext(options))
            {
                await db.GetService<IMigrator>().MigrateAsync();
                Account User(string name, AccountRole role) => new()
                {
                    Email = name + "@migration.test", Role = role,
                    Profile = new() { FullName = name, Nickname = name }
                };
                var owner = User("Owner", AccountRole.Student);
                var staff = User("Staff", AccountRole.Verificator);
                var admin = User("Admin", AccountRole.Admin);
                db.Accounts.AddRange(owner, staff, admin);
                var project = new Project { Creator = owner.Profile!, Title = "Completed", Description = "Done",
                    ProjectField = "IT", MemberTarget = 2, Status = ProjectStatus.Completed };
                var package = new Package { Id = Guid.NewGuid(), Code = "VERIFY", Name = "Verification", IsActive = true,
                    Type = PackageType.Verification, Role = TargetRole.Student, Price = 100 };
                var verification = new ProjectVerification { Id = Guid.NewGuid(), Project = project, Package = package,
                    RequestByUser = owner.Profile!, Status = ProjectVerificationStatus.Pending,
                    ScheduledStartAt = DateTimeOffset.UtcNow.AddDays(2), ScheduledEndAt = DateTimeOffset.UtcNow.AddDays(2).AddHours(1) };
                var assignment = new VerificationAssignment { Id = Guid.NewGuid(), ProjectVerification = verification,
                    AssignedToUserId = staff.Profile!.Id, AdminId = admin.Profile!.Id, AssignedAt = DateTimeOffset.UtcNow };
                db.VerificationAssignments.Add(assignment);
                await db.SaveChangesAsync();
                (verificationId, staffId, assignmentId) = (verification.Id, staff.Profile.Id, assignment.Id);
                await db.GetService<IMigrator>().MigrateAsync("20261020024442_AddVerificationMeetingRoom");
                await db.GetService<IMigrator>().MigrateAsync();
            }
            await using var check = new UniNetDbContext(options);
            Assert.Equal(staffId, (await check.ProjectVerifications.SingleAsync(v => v.Id == verificationId)).ReservedVerificatorUserId);
            Assert.Equal(staffId, (await check.VerificationAssignments.SingleAsync(a => a.Id == assignmentId)).AssignedToUserId);
            Assert.Empty(await check.VerificatorAvailabilities.ToListAsync());
            Assert.False(check.Database.HasPendingModelChanges());
        }
        finally
        {
            await new NpgsqlCommand($"DROP SCHEMA IF EXISTS {archive} CASCADE; DROP SCHEMA IF EXISTS {schema} CASCADE", adminConnection).ExecuteNonQueryAsync();
        }
#pragma warning restore EF1002
    }
}
