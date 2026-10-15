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
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task AcceptanceCancelsCompetingApplicationsOnlyWhenPositionIsFull(bool viaInvitation, int quantity)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        Account Student(string name) => new() { Email = $"{name}@test.com", Role = AccountRole.Student,
            Profile = new UserProfile { FullName = name, Nickname = name } };
        var owner = Student("Owner");
        var selected = Student("Selected");
        var applicant = Student("Applicant");
        db.Accounts.AddRange(owner, selected, applicant);
        var project = new Project { Creator = owner.Profile!, Title = "Project", ProjectField = "IT",
            Description = "Test", MemberTarget = 5, Status = ProjectStatus.Active,
            Visibility = ProjectVisibility.Public, RecruitmentStatus = RecruitmentStatus.Open,
            RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1),
            RoleRequirements = [new() { Id = Guid.NewGuid(), Role = "Developer", Quantity = quantity, Requirements = "Code" },
                new() { Id = Guid.NewGuid(), Role = "Tester", Quantity = 1, Requirements = "Test" }] };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var joins = new ProjectJoinRequestService(db);
        var invites = new ProjectInvitationService(db);
        var competing = await joins.SendAsync(project.Id, applicant.Id, new(" developer "));
        var competingInvite = await invites.SendAsync(project.Id, owner.Id, new(applicant.Profile!.Id, "Developer"));
        if (viaInvitation)
        {
            var invitation = await invites.SendAsync(project.Id, owner.Id, new(selected.Profile!.Id, "Developer"));
            Assert.Equal(ProjectInvitationStatus.Accepted, (await invites.AcceptAsync(invitation.Id, selected.Id)).Status);
        }
        else
        {
            var request = await joins.SendAsync(project.Id, selected.Id, new("Developer"));
            Assert.Equal(ProjectJoinRequestStatus.Accepted, (await joins.AcceptAsync(request.Id, owner.Id)).Status);
        }
        Assert.Equal(quantity == 1 ? ProjectJoinRequestStatus.Cancelled : ProjectJoinRequestStatus.Pending,
            (await db.ProjectJoinRequests.FindAsync(competing.Id))!.Status);
        Assert.Equal(quantity == 1 ? ProjectInvitationStatus.Cancelled : ProjectInvitationStatus.Sent,
            (await db.ProjectInvitations.FindAsync(competingInvite.Id))!.Status);
        if (quantity == 1)
        {
            // Simulate a legacy pending request left behind before automatic cancellation existed.
            (await db.ProjectJoinRequests.FindAsync(competing.Id))!.Status = ProjectJoinRequestStatus.Pending;
            await db.SaveChangesAsync();
            var replacement = await joins.SendAsync(project.Id, applicant.Id, new("Tester"));
            Assert.Equal(ProjectJoinRequestStatus.Pending, replacement.Status);
            Assert.Equal(ProjectJoinRequestStatus.Cancelled, (await db.ProjectJoinRequests.FindAsync(competing.Id))!.Status);
            var duplicate = await Assert.ThrowsAsync<ProjectJoinRequestException>(() =>
                joins.SendAsync(project.Id, applicant.Id, new("Tester")));
            Assert.Equal("JOIN_REQUEST_EXISTS", duplicate.Code);
            var blocked = await Assert.ThrowsAsync<ProjectInvitationException>(() =>
                invites.SendAsync(project.Id, owner.Id, new(applicant.Profile.Id, "Developer")));
            Assert.Equal("PROJECT_ROLE_FULL", blocked.Code);
        }
    }

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
