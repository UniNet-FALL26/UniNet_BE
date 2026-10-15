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

public sealed class ProjectResponseTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    public void AllProjectMappingsCountActiveMembersAndCreatorOnce(int activeMembers, bool creatorInMembers)
    {
        var project = new Project
        {
            MemberTarget = 4,
            CreatorId = Guid.NewGuid(),
            Creator = new UserProfile { FullName = "Owner", Nickname = "owner" },
            RoleRequirements = [new() { Role = "Developer", Quantity = 3, Requirements = "Code" }],
            Members = Enumerable.Range(0, activeMembers).Select(_ => new ProjectMember
            {
                UserId = Guid.NewGuid(),
                Status = ProjectMemberStatus.Active
            }).ToList()
        };
        project.Members.Add(new() { UserId = Guid.NewGuid(), Status = ProjectMemberStatus.Left });
        project.Members.Add(new() { UserId = Guid.NewGuid(), Status = ProjectMemberStatus.Kicked });
        if (creatorInMembers)
            project.Members.Add(new() { UserId = project.CreatorId, Status = ProjectMemberStatus.Active });

        Assert.Equal(activeMembers + 1, ProjectMapping.ToDetailResponse(project, null).CurrentMemberCount);
        Assert.Equal(activeMembers + 1, ProjectMapping.ToListItemResponse(project).CurrentMemberCount);
        Assert.Equal(activeMembers + 1, ProjectMapping.ToDiscoveryResponse(project).CurrentMemberCount);
    }

    [Theory]
    [InlineData(ProjectStatus.Pending)]
    [InlineData(ProjectStatus.Active)]
    [InlineData(ProjectStatus.Completed)]
    [InlineData(ProjectStatus.Rejected)]
    public async Task StatusQueryIncludesCreatedAndActiveMembershipProjects(ProjectStatus status)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection).ReplaceService<IModelCustomizer, SqliteProjectModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        Account Student(string name) => new()
        {
            Email = $"{name}@test.com", Role = AccountRole.Student,
            Profile = new UserProfile { FullName = name, Nickname = name }
        };
        var student = Student("Student");
        var other = Student("Other");
        db.Accounts.AddRange(student, other);
        Project Create(UserProfile creator, ProjectStatus projectStatus) => new()
        {
            Creator = creator, Title = "Project", Description = "Test", ProjectField = "IT",
            MemberTarget = 4, Status = projectStatus
        };
        var created = Create(student.Profile!, status);
        var joined = Create(other.Profile!, status);
        var left = Create(other.Profile!, status);
        var kicked = Create(other.Profile!, status);
        var unrelated = Create(other.Profile!, status);
        var wrongStatus = Create(other.Profile!, status == ProjectStatus.Active
            ? ProjectStatus.Completed : ProjectStatus.Active);
        foreach (var (project, memberStatus) in new[]
        {
            (created, ProjectMemberStatus.Active), (joined, ProjectMemberStatus.Active),
            (left, ProjectMemberStatus.Left), (kicked, ProjectMemberStatus.Kicked),
            (wrongStatus, ProjectMemberStatus.Active)
        })
            project.Members.Add(new() { Id = Guid.NewGuid(), User = student.Profile!,
                Role = "Developer", Status = memberStatus });
        db.Projects.AddRange(created, joined, left, kicked, unrelated, wrongStatus);
        await db.SaveChangesAsync();
        var service = new ProjectService(db, null!, new ConfigurationBuilder().Build());

        var response = await service.GetProjectsByStatusAsync(student.Id, status, 1, 10);
        Assert.Equal(2, response.TotalItems);
        Assert.Equal(new[] { created.Id, joined.Id }.Order(), response.Projects.Select(p => p.Id).Order());
        Assert.Equal(1, response.Projects.Single(p => p.Id == created.Id).CurrentMemberCount);
        Assert.Equal(2, response.Projects.Single(p => p.Id == joined.Id).CurrentMemberCount);
    }

    public sealed class SqliteProjectModelCustomizer(ModelCustomizerDependencies dependencies)
        : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<Project>().Property(p => p.CreatedAt)
                .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        }
    }
}
