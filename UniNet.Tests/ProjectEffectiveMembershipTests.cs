using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Common;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectEffectiveMembershipTests
{
    [Fact]
    public async Task EffectiveMembersAlwaysIncludeCreatorOnceAndOnlyIncludeActiveMembers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(
            new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var creator = CreateAccount("creator");
        var activeMember = CreateAccount("active");
        var formerMember = CreateAccount("former");
        var project = new Project
        {
            Creator = creator.Profile!,
            Title = "Membership test",
            ProjectField = "IT",
            Description = "Effective membership",
            MemberTarget = 5,
            MinimumQuantity = 1,
            RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1),
            ExpectedOutput = DateTimeOffset.UtcNow.AddDays(2),
            Status = ProjectStatus.Active,
            RecruitmentStatus = RecruitmentStatus.Open,
            Visibility = ProjectVisibility.Public
        };
        project.Members.Add(new ProjectMember
        {
            User = activeMember.Profile!,
            Role = "Developer",
            Status = ProjectMemberStatus.Active
        });
        project.Members.Add(new ProjectMember
        {
            User = formerMember.Profile!,
            Role = "Developer",
            Status = ProjectMemberStatus.Left
        });
        db.Accounts.AddRange(creator, activeMember, formerMember);
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var effectiveMembers = await ProjectMembership.EffectiveMemberIds(db, project.Id)
            .OrderBy(id => id).ToArrayAsync();

        var expected = new[] { creator.Profile!.Id, activeMember.Profile!.Id }.OrderBy(id => id);
        Assert.Equal(expected, effectiveMembers);
        Assert.False(await db.ProjectMembers.AnyAsync(member =>
            member.ProjectId == project.Id && member.UserId == creator.Profile.Id));

        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = creator.Profile.Id,
            Role = "Creator",
            Status = ProjectMemberStatus.Active
        });
        await db.SaveChangesAsync();

        var effectiveMembersWithCreatorRecord = await ProjectMembership.EffectiveMemberIds(db, project.Id)
            .OrderBy(id => id).ToArrayAsync();
        Assert.Equal(expected, effectiveMembersWithCreatorRecord);
        Assert.Equal(3, await db.ProjectMembers.CountAsync());
    }

    private static Account CreateAccount(string name) => new()
    {
        Email = $"{name}@test.com",
        Profile = new UserProfile { FullName = name, Nickname = name }
    };
}
