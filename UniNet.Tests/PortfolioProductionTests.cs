using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;
namespace UniNet.Tests;
public sealed class PortfolioProductionTests
{
    [Fact] public async Task CatalogFiltersInactiveAndUsesNameOrderWithoutRemovedFields()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var react=new Skill {Name="React",Category=SkillCategory.Frontend};
        var docker=new Skill {Name="Docker",Category=SkillCategory.DevOps,IconUrl="https://images.example.org/docker.png"};
        var inactive=new Skill {Name="Archived",IsActive=false};
        db.Skills.AddRange(react,docker,inactive);await db.SaveChangesAsync();
        var service=new PortfolioService(db);var catalog=await service.Catalog(default);
        Assert.Equal(new[]{docker.Id,react.Id},catalog.Select(x=>x.Id).ToArray());
        using var json=System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(catalog));
        Assert.Equal(new[]{"Category","IconUrl","Id","Name"},json.RootElement[0].EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("DevOps",catalog[0].Category);Assert.Equal(docker.IconUrl,catalog[0].IconUrl);
        await service.Save(a.Id,new(){Skills=[new(){SkillId=react.Id,Level=2}]},default);
        db.ChangeTracker.Clear();Assert.Equal(react.Id,(await service.Me(a.Id,default)).Portfolio.Skills.Single().SkillId);
    }
    [Fact] public async Task EditorSavePersistsIdentityAndManualStatisticsWithoutTouchingPrivateFields()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var service=new PortfolioService(db); var before=await service.Me(a.Id,default);
        await service.SaveEditor(a.Id,new PortfolioEditorRequest(new("New name","New nickname","https://images.example.org/avatar.png",null,"New bio"),before.Portfolio with { YearsOfExperience=5.5,Gpa="8.5/10" }),default);
        db.ChangeTracker.Clear(); var result=await service.Me(a.Id,default);
        Assert.Equal("New name",result.Profile.FullName); Assert.Equal("New nickname",result.Profile.Nickname); Assert.Equal("New bio",result.Profile.Bio);
        Assert.Equal(5.5,result.Portfolio.YearsOfExperience); Assert.Equal("8.5/10",result.Portfolio.Gpa);
        var identity=await db.UserProfiles.SingleAsync(); Assert.Equal("private",identity.StudentCode); Assert.False(identity.IsVerified);
        Assert.False(result.Portfolio.IsPublic); Assert.Contains("keep",(await db.CareerProfiles.SingleAsync()).AppearanceJson);
        Assert.Single(result.Portfolio.Projects);
    }
    [Fact] public async Task InvalidEditorIdentityCannotPartiallyPersistContent()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var service=new PortfolioService(db); var before=await service.Me(a.Id,default);
        await Assert.ThrowsAsync<AuthException>(()=>service.SaveEditor(a.Id,new(new("Name","Nickname","javascript:alert(1)",null,null),before.Portfolio with { Headline="Must not persist" }),default));
        db.ChangeTracker.Clear(); Assert.Equal(before.Portfolio.Headline,(await service.Me(a.Id,default)).Portfolio.Headline);
        await Assert.ThrowsAsync<AuthException>(()=>service.SaveEditor(Guid.NewGuid(),new(new("Name","Nickname",null,null,null),before.Portfolio),default));
    }
    [Theory] [InlineData(-1)] [InlineData(81)] [InlineData(double.NaN)]
    public void InvalidManualExperienceIsRejected(double years) => Assert.Throws<AuthException>(()=>PortfolioService.Validate(new(){YearsOfExperience=years}));
    private static async Task<(SqliteConnection Connection, UniNetDbContext Db, Account Account)> Setup()
    {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var account = new Account { Email="target@example.com", Profile=new UserProfile { FullName="Existing identity", Nickname="Keep", IsVerified=true, StudentCode="private", CareerProfile=new CareerProfile { ProjectsJson="[{\"title\":\"Keep\",\"technologies\":[]} ]", AppearanceJson="{\"cv\":{\"font\":\"keep\"}}" } } };
        db.Accounts.Add(account); await db.SaveChangesAsync(); return (connection,db,account);
    }
    [Fact] public async Task AppearanceOnlyPersistsAndDoesNotChangeContentOrPrivacy()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var service=new PortfolioService(db); var before=a.Profile!.CareerProfile!.ProjectsJson;
        await service.SaveAppearance(a.Id,new ProfileAppearance { Profile=new() { Theme="orange", HiddenSections=["certificates"] } },default);
        db.ChangeTracker.Clear(); var result=await service.Me(a.Id,default);
        Assert.Equal("orange",result.Portfolio.Appearance.Profile.Theme); Assert.Equal(before,(await db.CareerProfiles.SingleAsync()).ProjectsJson);
        Assert.False(result.Portfolio.IsPublic); Assert.Contains("keep",(await db.CareerProfiles.SingleAsync()).AppearanceJson); Assert.Single(await db.CareerProfiles.ToListAsync());
        await Assert.ThrowsAsync<AuthException>(()=>service.SaveAppearance(Guid.NewGuid(),new(),default));
        await Assert.ThrowsAsync<AuthException>(()=>service.Read(a.Profile.Id,null,default));
    }
    [Fact] public async Task ProfileIdentityIncludesFullNameAndNicknameForPublicDisplay()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        a.Profile!.CareerProfile!.IsPublic=true;await db.SaveChangesAsync();
        var response=await new PortfolioService(db).Read(a.Profile.Id,null,default);
        var json=System.Text.Json.JsonSerializer.SerializeToElement(response.Profile,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal("Existing identity",json.GetProperty("fullName").GetString());
        Assert.Equal("Keep",json.GetProperty("nickname").GetString());
        Assert.Equal("Keep",json.GetProperty("displayName").GetString());
    }
    [Fact] public async Task CanonicalSkillsPersistReferencesAndPublicResponseExcludesPrivateIdentity()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var skill=new Skill {Name="React",Category=SkillCategory.Frontend};db.Skills.Add(skill);await db.SaveChangesAsync();
        var service=new PortfolioService(db); var saved=await service.Save(a.Id,new PortfolioContent { IsPublic=true,Skills=[new() {SkillId=skill.Id,Level=1}] },default);
        var json=a.Profile!.CareerProfile!.SkillsJson!;Assert.Contains("skillId",json);Assert.DoesNotContain("name",json);Assert.DoesNotContain("iconUrl",json);
        var visitor=await service.Read(a.Profile.Id,null,default);Assert.False(visitor.IsOwner);Assert.Equal("React",visitor.Portfolio.Skills.Single().Name);
        var serialized=System.Text.Json.JsonSerializer.Serialize(visitor);Assert.DoesNotContain("private",serialized);Assert.DoesNotContain("StudentCode",serialized);
        await Assert.ThrowsAsync<AuthException>(()=>service.Save(a.Id,new PortfolioContent {Skills=[new(){SkillId=Guid.NewGuid()}]},default));
    }
    [Fact] public async Task SeedIsIdempotentPreservesExistingIdentityAndSections()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var seed=new ProfileDevelopmentSeed(db);await seed.Run("target@example.com",default);var first=a.Profile!.CareerProfile!.SkillsJson;
        await seed.Run("target@example.com",default);Assert.Single(await db.CareerProfiles.ToListAsync());Assert.Equal(10,await db.Skills.CountAsync());
        Assert.Equal(first,a.Profile.CareerProfile.SkillsJson);Assert.Equal("Existing identity",a.Profile.FullName);Assert.True(a.Profile.IsVerified);Assert.Contains("Keep",a.Profile.CareerProfile.ProjectsJson);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>seed.Run("missing@example.com",default));Assert.Equal(1,await db.Accounts.CountAsync());
    }
    [Theory][InlineData("unknown","blue")][InlineData("developer-modern","purple")]
    public void InvalidTemplateThemePairsAreRejected(string template,string theme) => Assert.Throws<AuthException>(()=>PortfolioService.ValidateAppearance(new() {Profile=new(){Template=template,Theme=theme}}));
    [Theory][InlineData("blue")][InlineData("pink")][InlineData("mint")]
    public async Task LandscapeAppearanceRoundTripsWithoutChangingContent(string theme)
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var service=new PortfolioService(db);
        var before=a.Profile!.CareerProfile!.ProjectsJson;
        await service.SaveAppearance(a.Id,new(){Profile=new(){Template="developer-landscape",Theme=theme}},default);
        db.ChangeTracker.Clear(); var saved=await service.Me(a.Id,default);
        Assert.Equal("developer-landscape",saved.Portfolio.Appearance.Profile.Template);
        Assert.Equal(theme,saved.Portfolio.Appearance.Profile.Theme);
        Assert.Equal(before,(await db.CareerProfiles.SingleAsync()).ProjectsJson);
    }
    [Theory][InlineData("orange")][InlineData("amber")][InlineData("unknown")]
    public void LandscapeRejectsUnsupportedThemes(string theme) => Assert.Throws<AuthException>(()=>PortfolioService.ValidateAppearance(new(){Profile=new(){Template="developer-landscape",Theme=theme}}));
    [Fact] public async Task SavingContentPreservesAppearanceAndOmittedOptionalSections()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        var service=new PortfolioService(db); await service.SaveAppearance(a.Id,new(){Profile=new(){Theme="orange"}},default);
        a.Profile!.CareerProfile!.ActivitiesJson="[{\"title\":\"Keep activity\"}]";
        a.Profile.CareerProfile.LanguagesJson="[{\"name\":\"Vietnamese\",\"level\":\"Native\"}]";
        await db.SaveChangesAsync();
        var saved=await service.Save(a.Id,new(){Headline="Updated content"},default);
        Assert.Equal("orange",saved.Portfolio.Appearance.Profile.Theme);Assert.Single(saved.Portfolio.Activities!);Assert.Single(saved.Portfolio.Languages!);
    }
    [Fact] public async Task AppearanceCreatesOneCareerProfileWhenMissingAndRejectsInactiveOwner()
    {
        var (connection,db,a)=await Setup(); await using var connectionScope=connection; await using var dbScope=db;
        db.CareerProfiles.Remove(a.Profile!.CareerProfile!); await db.SaveChangesAsync(); a.Profile.CareerProfile=null;
        var service=new PortfolioService(db); await service.SaveAppearance(a.Id,new(),default); await service.SaveAppearance(a.Id,new(){Profile=new(){Theme="orange"}},default);
        Assert.Single(await db.CareerProfiles.ToListAsync());a.Status=AccountStatus.Suspended;await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(()=>service.SaveAppearance(a.Id,new(),default));
    }
}
