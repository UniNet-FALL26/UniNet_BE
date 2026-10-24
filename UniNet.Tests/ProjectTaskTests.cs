using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectTaskTests
{
    private static async Task<(SqliteConnection Connection, UniNetDbContext Db, ProjectTaskService Tasks)> Setup()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection)
            .Options);

        await db.Database.EnsureCreatedAsync();
        var configuration = new ConfigurationBuilder().Build();
        return (connection, db, new ProjectTaskService(db, configuration));
    }

    private static async Task<(Account LeaderAccount, UserProfile LeaderProfile, Account MemberAccount, UserProfile MemberProfile, Project Project)> CreateProjectScenario(UniNetDbContext db)
    {
        var leaderAccount = new Account
        {
            Email = "leader@example.com",
            Role = AccountRole.Student,
            Status = AccountStatus.Active
        };

        var memberAccount = new Account
        {
            Email = "member@example.com",
            Role = AccountRole.Student,
            Status = AccountStatus.Active
        };

        var leaderProfile = new UserProfile
        {
            Account = leaderAccount,
            AccountId = leaderAccount.Id,
            FullName = "Project Leader",
            Nickname = "leader",
            IsVerified = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var memberProfile = new UserProfile
        {
            Account = memberAccount,
            AccountId = memberAccount.Id,
            FullName = "Project Member",
            Nickname = "member",
            IsVerified = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        leaderAccount.Profile = leaderProfile;
        memberAccount.Profile = memberProfile;

        var project = new Project
        {
            Creator = leaderProfile,
            CreatorId = leaderProfile.Id,
            Title = "Alpha Project",
            ProjectField = "Software",
            Description = "This is a test project.",
            MemberTarget = 5,
            RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(30),
            ExpectedOutput = DateTimeOffset.UtcNow.AddDays(60),
            Visibility = ProjectVisibility.Public,
            Status = ProjectStatus.Active,
            RecruitmentStatus = RecruitmentStatus.Open,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Accounts.AddRange(leaderAccount, memberAccount);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = memberProfile.Id,
            Role = "Backend",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return (leaderAccount, leaderProfile, memberAccount, memberProfile, project);
    }

    [Fact]
    public async Task AuthorizedProjectParticipantCanViewTasks()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);

            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "API work",
                    "Build the API contract and endpoints.",
                    DateTimeOffset.UtcNow.AddDays(4),
                    ProjectTaskStatus.ToDo,
                    [scenario.MemberProfile.Id]),
                default);

            var result = await tasks.GetProjectTasksAsync(scenario.Project.Id, scenario.MemberAccount.Id, default);

            Assert.Equal(1, result.TotalTasks);
            Assert.Contains(result.Tasks, x => x.Id == created.Id && x.AssignedUsers.Any(a => a.UserId == scenario.MemberProfile.Id));
        }
    }

    [Fact]
    public async Task NonMemberCannotViewAnotherProjectsTasks()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var stranger = new Account
            {
                Email = "stranger@example.com",
                Role = AccountRole.Student,
                Status = AccountStatus.Active
            };
            var strangerProfile = new UserProfile
            {
                Account = stranger,
                AccountId = stranger.Id,
                FullName = "Stranger",
                Nickname = "stranger",
                IsVerified = true
            };
            stranger.Profile = strangerProfile;
            db.Accounts.Add(stranger);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.GetProjectTasksAsync(scenario.Project.Id, stranger.Id, default));
            Assert.Equal("PROJECT_ACCESS_DENIED", ex.Code);
        }
    }

    [Fact]
    public async Task UnauthorizedUserCannotPerformLeaderOnlyActions()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Leader-only task", "Members cannot edit general task fields.", DateTimeOffset.UtcNow.AddDays(2)),
                default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.MemberAccount.Id,
                new CreateProjectTaskRequest("Blocked task", "This should fail.", DateTimeOffset.UtcNow.AddDays(2)),
                default));

            Assert.Equal("FORBIDDEN", ex.Code);

            var updateEx = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateProjectTaskAsync(
                scenario.Project.Id,
                created.Id,
                scenario.MemberAccount.Id,
                new UpdateProjectTaskRequest(Title: "Member cannot edit this"),
                default));

            Assert.Equal("FORBIDDEN", updateEx.Code);
        }
    }

    [Fact]
    public async Task AssignedActiveMemberCanStartTodoTaskWithoutChangingOtherTaskFields()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Startable task",
                    "Task fields should remain unchanged.",
                    DateTimeOffset.UtcNow.AddDays(3),
                    AssignedUserIds: [scenario.MemberProfile.Id]),
                default);

            var updated = await tasks.UpdateTaskStatusAsync(
                scenario.Project.Id,
                created.Id,
                scenario.MemberAccount.Id,
                ProjectTaskStatus.InProgress,
                default);

            Assert.Equal(ProjectTaskStatus.InProgress, updated.Status);
            Assert.Equal(created.Title, updated.Title);
            Assert.Equal(created.Description, updated.Description);
            Assert.Equal(created.Deadline, updated.Deadline);
            Assert.Equal([scenario.MemberProfile.Id], updated.AssignedUsers.Select(user => user.UserId));

            var persisted = await db.ProjectTasks.SingleAsync(task => task.Id == created.Id);
            Assert.Equal(ProjectTaskStatus.InProgress, persisted.Status);
        }
    }

    [Fact]
    public async Task UnassignedProjectMemberCannotStartTask()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Assigned task",
                    "Only its assignee can start it.",
                    DateTimeOffset.UtcNow.AddDays(3),
                    AssignedUserIds: [scenario.LeaderProfile.Id]),
                default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskStatusAsync(
                scenario.Project.Id,
                created.Id,
                scenario.MemberAccount.Id,
                ProjectTaskStatus.InProgress,
                default));

            Assert.Equal("TASK_STATUS_UPDATE_DENIED", ex.Code);
            Assert.Equal(ProjectTaskStatus.ToDo,
                (await db.ProjectTasks.SingleAsync(task => task.Id == created.Id)).Status);
        }
    }

    [Theory]
    [InlineData(ProjectMemberStatus.Left)]
    [InlineData(ProjectMemberStatus.Kicked)]
    public async Task InactiveAssignedMemberCannotStartTask(ProjectMemberStatus membershipStatus)
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Task for former member",
                    "Inactive members cannot start tasks.",
                    DateTimeOffset.UtcNow.AddDays(3),
                    AssignedUserIds: [scenario.MemberProfile.Id]),
                default);
            var membership = await db.ProjectMembers.SingleAsync(member =>
                member.ProjectId == scenario.Project.Id && member.UserId == scenario.MemberProfile.Id);
            membership.Status = membershipStatus;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskStatusAsync(
                scenario.Project.Id,
                created.Id,
                scenario.MemberAccount.Id,
                ProjectTaskStatus.InProgress,
                default));

            Assert.Equal("TASK_STATUS_UPDATE_DENIED", ex.Code);
            Assert.Equal(ProjectTaskStatus.ToDo,
                (await db.ProjectTasks.SingleAsync(task => task.Id == created.Id)).Status);
        }
    }

    [Theory]
    [InlineData(ProjectTaskStatus.InProgress)]
    [InlineData(ProjectTaskStatus.Completed)]
    [InlineData(ProjectTaskStatus.Cancelled)]
    public async Task MemberStatusEndpointOnlyAllowsStartingTodoTask(ProjectTaskStatus currentStatus)
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Not startable task",
                    "This task is not currently ToDo.",
                    DateTimeOffset.UtcNow.AddDays(3),
                    currentStatus,
                    [scenario.MemberProfile.Id]),
                default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskStatusAsync(
                scenario.Project.Id,
                created.Id,
                scenario.MemberAccount.Id,
                ProjectTaskStatus.InProgress,
                default));

            Assert.Equal("INVALID_TASK_STATUS_TRANSITION", ex.Code);
            Assert.Equal(currentStatus,
                (await db.ProjectTasks.SingleAsync(task => task.Id == created.Id)).Status);
        }
    }

    [Fact]
    public async Task MemberStatusEndpointRejectsTransitionsOtherThanInProgress()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "ToDo task",
                    "Members cannot complete tasks through the start endpoint.",
                    DateTimeOffset.UtcNow.AddDays(3),
                    AssignedUserIds: [scenario.MemberProfile.Id]),
                default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateTaskStatusAsync(
                scenario.Project.Id,
                created.Id,
                scenario.MemberAccount.Id,
                ProjectTaskStatus.Completed,
                default));

            Assert.Equal("INVALID_TASK_STATUS_TRANSITION", ex.Code);
            Assert.Equal(ProjectTaskStatus.ToDo,
                (await db.ProjectTasks.SingleAsync(task => task.Id == created.Id)).Status);
        }
    }

    [Fact]
    public async Task TaskCreationAndUpdateValidationRejectInvalidInput()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);

            var invalidTitle = await Assert.ThrowsAsync<ProjectException>(() => tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("", "Description", DateTimeOffset.UtcNow.AddDays(2)),
                default));
            Assert.Equal("INVALID_TASK_TITLE", invalidTitle.Code);

            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Test task", "Description", DateTimeOffset.UtcNow.AddDays(5)),
                default);

            var invalidStatus = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateProjectTaskAsync(
                scenario.Project.Id,
                created.Id,
                scenario.LeaderAccount.Id,
                new UpdateProjectTaskRequest(Status: (ProjectTaskStatus)999),
                default));
            Assert.Equal("INVALID_TASK_STATUS", invalidStatus.Code);
        }
    }

    [Fact]
    public async Task ClientCannotSetOverdueStatusOnCreateOrUpdate()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);

            var createEx = await Assert.ThrowsAsync<ProjectException>(() => tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Overdue attempt",
                    "This must be rejected.",
                    DateTimeOffset.UtcNow.AddDays(1),
                    ProjectTaskStatus.Overdue),
                default));
            Assert.Equal("INVALID_TASK_STATUS", createEx.Code);

            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Valid task", "Description", DateTimeOffset.UtcNow.AddDays(2), ProjectTaskStatus.ToDo),
                default);

            var updateEx = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateProjectTaskAsync(
                scenario.Project.Id,
                created.Id,
                scenario.LeaderAccount.Id,
                new UpdateProjectTaskRequest(Status: ProjectTaskStatus.Overdue),
                default));
            Assert.Equal("INVALID_TASK_STATUS", updateEx.Code);
        }
    }

    [Fact]
    public async Task DeadlineOffsetIsNormalizedAndOverdueIsDerivedCorrectly()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var offsetDeadline = DateTimeOffset.Parse("2024-01-01T12:00:00+02:00");

            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Past due task",
                    "This should be overdue.",
                    offsetDeadline,
                    ProjectTaskStatus.ToDo),
                default);

            var result = await tasks.GetProjectTaskAsync(scenario.Project.Id, created.Id, scenario.LeaderAccount.Id, default);
            Assert.Equal(ProjectTaskStatus.Overdue, result.Status);
            Assert.Equal(offsetDeadline.ToUniversalTime(), created.Deadline);
        }
    }

    [Fact]
    public async Task CompletedAndCancelledTasksStayNonOverdue()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);

            var completed = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Completed task",
                    "Already finished.",
                    DateTimeOffset.UtcNow.AddDays(-1),
                    ProjectTaskStatus.Completed),
                default);

            var cancelled = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Cancelled task",
                    "Already cancelled.",
                    DateTimeOffset.UtcNow.AddDays(-2),
                    ProjectTaskStatus.Cancelled),
                default);

            var completedResult = await tasks.GetProjectTaskAsync(scenario.Project.Id, completed.Id, scenario.LeaderAccount.Id, default);
            var cancelledResult = await tasks.GetProjectTaskAsync(scenario.Project.Id, cancelled.Id, scenario.LeaderAccount.Id, default);

            Assert.Equal(ProjectTaskStatus.Completed, completedResult.Status);
            Assert.Equal(ProjectTaskStatus.Cancelled, cancelledResult.Status);

            var completedTransition = await Assert.ThrowsAsync<ProjectException>(() => tasks.UpdateProjectTaskAsync(
                scenario.Project.Id,
                completed.Id,
                scenario.LeaderAccount.Id,
                new UpdateProjectTaskRequest(Status: ProjectTaskStatus.InProgress),
                default));
            Assert.Equal("INVALID_TASK_STATUS_TRANSITION", completedTransition.Code);
        }
    }

    [Fact]
    public async Task AssignmentRejectsUsersWhoAreNotProjectMembers()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            Assert.False(await db.ProjectMembers.AnyAsync(m =>
                m.ProjectId == scenario.Project.Id && m.UserId == scenario.LeaderProfile.Id));

            var listedMembers = await new ProjectMemberService(db)
                .GetProjectMembersAsync(scenario.Project.Id, default);
            Assert.Contains(listedMembers.Members, member =>
                member.UserId == scenario.LeaderProfile.Id &&
                member.Role == "Creator" &&
                member.Status == ProjectMemberStatus.Active);

            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Task with invalid assignee", "Description", DateTimeOffset.UtcNow.AddDays(3)),
                default);

            var stranger = new Account
            {
                Email = "invalidassignee@example.com",
                Role = AccountRole.Student,
                Status = AccountStatus.Active
            };
            var strangerProfile = new UserProfile
            {
                Account = stranger,
                AccountId = stranger.Id,
                FullName = "Invalid Assignee",
                Nickname = "invalid",
                IsVerified = true
            };
            stranger.Profile = strangerProfile;
            db.Accounts.Add(stranger);
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.AssignUsersAsync(
                scenario.Project.Id,
                created.Id,
                scenario.LeaderAccount.Id,
                [strangerProfile.Id],
                default));

            Assert.Equal("INVALID_TASK_ASSIGNEE", ex.Code);
        }
    }

    [Fact]
    public async Task ProjectCreatorCanBeAssignedToTask()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Creator task", "The creator is a project participant.", DateTimeOffset.UtcNow.AddDays(3)),
                default);

            var assigned = await tasks.AssignUsersAsync(
                scenario.Project.Id,
                created.Id,
                scenario.LeaderAccount.Id,
                [scenario.LeaderProfile.Id],
                default);

            var assignment = Assert.Single(assigned.AssignedUsers);
            Assert.Equal(scenario.LeaderProfile.Id, assignment.UserId);
        }
    }

    [Theory]
    [InlineData(ProjectMemberStatus.Kicked)]
    [InlineData(ProjectMemberStatus.Left)]
    public async Task InactiveProjectMemberCannotBeAssignedAndExistingAssignmentsRemain(ProjectMemberStatus status)
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Task with existing assignment",
                    "An invalid replacement must be rejected.",
                    DateTimeOffset.UtcNow.AddDays(3),
                    AssignedUserIds: [scenario.MemberProfile.Id]),
                default);

            var member = await db.ProjectMembers.SingleAsync(m =>
                m.ProjectId == scenario.Project.Id && m.UserId == scenario.MemberProfile.Id);
            member.Status = status;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.AssignUsersAsync(
                scenario.Project.Id,
                created.Id,
                scenario.LeaderAccount.Id,
                [scenario.MemberProfile.Id],
                default));

            Assert.Equal("INVALID_TASK_ASSIGNEE", ex.Code);
            var persisted = await db.ProjectTaskAssignments
                .Where(a => a.ProjectTaskId == created.Id)
                .Select(a => a.UserId)
                .ToListAsync();
            Assert.Equal([scenario.MemberProfile.Id], persisted);
        }
    }

    [Fact]
    public async Task CreatorOfAnotherProjectCannotBeAssigned()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var otherCreatorAccount = new Account
            {
                Email = "othercreator@example.com",
                Role = AccountRole.Student,
                Status = AccountStatus.Active
            };
            var otherCreatorProfile = new UserProfile
            {
                Account = otherCreatorAccount,
                AccountId = otherCreatorAccount.Id,
                FullName = "Other Project Creator",
                Nickname = "othercreator",
                IsVerified = true
            };
            otherCreatorAccount.Profile = otherCreatorProfile;
            db.Accounts.Add(otherCreatorAccount);
            db.Projects.Add(new Project
            {
                Creator = otherCreatorProfile,
                CreatorId = otherCreatorProfile.Id,
                Title = "Another Project",
                ProjectField = "Software",
                Description = "A separate project.",
                MemberTarget = 5,
                RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(30),
                ExpectedOutput = DateTimeOffset.UtcNow.AddDays(60),
                Visibility = ProjectVisibility.Public,
                Status = ProjectStatus.Active,
                RecruitmentStatus = RecruitmentStatus.Open,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Scoped assignment", "Creators are project-scoped.", DateTimeOffset.UtcNow.AddDays(3)),
                default);

            var ex = await Assert.ThrowsAsync<ProjectException>(() => tasks.AssignUsersAsync(
                scenario.Project.Id,
                created.Id,
                scenario.LeaderAccount.Id,
                [otherCreatorProfile.Id],
                default));

            Assert.Equal("INVALID_TASK_ASSIGNEE", ex.Code);
        }
    }

    [Fact]
    public async Task AssignmentAndTaskRetrievalReturnExpectedData()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);

            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest(
                    "Assignment task",
                    "This must include the project member.",
                    DateTimeOffset.UtcNow.AddDays(7),
                    ProjectTaskStatus.InProgress),
                default);

            await tasks.AssignUsersAsync(
                scenario.Project.Id,
                created.Id,
                scenario.LeaderAccount.Id,
                [scenario.MemberProfile.Id, scenario.MemberProfile.Id],
                default);

            var retrieved = await tasks.GetProjectTaskAsync(scenario.Project.Id, created.Id, scenario.MemberAccount.Id, default);

            Assert.Equal("Assignment task", retrieved.Title);
            var assignment = Assert.Single(retrieved.AssignedUsers);
            Assert.Equal(scenario.MemberProfile.Id, assignment.UserId);
        }
    }

    [Fact]
    public async Task DeletionRespectsProjectLeaderAuthorization()
    {
        var (connection, db, tasks) = await Setup();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectScenario(db);
            var created = await tasks.CreateProjectTaskAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateProjectTaskRequest("Delete me", "Not for members.", DateTimeOffset.UtcNow.AddDays(4)),
                default);

            var memberDelete = await Assert.ThrowsAsync<ProjectException>(() => tasks.DeleteProjectTaskAsync(
                scenario.Project.Id,
                created.Id,
                scenario.MemberAccount.Id,
                default));
            Assert.Equal("FORBIDDEN", memberDelete.Code);

            await tasks.DeleteProjectTaskAsync(scenario.Project.Id, created.Id, scenario.LeaderAccount.Id, default);

            Assert.Equal(0, await db.ProjectTasks.CountAsync());
        }
    }
}
