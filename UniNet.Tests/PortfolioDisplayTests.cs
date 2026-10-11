using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PortfolioDisplayTests
{
    [Theory]
    [InlineData("developer-modern", "blue")]
    [InlineData("developer-modern", "amber")]
    [InlineData("developer-showcase", "mint")]
    [InlineData("developer-showcase", "orange")]
    [InlineData("developer-landscape", "blue")]
    [InlineData("developer-landscape", "pink")]
    [InlineData("developer-landscape", "mint")]
    public async Task EditorAppearanceAndContentSurviveFreshDatabaseReads(string template, string theme)
    {
        var (connection, db, account) = await Setup();
        await using var connectionScope = connection; await using var dbScope = db;
        var service = new PortfolioService(db);
        var original = await service.Me(account.Id, default);
        var content = original.Portfolio with { Headline = "Updated", Appearance = new() { Profile = original.Portfolio.Appearance.Profile with { Template = template, Theme = theme } } };
        await service.SaveEditor(account.Id, new(new("Owner", "Owner", null, null, "Bio"), content), default);
        db.ChangeTracker.Clear();
        var saved = await service.Me(account.Id, default);
        Assert.Equal(template, saved.Portfolio.Appearance.Profile.Template);
        Assert.Equal(theme, saved.Portfolio.Appearance.Profile.Theme);
        Assert.Equal("Updated", saved.Portfolio.Headline);
        Assert.Single(saved.Portfolio.Projects); Assert.Single(saved.Portfolio.Activities!);
        Assert.True(saved.Portfolio.IsPublic);
        Assert.Contains("keep", (await db.CareerProfiles.SingleAsync()).AppearanceJson);
        Assert.Equal("private-code", (await db.UserProfiles.SingleAsync()).StudentCode);
    }

    [Theory]
    [InlineData("developer-modern", "mint")]
    [InlineData("developer-showcase", "blue")]
    [InlineData("unknown", "blue")]
    public async Task InvalidEditorAppearanceCannotPartiallyWriteIdentity(string template, string theme)
    {
        var (connection, db, account) = await Setup();
        await using var connectionScope = connection; await using var dbScope = db;
        var service = new PortfolioService(db); var original = await service.Me(account.Id, default);
        var content = original.Portfolio with { Appearance = new() { Profile = new() { Template = template, Theme = theme } } };
        var error = await Assert.ThrowsAsync<AuthException>(() => service.SaveEditor(account.Id, new(new("Must not save", "Owner", null, null, null), content), default));
        Assert.Equal("INVALID_APPEARANCE", error.Code);
        db.ChangeTracker.Clear(); Assert.Equal("Owner", (await service.Me(account.Id, default)).Profile.FullName);
    }

    [Fact]
    public async Task DisplayOnlyUpdatePreservesContentsPrivacyAndOtherAppearanceKeys()
    {
        var (connection, db, account) = await Setup();
        await using var connectionScope = connection; await using var dbScope = db;
        var service = new PortfolioService(db);
        await service.SaveAppearance(account.Id, new() { Profile = new() { Template = "developer-landscape", Theme = "pink" } }, default);
        var projects = account.Profile!.CareerProfile!.ProjectsJson;
        await service.SaveDisplay(account.Id, false, default);
        db.ChangeTracker.Clear(); var off = await service.Me(account.Id, default);
        Assert.False(off.Portfolio.Appearance.Profile.ShowPortfolio); Assert.True(off.Portfolio.IsPublic);
        Assert.Equal(projects, (await db.CareerProfiles.SingleAsync()).ProjectsJson);
        var visitor = await service.Read(account.Profile.Id, null, default);
        Assert.False(visitor.IsOwner); Assert.Empty(visitor.Portfolio.Projects); Assert.Null(visitor.Portfolio.ContactEmail);
        Assert.Null(visitor.Profile.Bio); Assert.Null(visitor.SkillCatalog);
        await service.SaveDisplay(account.Id, true, default);
        db.ChangeTracker.Clear(); var on = await service.Me(account.Id, default);
        Assert.True(on.Portfolio.Appearance.Profile.ShowPortfolio);
        Assert.Equal("developer-landscape", on.Portfolio.Appearance.Profile.Template); Assert.Equal("pink", on.Portfolio.Appearance.Profile.Theme);
        Assert.Single(on.Portfolio.Projects); Assert.Contains("keep", (await db.CareerProfiles.SingleAsync()).AppearanceJson);
        Assert.Single((await service.Read(account.Profile.Id, null, default)).Portfolio.Projects);
        await Assert.ThrowsAsync<AuthException>(() => service.SaveDisplay(Guid.NewGuid(), false, default));
    }

    [Fact]
    public async Task OldDataDefaultsToAutomaticAndVisitorsNeverReceiveHiddenContent()
    {
        var (connection, db, account) = await Setup();
        await using var connectionScope = connection; await using var dbScope = db;
        var service = new PortfolioService(db);
        Assert.Null((await service.Me(account.Id, default)).Portfolio.Appearance.Profile.ShowPortfolio);
        await service.SaveAppearance(account.Id, new() { Profile = new() { HiddenSections = ["other-projects", "activities", "contact", "hero", "stats"] } }, default);
        var visitor = await service.Read(account.Profile!.Id, null, default);
        Assert.Empty(visitor.Portfolio.Projects); Assert.Empty(visitor.Portfolio.Activities!);
        Assert.Null(visitor.Profile.Bio); Assert.Null(visitor.Portfolio.Headline); Assert.Null(visitor.Portfolio.ContactEmail); Assert.Null(visitor.Portfolio.Gpa);
        var serialized = System.Text.Json.JsonSerializer.Serialize(visitor);
        Assert.DoesNotContain("secret", serialized); Assert.DoesNotContain("private-code", serialized);
        Assert.Single((await service.Me(account.Id, default)).Portfolio.Projects);
        account.Profile.CareerProfile!.IsPublic = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(() => service.Read(account.Profile.Id, Guid.NewGuid(), default));
        account.Role = AccountRole.Partner; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(() => service.SaveDisplay(account.Id, true, default));
        await Assert.ThrowsAsync<AuthException>(() => service.Read(account.Profile.Id, null, default));
    }

    [Fact]
    public async Task AppearanceUpdatesRetainDisplayPreferenceAndNormalizeLegacyOrange()
    {
        var (connection, db, account) = await Setup();
        await using var connectionScope = connection; await using var dbScope = db;
        var service = new PortfolioService(db);
        await service.SaveDisplay(account.Id, false, default);
        var saved = await service.SaveAppearance(account.Id, new() { Profile = new() { Theme = "orange" } }, default);
        Assert.Equal("amber", saved.Portfolio.Appearance.Profile.Theme);
        Assert.False(saved.Portfolio.Appearance.Profile.ShowPortfolio);
    }

    [Fact]
    public async Task StaleEditorCannotOverwriteDisplayPreferenceChangedOnAnotherDevice()
    {
        var (connection, db, account) = await Setup();
        await using var connectionScope = connection; await using var dbScope = db;
        var service = new PortfolioService(db);
        await service.SaveDisplay(account.Id, true, default);
        var oldDraft = await service.Me(account.Id, default);
        await service.SaveDisplay(account.Id, false, default);
        await service.SaveEditor(account.Id, new(new("Owner", "Owner", null, null, null), oldDraft.Portfolio with {
            Appearance = oldDraft.Portfolio.Appearance with { Profile = oldDraft.Portfolio.Appearance.Profile with { Theme = "amber" } }
        }), default);
        db.ChangeTracker.Clear(); var saved = await service.Me(account.Id, default);
        Assert.False(saved.Portfolio.Appearance.Profile.ShowPortfolio);
        Assert.Equal("amber", saved.Portfolio.Appearance.Profile.Theme); Assert.Single(saved.Portfolio.Projects);
    }

    private static async Task<(SqliteConnection, UniNetDbContext, Account)> Setup()
    {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var account = new Account { Email = "display-owner@example.org", Role = AccountRole.Student, Profile = new UserProfile {
            FullName = "Owner", Nickname = "Owner", StudentCode = "private-code", Bio = "secret bio",
            CareerProfile = new CareerProfile { Headline = "secret headline", IsPublic = true, ProjectsJson = "[{\"title\":\"secret project\",\"technologies\":[],\"featured\":false}]", ActivitiesJson = "[{\"title\":\"secret activity\"}]", BasicInfoJson = "{\"contactEmail\":\"secret@example.org\",\"gpa\":\"secret GPA\"}", AppearanceJson = "{\"cv\":{\"font\":\"keep\"}}" }
        } };
        db.Accounts.Add(account); await db.SaveChangesAsync(); return (connection, db, account);
    }
}
