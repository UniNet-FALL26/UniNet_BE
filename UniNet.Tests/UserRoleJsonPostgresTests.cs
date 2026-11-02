using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class UserRoleJsonPostgresFactAttribute : FactAttribute
{
    public UserRoleJsonPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNINET_ROLE_JSON_TEST_POSTGRES")))
            Skip = "Set UNINET_ROLE_JSON_TEST_POSTGRES to an isolated local PostgreSQL test database.";
    }
}

public sealed class UserRoleJsonPostgresTests
{
    private const string Before = "20261101155015_AddProjectAssessmentRubric";
    private const string After = "20261102014809_UserRoleJson";
    private static readonly DateTimeOffset Created = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [UserRoleJsonPostgresFact]
    public async Task MigrationPreservesLegacyValuesSupportsRealCrudAndGuardsLossyDowngrade()
    {
        await using var store = await Store.Create();
        await using var db = store.Open();
        await db.GetService<IMigrator>().MigrateAsync(Before);
        var rows = new[]
        {
            new Legacy(AccountRole.Student, "Đại học FPT", "", null, "", null, null, ""),
            new Legacy(AccountRole.Partner, null, null, null, "Công ty Kỹ năng Việt", PartnerType.TrainingCenter, "", "[\"https://example.test/đối-tác\",\"https://second.example.test\"]"),
            new Legacy(AccountRole.Assessor, "Đại học Bách khoa", "Backend & Trí tuệ nhân tạo", "", "Công ty chuyên gia", null, null, null),
            new Legacy(AccountRole.Student, null, null, null, null, null, null, null, AccountStatus.Suspended)
        };
        foreach (var row in rows) await Seed(db, row);
        await db.GetService<IMigrator>().MigrateAsync(After);
        Assert.False(db.Database.HasPendingModelChanges());
        foreach (var row in rows)
        {
            var account = await db.Accounts.AsNoTracking().Include(a => a.Profile).SingleAsync(a => a.Id == row.AccountId);
            Assert.Equal(row.Role, account.Role); Assert.Equal(row.Status, account.Status);
            Assert.Equal(7, account.SessionVersion); Assert.True(account.EmailVerified);
            Assert.Equal(Created, account.CreatedAt);
            var profile = account.Profile!;
            Assert.Equal(row.UserId, profile.Id); Assert.Equal(row.AccountId, profile.AccountId);
            Assert.Equal("Nguyễn Văn " + row.Role, profile.FullName); Assert.Equal("nickname", profile.Nickname);
            Assert.Equal("https://images.example.test/avatar.png", profile.AvatarUrl);
            Assert.Equal("https://images.example.test/cover.png", profile.CoverUrl);
            Assert.Equal("Giới thiệu có dấu", profile.Bio); Assert.Equal("contact@example.test", profile.ContactEmail);
            Assert.Equal("0901234567", profile.Phone); Assert.Equal("Thành phố Hồ Chí Minh", profile.Address);
            Assert.True(profile.IsVerified); Assert.Equal(Created, profile.CreatedAt); Assert.Equal(Created, profile.UpdatedAt);
            Assert.Equal(row.University, profile.UniversityName); Assert.Equal(row.Major, profile.Major);
            Assert.Equal(row.StudentCode, profile.StudentCode); Assert.Equal(row.Organization, profile.OrganizationName);
            Assert.Equal(row.PartnerType, profile.PartnerType); Assert.Equal(row.Industry, profile.Industry); Assert.Equal(row.Website, profile.Website);
        }
        var assessor = await db.Accounts.AsNoTracking().Include(a => a.Profile).SingleAsync(a => a.Id == rows[2].AccountId);
        Assert.Equal(rows[2].University, assessor.Profile!.AssessorJson!.University);
        Assert.Equal(rows[2].Organization, assessor.Profile.AssessorJson.CompanyName);
        Assert.Equal(new[] { rows[2].Major }, assessor.Profile.AssessorJson.Expertise);
        Assert.Equal(0, assessor.Profile.AssessorJson.YearsOfExperience);
        Assert.Null(assessor.Profile.AssessorJson.JobTitle);
        var blank = await db.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == rows[3].UserId);
        Assert.Null(blank.StudentJson); Assert.Null(blank.PartnerJson); Assert.Null(blank.AssessorJson);
        Assert.Equal(rows[0].UserId, await db.UserProfiles.Where(p => p.StudentJson!.UniversityName == rows[0].University).Select(p => p.Id).SingleAsync());
        Assert.Equal(rows[1].UserId, await db.UserProfiles.Where(p => p.PartnerJson!.OrganizationName == rows[1].Organization).Select(p => p.Id).SingleAsync());

        var admin = new Account { Email = "role-json-admin@test.local", Role = AccountRole.Admin };
        var fresh = new Account { Email = "role-json-new-student@test.local", Role = AccountRole.Student, Profile = new() { FullName = "New student", Nickname = "new" } };
        var expert = new Account { Email = "role-json-new-assessor@test.local", Role = AccountRole.Assessor, Profile = new() { FullName = "New expert", Nickname = "expert" } };
        db.AddRange(admin, fresh, expert); await db.SaveChangesAsync();
        var search = await new AdminUsersService(db).ListAsync(admin.Id, new(Search: "Kỹ năng Việt"), default);
        Assert.Equal(rows[1].AccountId, Assert.Single(search.Items).Id);
        var profiles = new ProfileService(db);
        await profiles.Student(fresh.Id, new("Sinh viên mới", null, null, "Tiểu sử", "Trường mới", "Ngành mới", "SE999"), default);
        var saved = await profiles.Assessor(expert.Id, new("Chuyên gia mới", "expert", null, null, "Tiểu sử chuyên gia", "expert@example.test", "0907654321", "Hà Nội",
            new("Kỹ sư", "Công ty ABC", 8, ["Backend", "Kiến trúc phần mềm"], "Thạc sĩ", "Đại học Quốc gia", "https://linkedin.com/in/expert", "https://github.com/expert", "https://expert.example.test")), default);
        Assert.Equal(8, saved.Profile!.AssessorJson!.YearsOfExperience);
        db.ChangeTracker.Clear();
        var persisted = await db.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == expert.Profile!.Id);
        Assert.Equal(new[] { "Backend", "Kiến trúc phần mềm" }, persisted.AssessorJson!.Expertise);
        Assert.Equal("https://github.com/expert", persisted.AssessorJson.GitHubUrl);
        var newStudent = await db.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == fresh.Profile!.Id);
        Assert.Equal("Trường mới", newStudent.UniversityName); Assert.Equal("SE999", newStudent.StudentCode);
        await profiles.ClearAssessor(expert.Id, default);
        db.ChangeTracker.Clear();
        Assert.Null((await db.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == expert.Profile!.Id)).AssessorJson);

        var blocked = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(Before));
        Assert.Contains("Export assessor profile JSON", blocked.MessageText);
        Assert.Contains(After, await db.Database.GetAppliedMigrationsAsync());
        // Explicit export/clear prerequisite is performed only inside this generated test schema.
        await db.Database.ExecuteSqlRawAsync("UPDATE \"Users\" SET \"AssessorJson\" = NULL");
        await db.GetService<IMigrator>().MigrateAsync(Before);
        foreach (var row in rows) await AssertRestored(db, row);
        Assert.DoesNotContain(After, await db.Database.GetAppliedMigrationsAsync());
    }

    private sealed record Legacy(AccountRole Role, string? University, string? Major, string? StudentCode,
        string? Organization, PartnerType? PartnerType, string? Industry, string? Website, AccountStatus Status = AccountStatus.Active)
    {
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
    }

    private static async Task Seed(UniNetDbContext db, Legacy row)
    {
        var role = (short)row.Role; var status = (short)row.Status; var type = (short?)row.PartnerType;
        var email = row.AccountId + "@migration.test"; var fullName = "Nguyễn Văn " + row.Role;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Accounts" ("Id", "Email", "Role", "Status", "EmailVerified", "SessionVersion", "CreatedAt", "UpdatedAt")
                VALUES ({row.AccountId}, {email}, {role}, {status}, TRUE, 7, {Created}, {Created});
            INSERT INTO "Users" ("Id", "AccountId", "FullName", "Nickname", "AvatarUrl", "CoverUrl", "Bio", "ContactEmail", "Phone", "Address", "IsVerified", "CreatedAt", "UpdatedAt",
                "UniversityName", "Major", "StudentCode", "OrganizationName", "PartnerType", "Industry", "Website")
                VALUES ({row.UserId}, {row.AccountId}, {fullName}, 'nickname', 'https://images.example.test/avatar.png', 'https://images.example.test/cover.png', 'Giới thiệu có dấu',
                    'contact@example.test', '0901234567', 'Thành phố Hồ Chí Minh', TRUE, {Created}, {Created},
                    {row.University}, {row.Major}, {row.StudentCode}, {row.Organization}, {type}, {row.Industry}, {row.Website});
            """);
    }

    private static async Task AssertRestored(UniNetDbContext db, Legacy row)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT \"UniversityName\", \"Major\", \"StudentCode\", \"OrganizationName\", \"PartnerType\", \"Industry\", \"Website\" FROM \"Users\" WHERE \"Id\" = @id";
        command.Parameters.Add(new NpgsqlParameter("id", row.UserId));
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var values = new object?[] { row.University, row.Major, row.StudentCode, row.Organization, (short?)row.PartnerType, row.Industry, row.Website };
        for (var index = 0; index < values.Length; index++)
            Assert.Equal(values[index], await reader.IsDBNullAsync(index) ? null : reader.GetValue(index));
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string schema = "role_json_test_" + Guid.NewGuid().ToString("N");
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_ROLE_JSON_TEST_POSTGRES")!;
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(
            new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString,
            options => options.MigrationsHistoryTable("__EFMigrationsHistory", schema)).Options);
        public static async Task<Store> Create()
        {
            var store = new Store();
            if (new NpgsqlConnectionStringBuilder(store.connection).Host is not ("127.0.0.1" or "localhost"))
                throw new InvalidOperationException("Role JSON tests require an isolated local PostgreSQL database.");
            await using var db = store.Open();
#pragma warning disable EF1002 // Identifier is generated solely from a fixed prefix and GUID.
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA {store.schema}");
#pragma warning restore EF1002
            return store;
        }
        public async ValueTask DisposeAsync()
        {
            await using var db = Open();
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS {schema} CASCADE");
#pragma warning restore EF1002
        }
    }
}
