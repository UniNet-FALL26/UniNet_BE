using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectRecruitmentTests
{
    [Theory]
    [InlineData(1, RecruitmentStatus.Expired)]
    [InlineData(2, RecruitmentStatus.Closed)]
    [InlineData(4, RecruitmentStatus.Closed)]
    public async Task DeadlineCountsOnlyActiveMembersAndCreatorOnce(int activeMembers, RecruitmentStatus expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection).ReplaceService<IModelCustomizer, RecruitmentModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = "owner@test.com", Profile = new UserProfile { FullName = "Owner", Nickname = "owner" } };
        var now = DateTimeOffset.UtcNow;
        var project = new Project { Creator = owner.Profile!, Title = "Test", Description = "Test", ProjectField = "IT",
            MemberTarget = 5, MinimumQuantity = 3, Status = ProjectStatus.Active,
            RecruitmentStatus = activeMembers == 4 ? RecruitmentStatus.Full : RecruitmentStatus.Open,
            RecruitmentDeadline = now };
        db.Accounts.Add(owner);
        project.Members.Add(new() { User = owner.Profile!, Role = "Leader", Status = ProjectMemberStatus.Active });
        for (var i = 0; i < activeMembers + 2; i++)
        {
            var account = new Account { Email = $"member{i}@test.com", Profile = new UserProfile { FullName = "Member", Nickname = $"member{i}" } };
            db.Accounts.Add(account);
            project.Members.Add(new() { User = account.Profile!, Role = "Developer",
                Status = i < activeMembers ? ProjectMemberStatus.Active : i == activeMembers ? ProjectMemberStatus.Left : ProjectMemberStatus.Kicked });
        }
        project.JoinRequests.Add(new() { User = owner.Profile!, Role = "Tester", Status = ProjectJoinRequestStatus.Pending });
        project.Invitations.Add(new() { Inviter = owner.Profile!, Invitee = owner.Profile!, Role = "Tester", Status = ProjectInvitationStatus.Sent });
        var future = new Project { Creator = owner.Profile!, Title = "Future", Description = "Test", ProjectField = "IT",
            MemberTarget = 5, MinimumQuantity = 3, Status = ProjectStatus.Active,
            RecruitmentDeadline = now.AddDays(1), RecruitmentStatus = RecruitmentStatus.Open };
        db.Projects.AddRange(project, future);
        await db.SaveChangesAsync();
        var tasks = new ProjectRecruitmentTasks(db);
        await tasks.RunAsync(now);
        Assert.Equal(expected, project.RecruitmentStatus);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Equal(RecruitmentStatus.Open, future.RecruitmentStatus);
        Assert.Equal(ProjectJoinRequestStatus.Cancelled, project.JoinRequests.Single().Status);
        Assert.Equal(ProjectInvitationStatus.Cancelled, project.Invitations.Single().Status);
        await tasks.RunAsync(now.AddMinutes(1));
        Assert.Equal(now, project.UpdatedAt);
        Assert.Equal(3, ProjectMapping.ToDetailResponse(project, null).MinimumQuantity);
        Assert.Equal(3, ProjectMapping.ToListItemResponse(project).MinimumQuantity);
        Assert.Equal(3, ProjectMapping.ToDiscoveryResponse(project).MinimumQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public async Task CreateRejectsMinimumOutsideMemberTarget(int minimum)
    {
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite("Data Source=:memory:").Options);
        var service = new ProjectService(db, null!, new ConfigurationBuilder().Build());
        var request = new CreateProjectRequest("Project", "IT", "Description", 5, minimum,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2), ProjectVisibility.Public);
        var error = await Assert.ThrowsAsync<ProjectException>(() => service.CreateProjectAsync(Guid.NewGuid(), request, default));
        Assert.Equal("INVALID_MINIMUM_QUANTITY", error.Code);
    }

    public sealed class RecruitmentModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Project>().Property(p => p.RecruitmentDeadline)
                .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        }
    }
}
