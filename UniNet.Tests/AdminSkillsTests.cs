using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class AdminSkillsTests
{
    [Fact]
    public async Task CatalogUsesRealRowsCountsAndFiltersAndValidatesAdminWrites()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var admin = new Account { Email = "admin@skills.test", Role = AccountRole.Admin };
        var student = new Account { Email = "student@skills.test", Role = AccountRole.Student, Profile = new() { FullName = "Student", Nickname = "student" } };
        var staff = new Account { Email = "staff@skills.test", Role = AccountRole.Moderator, Profile = new() { FullName = "Staff", Nickname = "staff" } };
        db.Accounts.AddRange(admin, student, staff); await db.SaveChangesAsync();
        var service = new AdminSkillsService(db);
        await Assert.ThrowsAsync<AuthException>(() => service.ListAsync(student.Id, new(), default));
        await Assert.ThrowsAsync<AuthException>(() => service.SaveAsync(staff.Id, null, new("React", SkillCategory.Frontend, true), default));
        var react = await service.SaveAsync(admin.Id, null, new(" React ", SkillCategory.Frontend, true), default);
        var other = await service.SaveAsync(admin.Id, null, new("Legacy", null, false), default);
        db.Set<UserSkill>().AddRange(new UserSkill { Id = Guid.NewGuid(), UserId = student.Profile!.Id, SkillId = react.Id }, new UserSkill { Id = Guid.NewGuid(), UserId = staff.Profile!.Id, SkillId = react.Id });
        await db.SaveChangesAsync();
        (await db.Set<Skill>().SingleAsync(s => s.Id == react.Id)).IconUrl = "https://example.test/react.svg";
        await db.SaveChangesAsync();
        var result = await service.ListAsync(admin.Id, new(Search: "REACT", Category: SkillCategory.Frontend, IsActive: true, PageSize: 1), default);
        Assert.Equal(1, result.Total); Assert.Equal(1, Assert.Single(result.Items).StudentCount);
        Assert.Equal("https://example.test/react.svg", Assert.Single(result.Items).IconUrl);
        Assert.Equal(react.Id, (await service.ListAsync(admin.Id, new(Sort: "popular"), default)).Items[0].Id);
        await Assert.ThrowsAsync<AuthException>(() => service.ListAsync(admin.Id, new(Sort: "invalid"), default));
        Assert.Equal(2, (await service.ListAsync(admin.Id, new(), default)).Total);
        Assert.Equal(other.Id, Assert.Single((await service.ListAsync(admin.Id, new(IsActive: false), default)).Items).Id);
        var duplicate = await Assert.ThrowsAsync<AuthException>(() => service.SaveAsync(admin.Id, null, new("react", null, true), default));
        Assert.Equal(409, duplicate.Status);
        await Assert.ThrowsAsync<AuthException>(() => service.SaveAsync(admin.Id, null, new(" ", null, true), default));
        await Assert.ThrowsAsync<AuthException>(() => service.SaveAsync(admin.Id, null, new("Invalid", (SkillCategory)9, true), default));
        await Assert.ThrowsAsync<AuthException>(() => service.ListAsync(admin.Id, new(PageSize: 101), default));
        await Assert.ThrowsAsync<AuthException>(() => service.SaveAsync(admin.Id, Guid.NewGuid(), new("Missing", null, true), default));
        var updated = await service.SaveAsync(admin.Id, react.Id, new("React.js", SkillCategory.Frontend, false), default);
        Assert.False(updated.IsActive); Assert.Equal(1, updated.StudentCount);
        Assert.Equal("https://example.test/react.svg", updated.IconUrl);
        var iconUrl = "https://res.cloudinary.com/demo/image/upload/v1/uninet/skills/icon.png";
        updated = await service.SaveAsync(admin.Id, react.Id, new("React.js", SkillCategory.Frontend, true, iconUrl), default);
        Assert.Equal(iconUrl, updated.IconUrl);
        await Assert.ThrowsAsync<AuthException>(() => service.SaveAsync(admin.Id, react.Id, new("React.js", null, true, "https://example.test/untrusted.png"), default));
        await Assert.ThrowsAsync<AuthException>(() => service.SaveAsync(admin.Id, react.Id, new("React.js", null, true, "javascript:alert(1)"), default));
        Assert.Equal(2, await db.Set<UserSkill>().CountAsync());
    }
}
