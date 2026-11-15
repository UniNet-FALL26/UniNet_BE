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

public sealed class ProjectMembershipTests
{
    [Fact]
    public async Task InviteStudentStoresSentInvitationAndNotifiesInvitee()
    {
        await using var f = await Fixture.CreateAsync();
        var invitation = await f.Invite(" Developer ", " Welcome ");
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.ProjectInvitations.SingleAsync();
        Assert.Equal(invitation.Id, stored.Id);
        Assert.Equal(f.Project.Id, stored.ProjectId);
        Assert.Equal(f.Owner.Profile!.Id, stored.InviterId);
        Assert.Equal(f.Student.Profile!.Id, stored.InviteeId);
        Assert.Equal(ProjectInvitationStatus.Sent, stored.Status);
        Assert.Equal("Developer", stored.Role);
        Assert.Equal("Welcome", stored.Message);
        await f.AssertNotice("project.invitation.received", f.Student.Id, invitation.Id);
        Assert.Empty(await f.Db.ProjectMembers.ToListAsync());
    }

    [Fact]
    public async Task RequestToJoinStoresPendingRequestWithTrimmedInput()
    {
        await using var f = await Fixture.CreateAsync();
        var request = await f.Apply(" Developer ", " Please consider ");
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.ProjectJoinRequests.SingleAsync();
        Assert.Equal(request.Id, stored.Id);
        Assert.Equal(f.Project.Id, stored.ProjectId);
        Assert.Equal(f.Student.Profile!.Id, stored.UserId);
        Assert.Equal(ProjectJoinRequestStatus.Pending, stored.Status);
        Assert.Equal("Developer", stored.Role);
        Assert.Equal("Please consider", stored.Message);
        Assert.Empty(await f.Db.ProjectMembers.ToListAsync());
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
    }

    [Theory]
    [InlineData(true, 150)]
    [InlineData(true, 151)]
    [InlineData(true, 0)]
    [InlineData(false, 150)]
    [InlineData(false, 151)]
    [InlineData(false, 0)]
    public async Task SendValidatesTrimmedRoleLength(bool invitation, int length)
    {
        await using var f = await Fixture.CreateAsync();
        var role = " " + new string('x', length) + " ";
        if (length == 150)
        {
            if (invitation) Assert.Equal(role.Trim(), (await f.Invite(role)).Role);
            else Assert.Equal(role.Trim(), (await f.Apply(role)).Role);
            return;
        }

        await AssertError(invitation, "INVALID_ROLE", 400,
            () => invitation ? (Task)f.Invite(role) : f.Apply(role));
        Assert.Empty(await f.Db.ProjectInvitations.ToListAsync());
        Assert.Empty(await f.Db.ProjectJoinRequests.ToListAsync());
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendRejectsUnauthorizedActorAndDuplicateOpenItem(bool invitation)
    {
        await using var f = await Fixture.CreateAsync();
        if (invitation)
        {
            await AssertError(true, "FORBIDDEN", 403, () =>
                f.Invitations.SendAsync(f.Project.Id, f.Other.Id, new(f.Student.Profile!.Id, "Developer")));
            await f.Invite();
            await AssertError(true, "INVITATION_EXISTS", 409, () => f.Invite());
            Assert.Single(await f.Db.ProjectInvitations.ToListAsync());
            Assert.Single(await f.Db.Set<VerificationNotification>().ToListAsync());
        }
        else
        {
            await AssertError(false, "CANNOT_JOIN_OWN_PROJECT", 403, () =>
                f.Requests.SendAsync(f.Project.Id, f.Owner.Id, new("Developer")));
            await f.Apply();
            await AssertError(false, "JOIN_REQUEST_EXISTS", 409, () => f.Apply());
            Assert.Single(await f.Db.ProjectJoinRequests.ToListAsync());
            Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        }
        Assert.Empty(await f.Db.ProjectMembers.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendRejectsExpiredRecruitment(bool invitation)
    {
        await using var f = await Fixture.CreateAsync();
        f.Project.RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(-1);
        await f.Db.SaveChangesAsync();
        await AssertError(invitation, "RECRUITMENT_CLOSED", 409,
            () => invitation ? (Task)f.Invite() : f.Apply());
        Assert.Empty(await f.Db.ProjectInvitations.ToListAsync());
        Assert.Empty(await f.Db.ProjectJoinRequests.ToListAsync());
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
    }

    [Theory]
    [InlineData("accept", ProjectInvitationStatus.Accepted)]
    [InlineData("reject", ProjectInvitationStatus.Declined)]
    [InlineData("cancel", ProjectInvitationStatus.Cancelled)]
    public async Task InvitationDecisionEnforcesActorAndState(
        string decision, ProjectInvitationStatus expected)
    {
        await using var f = await Fixture.CreateAsync();
        var item = await f.Invite();
        var actor = decision == "cancel" ? f.Owner : f.Student;
        Task<ProjectInvitationResponse> Decide(Guid caller) => decision switch
        {
            "accept" => f.Invitations.AcceptAsync(item.Id, caller),
            "reject" => f.Invitations.DeclineAsync(item.Id, caller),
            _ => f.Invitations.CancelAsync(item.Id, caller)
        };

        var wrongActor = decision == "cancel" ? f.Student : f.Owner;
        await AssertError(true, "FORBIDDEN", 403, () => Decide(wrongActor.Id));
        Assert.Equal(ProjectInvitationStatus.Sent, (await f.Db.ProjectInvitations.SingleAsync()).Status);
        Assert.Empty(await f.Db.ProjectMembers.ToListAsync());
        Assert.Equal(expected, (await Decide(actor.Id)).Status);
        await AssertError(true, "INVALID_INVITATION_STATE", 409, () => Decide(actor.Id));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(expected, (await f.Db.ProjectInvitations.SingleAsync()).Status);
        await f.AssertMembership(decision == "accept");
        await f.AssertNotice("project.invitation.updated",
            decision == "cancel" ? f.Student.Id : f.Owner.Id, item.Id);
    }

    [Theory]
    [InlineData("accept", ProjectJoinRequestStatus.Accepted)]
    [InlineData("reject", ProjectJoinRequestStatus.Rejected)]
    [InlineData("cancel", ProjectJoinRequestStatus.Cancelled)]
    public async Task JoinRequestDecisionEnforcesActorAndState(string decision, ProjectJoinRequestStatus expected)
    {
        await using var f = await Fixture.CreateAsync();
        var item = await f.Apply();
        var actor = decision == "cancel" ? f.Student : f.Owner;
        Task<ProjectJoinRequestResponse> Decide(Guid caller) => decision switch
        {
            "accept" => f.Requests.AcceptAsync(item.Id, caller),
            "reject" => f.Requests.RejectAsync(item.Id, caller),
            _ => f.Requests.CancelAsync(item.Id, caller)
        };

        var wrongActor = decision == "cancel" ? f.Owner : f.Student;
        await AssertError(false, "FORBIDDEN", 403, () => Decide(wrongActor.Id));
        Assert.Equal(ProjectJoinRequestStatus.Pending, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        Assert.Empty(await f.Db.ProjectMembers.ToListAsync());
        Assert.Equal(expected, (await Decide(actor.Id)).Status);
        await AssertError(false, "INVALID_JOIN_REQUEST_STATE", 409, () => Decide(actor.Id));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(expected, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        await f.AssertMembership(decision == "accept");
        if (decision == "cancel") Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        else await f.AssertNotice("project.join-request.reviewed", f.Student.Id, f.Project.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AcceptReactivatesFormerMemberAndCancelsTheirOtherApplication(bool invitation)
    {
        await using var f = await Fixture.CreateAsync();
        f.Project.MemberTarget = 2;
        var member = new ProjectMember { ProjectId = f.Project.Id, UserId = f.Student.Profile!.Id,
            Role = "Tester", Status = ProjectMemberStatus.Left, LeftAt = DateTimeOffset.UtcNow.AddDays(-1) };
        f.Db.ProjectMembers.Add(member);
        await f.Db.SaveChangesAsync();
        var invite = await f.Invite();
        var request = await f.Apply();

        if (invitation)
        {
            await f.Invitations.AcceptAsync(invite.Id, f.Student.Id);
            Assert.Equal(ProjectJoinRequestStatus.Cancelled, (await f.Db.ProjectJoinRequests.SingleAsync()).Status);
        }
        else
        {
            await f.Requests.AcceptAsync(request.Id, f.Owner.Id);
            Assert.Equal(ProjectInvitationStatus.Cancelled, (await f.Db.ProjectInvitations.SingleAsync()).Status);
        }

        f.Db.ChangeTracker.Clear();
        await f.AssertMembership(true);
        var stored = await f.Db.ProjectMembers.SingleAsync();
        Assert.Equal(member.Id, stored.Id);
        Assert.Null(stored.LeftAt);
        Assert.Equal(RecruitmentStatus.Full, (await f.Db.Projects.SingleAsync()).RecruitmentStatus);
    }

    private static async Task AssertError(bool invitation, string code, int status, Func<Task> operation)
    {
        if (invitation)
        {
            var error = await Assert.ThrowsAsync<ProjectInvitationException>(operation);
            Assert.Equal(code, error.Code);
            Assert.Equal(status, error.Status);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ProjectJoinRequestException>(operation);
            Assert.Equal(code, error.Code);
            Assert.Equal(status, error.Status);
        }
    }

    private sealed class Fixture(SqliteConnection connection, UniNetDbContext db,
        Project project, Account owner, Account student, Account other) : IAsyncDisposable
    {
        public UniNetDbContext Db { get; } = db;
        public Project Project { get; } = project;
        public Account Owner { get; } = owner;
        public Account Student { get; } = student;
        public Account Other { get; } = other;
        public ProjectInvitationService Invitations { get; } = new(db);
        public ProjectJoinRequestService Requests { get; } = new(db);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            Account Student(string name) => new()
            {
                Email = $"{name}@test.com", Role = AccountRole.Student,
                Profile = new UserProfile { FullName = name, Nickname = name }
            };
            var owner = Student("Owner");
            var student = Student("Applicant");
            var other = Student("Other");
            var project = new Project
            {
                Creator = owner.Profile!, Title = "Project", ProjectField = "IT", Description = "Test",
                MemberTarget = 5, Status = ProjectStatus.Active, Visibility = ProjectVisibility.Public,
                RecruitmentStatus = RecruitmentStatus.Open, RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1)
            };
            db.Accounts.AddRange(owner, student, other);
            db.Projects.Add(project);
            await db.SaveChangesAsync();
            return new(connection, db, project, owner, student, other);
        }

        public Task<ProjectInvitationResponse> Invite(string role = "Developer", string? message = null) =>
            Invitations.SendAsync(Project.Id, Owner.Id, new(Student.Profile!.Id, role, message));

        public Task<ProjectJoinRequestResponse> Apply(string role = "Developer", string? message = null) =>
            Requests.SendAsync(Project.Id, Student.Id, new(role, message));

        public async Task AssertNotice(string type, Guid recipient, Guid target)
        {
            var notice = await Db.Set<VerificationNotification>().SingleAsync(n => n.Type == type);
            Assert.Equal(recipient, notice.RecipientUserId);
            Assert.Equal(target, notice.TargetId);
        }

        public async Task AssertMembership(bool accepted)
        {
            var members = await Db.ProjectMembers.ToListAsync();
            if (!accepted)
            {
                Assert.Empty(members);
                return;
            }
            var member = Assert.Single(members);
            Assert.Equal(Project.Id, member.ProjectId);
            Assert.Equal(Student.Profile!.Id, member.UserId);
            Assert.Equal("Developer", member.Role);
            Assert.Equal(ProjectMemberStatus.Active, member.Status);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
