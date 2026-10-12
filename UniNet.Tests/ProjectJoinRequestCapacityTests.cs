using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectJoinRequestCapacityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FirstRequestChecksOccupiedPositionBeforeCreatingRequest(int quantity)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        Account Student(string name) => new() { Email = $"{name}@test.com", Role = AccountRole.Student,
            Profile = new UserProfile { FullName = name, Nickname = name } };
        var owner = Student("Owner");
        var member = Student("Member");
        var applicant = Student("Applicant");
        db.Accounts.AddRange(owner, member, applicant);
        var project = new Project { Creator = owner.Profile!, Title = "Project", ProjectField = "IT",
            Description = "Test", MemberTarget = 4, Status = ProjectStatus.Active,
            Visibility = ProjectVisibility.Public, RecruitmentStatus = RecruitmentStatus.Open,
            RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1),
            RoleRequirements = [new() { Id = Guid.NewGuid(), Role = "Developer", Quantity = quantity,
                Requirements = "Code" }] };
        db.Projects.Add(project);
        db.ProjectMembers.Add(new() { Id = Guid.NewGuid(), Project = project, User = member.Profile!,
            Role = " developer ", Status = ProjectMemberStatus.Active, JoinedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var service = new ProjectJoinRequestService(db);
        Assert.Empty(await db.ProjectJoinRequests.ToListAsync());
        if (quantity == 1)
        {
            var ex = await Assert.ThrowsAsync<ProjectJoinRequestException>(() =>
                service.SendAsync(project.Id, applicant.Id, new("Developer")));
            Assert.Equal("PROJECT_ROLE_FULL", ex.Code);
            Assert.Equal(409, ex.Status);
            Assert.Equal("You cannot join this project because another person is already in it.", ex.Message);
            Assert.Empty(await db.ProjectJoinRequests.ToListAsync());
            Assert.Empty(db.ChangeTracker.Entries<ProjectJoinRequest>());
        }
        else
        {
            await service.SendAsync(project.Id, applicant.Id, new("Developer"));
            Assert.Single(await db.ProjectJoinRequests.ToListAsync());
        }
    }
}
