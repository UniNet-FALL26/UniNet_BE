using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class FriendshipTests
{
    [Theory]
    [InlineData(FriendshipStatus.Accepted, false, 1)]
    [InlineData(FriendshipStatus.Accepted, true, 1)]
    [InlineData(FriendshipStatus.Pending, false, 0)]
    [InlineData(FriendshipStatus.Rejected, false, 0)]
    [InlineData(FriendshipStatus.Cancelled, false, 0)]
    [InlineData(FriendshipStatus.Removed, false, 0)]
    public async Task MutualFriendsCountUsesAcceptedEdgesInEitherDirection(FriendshipStatus status, bool reverse, int expected)
    {
        await using var f = await Fixture.Create();
        f.Db.Friendships.AddRange(
            new() { RequesterAccountId = reverse ? f.C.Id : f.A.Id, ReceiverAccountId = reverse ? f.A.Id : f.C.Id, Status = FriendshipStatus.Accepted },
            new() { RequesterAccountId = reverse ? f.B.Id : f.C.Id, ReceiverAccountId = reverse ? f.C.Id : f.B.Id, Status = status });
        await f.Db.SaveChangesAsync();
        Assert.Equal(expected, (await f.Service.RelationshipAsync(f.A.Id, f.B.Profile!.Id, default)).MutualFriendsCount);
        Assert.Equal(expected, (await f.Service.ListAsync(f.A.Id, "people", 1, 20, "Bob", null, default)).Items.Single().MutualFriendsCount);
        Assert.Equal(0, (await f.Service.RelationshipAsync(f.A.Id, f.A.Profile!.Id, default)).MutualFriendsCount);
        var sent = await f.Service.SendAsync(f.A.Id, f.B.Id, default);
        Assert.Equal(expected, sent.MutualFriendsCount);
        var accepted = await f.Service.DecideAsync(f.B.Id, sent.FriendshipId!.Value, "accept", sent.FriendshipVersion!.Value, default);
        Assert.Equal(expected, accepted.MutualFriendsCount);
        Assert.Equal(expected, (await f.Service.ListAsync(f.A.Id, "friends", 1, 20, "Bob", null, default)).Items.Single().MutualFriendsCount);
        f.C.Status = AccountStatus.Suspended; await f.Db.SaveChangesAsync();
        Assert.Equal(0, (await f.Service.RelationshipAsync(f.A.Id, f.B.Profile.Id, default)).MutualFriendsCount);
        f.C.Status = AccountStatus.Active; f.C.Role = AccountRole.Partner; await f.Db.SaveChangesAsync();
        Assert.Equal(0, (await f.Service.RelationshipAsync(f.A.Id, f.B.Profile.Id, default)).MutualFriendsCount);
    }

    [Theory]
    [InlineData(AccountRole.Partner)]
    [InlineData(AccountRole.Assessor)]
    [InlineData(AccountRole.Verifier)]
    [InlineData(AccountRole.Moderator)]
    [InlineData(AccountRole.Accountant)]
    [InlineData(AccountRole.Admin)]
    public async Task OnlyStudentsCanActBeFoundOrReceiveFriendRequests(AccountRole role)
    {
        await using var f = await Fixture.Create();
        f.B.Role = role; await f.Db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.SendAsync(f.B.Id, f.A.Id, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.ListAsync(f.B.Id, "people", 1, 20, null, null, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.RelationshipAsync(f.B.Id, f.A.Profile!.Id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.SendAsync(f.A.Id, f.B.Id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.RelationshipAsync(f.A.Id, f.B.Profile!.Id, default))).Status);
        Assert.DoesNotContain((await f.Service.ListAsync(f.A.Id, "people", 1, 20, null, null, default)).Items, item => item.AccountId == f.B.Id);
        Assert.Empty(await f.Db.Friendships.ToListAsync());
    }

    [Fact]
    public async Task ChangedAccountRoleCannotAcceptAnOldPendingRequest()
    {
        await using var f = await Fixture.Create();
        var sent = await f.Service.SendAsync(f.A.Id, f.B.Id, default);
        f.A.Role = AccountRole.Partner; await f.Db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(f.B.Id, sent.FriendshipId!.Value, "accept", 1, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(f.A.Id, sent.FriendshipId!.Value, "cancel", 1, default))).Status);
        Assert.Equal(FriendshipStatus.Pending, (await f.Db.Friendships.SingleAsync()).Status);
        Assert.Single(await f.Db.Set<VerificationNotification>().ToListAsync());
    }

    [Fact]
    public async Task RequestAcceptListsAndNotificationsUseAccountIdsAndNeverCreateProjectMembers()
    {
        await using var f = await Fixture.Create();
        var sent = await f.Service.SendAsync(f.A.Id, f.B.Id, default);
        Assert.Equal("outgoing", sent.Relationship);
        Assert.Equal(f.B.Profile!.Id, sent.ProfileId);
        Assert.Equal("incoming", (await f.Service.RelationshipAsync(f.B.Id, f.A.Profile!.Id, default)).Relationship);
        Assert.Single((await f.Service.ListAsync(f.B.Id, "received", 1, 20, null, null, default)).Items);
        var accepted = await f.Service.DecideAsync(f.B.Id, sent.FriendshipId!.Value, "accept", sent.FriendshipVersion!.Value, default);
        Assert.Equal("friends", accepted.Relationship);
        Assert.Single((await f.Service.ListAsync(f.A.Id, "friends", 1, 20, null, null, default)).Items);
        Assert.Empty((await f.Service.ListAsync(f.A.Id, "sent", 1, 20, null, null, default)).Items);
        var notices = await f.Db.Set<VerificationNotification>().ToListAsync();
        Assert.Contains(notices, n => n.RecipientUserId == f.B.Id && n.Type == "friendship.request.received");
        Assert.Contains(notices, n => n.RecipientUserId == f.A.Id && n.Type == "friendship.accepted");
        Assert.Equal(2, await f.Db.Set<NotificationDelivery>().CountAsync(d => !d.Reason.StartsWith("chat.")));
        Assert.Equal(2, await f.Db.Set<NotificationDelivery>().CountAsync(d => d.Reason == "chat.relationship"));
        Assert.Empty(await f.Db.ProjectMembers.ToListAsync());
        var targets = new NotificationTargets(f.Db, new NotificationService(f.Db));
        Assert.Equal("/profile/friends", await targets.ResolveAsync(f.B.Id, notices.Single(n => n.RecipientUserId == f.B.Id).Id, default));
        await f.Service.DecideAsync(f.A.Id, accepted.FriendshipId!.Value, "remove", accepted.FriendshipVersion!.Value, default);
        Assert.Empty((await f.Service.ListAsync(f.B.Id, "friends", 1, 20, null, null, default)).Items);
    }

    [Fact]
    public async Task SelfMissingInactiveDuplicateReverseAndUnauthorizedOperationsAreRejected()
    {
        await using var f = await Fixture.Create();
        Assert.Equal("CANNOT_FRIEND_SELF", (await Assert.ThrowsAsync<ProjectException>(() => f.Service.SendAsync(f.A.Id, f.A.Id, default))).Code);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.SendAsync(f.A.Id, Guid.NewGuid(), default))).Status);
        var sent = await f.Service.SendAsync(f.A.Id, f.B.Id, default);
        foreach (var pair in new[] { (f.A.Id, f.B.Id), (f.B.Id, f.A.Id) })
            Assert.Equal(409, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.SendAsync(pair.Item1, pair.Item2, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(f.A.Id, sent.FriendshipId!.Value, "accept", 1, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(f.B.Id, sent.FriendshipId!.Value, "cancel", 1, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(f.C.Id, sent.FriendshipId!.Value, "accept", 1, default))).Status);
        Assert.Single(await f.Db.Friendships.ToListAsync());
        Assert.Single(await f.Db.Set<VerificationNotification>().ToListAsync());
        f.C.Status = AccountStatus.Suspended; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.SendAsync(f.A.Id, f.C.Id, default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.ListAsync(f.C.Id, "people", 1, 20, null, null, default));
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("cancel")]
    public async Task ResendReusesPairButOldVersionCannotActOnNewRequest(string action)
    {
        await using var f = await Fixture.Create();
        var sent = await f.Service.SendAsync(f.A.Id, f.B.Id, default);
        await f.Service.DecideAsync(action == "reject" ? f.B.Id : f.A.Id, sent.FriendshipId!.Value, action, 1, default);
        var again = await f.Service.SendAsync(f.A.Id, f.B.Id, default);
        Assert.Equal(sent.FriendshipId, again.FriendshipId);
        Assert.True(again.FriendshipVersion > sent.FriendshipVersion);
        Assert.Equal("FRIENDSHIP_CHANGED", (await Assert.ThrowsAsync<ProjectException>(() => f.Service.DecideAsync(f.B.Id, again.FriendshipId!.Value, "accept", 1, default))).Code);
        Assert.Equal(2, await f.Db.Set<VerificationNotification>().CountAsync());
        await f.Service.DecideAsync(f.B.Id, again.FriendshipId!.Value, "accept", again.FriendshipVersion!.Value, default);
        Assert.Single(await f.Db.Friendships.ToListAsync());
    }

    [Fact]
    public async Task UniqueComputedPairAndSelfConstraintProtectDatabaseEvenOutsideService()
    {
        await using var f = await Fixture.Create();
        f.Db.Friendships.Add(new() { RequesterAccountId = f.A.Id, ReceiverAccountId = f.B.Id });
        await f.Db.SaveChangesAsync();
        f.Db.Friendships.Add(new() { RequesterAccountId = f.B.Id, ReceiverAccountId = f.A.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        f.Db.Friendships.Add(new() { RequesterAccountId = f.A.Id, ReceiverAccountId = f.A.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task SearchAndPaginationAreBoundedAndReturnRelationshipsNotCredentials()
    {
        await using var f = await Fixture.Create();
        await f.Service.SendAsync(f.C.Id, f.A.Id, default);
        var search = await f.Service.ListAsync(f.A.Id, "people", 1, 1, "user", null, default);
        Assert.Equal(2, search.Total); Assert.Single(search.Items);
        Assert.DoesNotContain(search.Items, p => p.AccountId == f.A.Id);
        var received = await f.Service.ListAsync(f.A.Id, "received", 1, 20, null, null, default);
        Assert.Equal("incoming", Assert.Single(received.Items).Relationship);
        Assert.Empty((await f.Service.ListAsync(f.A.Id, "people", 1, 20, "missing", null, default)).Items);
        await Assert.ThrowsAsync<ProjectException>(() => f.Service.ListAsync(f.A.Id, "friends", 0, 100, null, null, default));
    }

    [Fact]
    public async Task ProjectCandidatesExcludeMembersPendingInvitesAndOtherRolesAndRequireOwnership()
    {
        await using var f = await Fixture.Create();
        Account User(string name, AccountRole role = AccountRole.Student) => new() { Email = name + "@candidate.test", Role = role, Profile = new() { FullName = name, Nickname = name } };
        var invited = User("Invited"); var partner = User("Partner", AccountRole.Partner);
        f.Db.Accounts.AddRange(invited, partner);
        var project = new Project { CreatorId = f.A.Profile!.Id, Title = "Project", Description = "Test", ProjectField = "IT", Status = ProjectStatus.Active };
        f.Db.Projects.Add(project);
        f.Db.ProjectMembers.Add(new() { Project = project, UserId = f.C.Profile!.Id, Role = "Developer", Status = ProjectMemberStatus.Active });
        f.Db.ProjectJoinRequests.Add(new() { Project = project, User = invited.Profile!, RequestType = ProjectRequestType.Invite, Status = ProjectJoinRequestStatus.Pending, Role = "Developer", InviterId = f.A.Profile.Id });
        await f.Db.SaveChangesAsync();
        var request = await f.Service.SendAsync(f.A.Id, f.B.Id, default);
        await f.Service.DecideAsync(f.B.Id, request.FriendshipId!.Value, "accept", 1, default);
        var candidates = await f.Service.ListAsync(f.A.Id, "people", 1, 20, null, project.Id, default);
        var candidate = Assert.Single(candidates.Items);
        Assert.Equal(f.B.Id, candidate.AccountId); Assert.Equal(f.B.Profile!.Id, candidate.ProfileId);
        Assert.Equal("friends", candidate.Relationship);
        Assert.Single((await f.Service.ListAsync(f.A.Id, "friends", 1, 20, null, project.Id, default)).Items);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProjectException>(() => f.Service.ListAsync(f.B.Id, "people", 1, 20, null, project.Id, default))).Status);
    }

    private sealed class Fixture(SqliteConnection connection, UniNetDbContext db, Account a, Account b, Account c) : IAsyncDisposable
    {
        public UniNetDbContext Db => db;
        public Account A => a; public Account B => b; public Account C => c;
        public FriendshipService Service { get; } = new(db);
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await db.Database.EnsureCreatedAsync();
            Account User(string name) => new() { Email = name + "@friend.test", Role = AccountRole.Student, Profile = new() { FullName = name + " User", Nickname = name } };
            var a = User("Alice"); var b = User("Bob"); var c = User("Charlie"); db.Accounts.AddRange(a, b, c); await db.SaveChangesAsync();
            return new(connection, db, a, b, c);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
