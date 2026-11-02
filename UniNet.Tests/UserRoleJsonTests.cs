using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class UserRoleJsonTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private UniNetDbContext db = null!;
    private readonly Account student = new()
    {
        Email = "student@role-json.test", Role = AccountRole.Student,
        Profile = new() { FullName = "Student Name", Nickname = "student", Bio = "Student biography", UniversityName = "University A", Major = "Software", StudentCode = "SE123", IsVerified = true }
    };
    private readonly Account partner = new()
    {
        Email = "partner@role-json.test", Role = AccountRole.Partner,
        Profile = new() { FullName = "Partner Contact", Nickname = "partner", OrganizationName = "Original Organization", PartnerType = PartnerType.Company, Industry = "Technology", Website = "[\"https://example.test\",\"https://portfolio.test\"]", Bio = "Partner biography", ContactEmail = "contact@example.test", Phone = "0901234567", Address = "Address", IsVerified = true }
    };

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        db.AddRange(student, partner); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }

    [Fact]
    public void PostgreSqlUsersStoreRoleDetailsOnlyInJsonbColumns()
    {
        using var modelDb = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test;Username=postgres").Options);
        var users = modelDb.Model.GetRelationalModel().Tables.Single(table => table.Name == "Users");
        foreach (var name in new[] { "StudentJson", "PartnerJson", "AssessorJson" })
            Assert.Equal("jsonb", users.Columns.Single(column => column.Name == name).StoreType);
        foreach (var name in new[] { "UniversityName", "Major", "StudentCode", "OrganizationName", "PartnerType", "Industry", "Website" })
            Assert.DoesNotContain(users.Columns, column => column.Name == name);
        foreach (var name in new[] { "FullName", "Nickname", "AvatarUrl", "CoverUrl", "Bio", "ContactEmail", "Phone", "Address", "IsVerified", "AccountId" })
            Assert.Contains(users.Columns, column => column.Name == name);
    }

    [Fact]
    public async Task StudentLegacyCrudRoundTripsThroughJsonAndKeepsResponseFields()
    {
        var account = await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == student.Id);
        Assert.Equal("University A", account.Profile!.UniversityName);
        using (var stored = await StoredJson("StudentJson", account.Profile.Id))
        {
            Assert.Equal("University A", stored.RootElement.GetProperty("UniversityName").GetString());
            Assert.Equal("Software", stored.RootElement.GetProperty("Major").GetString());
            Assert.Equal("SE123", stored.RootElement.GetProperty("StudentCode").GetString());
        }
        var service = new ProfileService(db);
        var saved = await service.Student(account.Id, new("Student Updated", null, null, "Updated biography", "University B", "Design", "SE999", "updated"), default);
        Assert.Equal("University B", saved.Profile!.UniversityName);
        Assert.Equal("Design", saved.Profile.Major);
        Assert.Equal("SE999", saved.Profile.StudentCode);
        Assert.False(saved.Profile.IsVerified);
        db.ChangeTracker.Clear();
        account = await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == student.Id);
        Assert.Equal("University B", account.Profile!.UniversityName);
        var wire = JsonSerializer.SerializeToElement(AuthMapping.ToResponse(account), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("University B", wire.GetProperty("profile").GetProperty("universityName").GetString());
        Assert.Equal("updated", wire.GetProperty("profile").GetProperty("nickname").GetString());
        await service.Student(account.Id, new("Student Updated", null, null, null, null, null, null), default);
        db.ChangeTracker.Clear();
        var cleared = await db.UserProfiles.SingleAsync(p => p.Id == student.Profile!.Id);
        Assert.Null(cleared.UniversityName); Assert.Null(cleared.Major); Assert.Null(cleared.StudentCode);
        Assert.Equal(student.Profile!.Id, cleared.Id);
    }

    [Fact]
    public async Task PartnerLegacyCrudPreservesWebsiteListAndCommonContacts()
    {
        var account = await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == partner.Id);
        using (var stored = await StoredJson("PartnerJson", account.Profile!.Id))
        {
            Assert.Equal("Original Organization", stored.RootElement.GetProperty("OrganizationName").GetString());
            Assert.Equal(0, stored.RootElement.GetProperty("PartnerType").GetInt32());
            Assert.Equal(account.Profile.Website, stored.RootElement.GetProperty("Website").GetString());
        }
        var before = AuthMapping.ToResponse(account);
        Assert.Equal(new[] { "https://example.test", "https://portfolio.test" }, before.Profile!.Websites);
        Assert.Equal("contact@example.test", before.Profile.ContactEmail);
        var service = new ProfileService(db);
        var saved = await service.Partner(account.Id, new("Updated Organization", PartnerType.TrainingCenter, null, null,
            "Updated biography", "Education", null, "updated@example.test", "0907654321", "New address",
            ["https://new.example.test", "https://second.example.test"]), default);
        Assert.Equal("Updated Organization", saved.Profile!.OrganizationName);
        Assert.Equal(PartnerType.TrainingCenter, saved.Profile.PartnerType);
        Assert.Equal(2, saved.Profile.Websites.Count);
        Assert.Equal("updated@example.test", saved.Profile.ContactEmail);
        Assert.False(saved.Profile.IsVerified);
        db.ChangeTracker.Clear();
        account = await db.Accounts.Include(a => a.Profile).SingleAsync(a => a.Id == partner.Id);
        Assert.Equal("Education", account.Profile!.Industry);
        Assert.Equal("0907654321", account.Profile.Phone);
        var cleared = await service.Partner(account.Id, new("Updated Organization", PartnerType.TrainingCenter, null, null,
            null, null, null, null, null, null, []), default);
        Assert.Null(cleared.Profile!.Industry); Assert.Null(cleared.Profile.Website);
        Assert.Empty(cleared.Profile.Websites); Assert.Null(cleared.Profile.ContactEmail);
    }

    [Fact]
    public async Task AdminSearchStillFindsOrganizationStoredInsidePartnerJson()
    {
        var admin = new Account { Email = "admin@role-json.test", Role = AccountRole.Admin };
        db.Add(admin); await db.SaveChangesAsync();
        var result = await new AdminUsersService(db).ListAsync(admin.Id, new(Search: "Original Organization"), default);
        Assert.Equal(partner.Id, Assert.Single(result.Items).Id);
    }

    private async Task<JsonDocument> StoredJson(string column, Guid profileId)
    {
        Assert.Contains(column, new[] { "StudentJson", "PartnerJson" });
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{column}\" FROM \"Users\" WHERE \"Id\" = $id";
        command.Parameters.AddWithValue("$id", profileId);
        return JsonDocument.Parse(Assert.IsType<string>(await command.ExecuteScalarAsync()));
    }
}
