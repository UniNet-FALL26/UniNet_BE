using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Entities.Messaging;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class MessagingTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection;
        public required UniNetDbContext Db;
        public MessagingService Chat => new(Db);
        public Account A = User("Alice"), B = User("Bob"), C = User("Carol"), D = User("Dan");
        private static Account User(string name) => new() { Email = name + "@chat.test", Profile = new() { FullName = name, Nickname = name } };
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await db.Database.EnsureCreatedAsync();
            var f = new Fixture { Connection = connection, Db = db }; db.AddRange(f.A, f.B, f.C, f.D); await db.SaveChangesAsync(); return f;
        }
        public async Task Befriend(Account a, Account b)
        {
            Db.Friendships.Add(new() { RequesterAccountId = a.Id, ReceiverAccountId = b.Id, Status = FriendshipStatus.Accepted }); await Db.SaveChangesAsync();
        }
        public async Task<Project> Project()
        {
            var project = new Project { CreatorId = A.Profile!.Id, Title = "Chat project", Description = "A project", ProjectField = "IT", Status = UniNet.Domain.Enums.ProjectStatus.Active };
            Db.Projects.Add(project); Db.ProjectMembers.AddRange(new ProjectMember { ProjectId = project.Id, UserId = A.Profile.Id, Role = "Owner" }, new ProjectMember { ProjectId = project.Id, UserId = B.Profile!.Id, Role = "Developer" });
            await Db.SaveChangesAsync(); return project;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    [Fact]
    public async Task MessagingPreviewDoesNotCreateRoomsAndFirstSendIsIdempotent()
    {
        await using var f = await Fixture.Create();
        Assert.Null((await f.Chat.Direct(f.A.Id, f.B.Profile!.Id, default)).ConversationId); Assert.Empty(await f.Db.Conversations.ToListAsync());
        var request = new ChatSendRequest(Guid.NewGuid(), "Hello");
        var first = await f.Chat.SendDirect(f.A.Id, f.B.Profile.Id, request, [], default);
        var again = await f.Chat.SendDirect(f.A.Id, f.B.Profile.Id, request, [], default);
        Assert.Equal(first.Id, again.Id); Assert.Single(await f.Db.Conversations.ToListAsync()); Assert.Single(await f.Db.Messages.ToListAsync());
        Assert.Equal(2, await f.Db.ConversationMembers.CountAsync());
        Assert.Equal("strangers", (await f.Chat.Get(f.B.Id, first.ConversationId, default)).Relationship);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.Send(f.A.Id, first.ConversationId, request with { Content = "Changed" }, [], default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.History(f.C.Id, first.ConversationId, null, 50, default));
    }
    [Fact]
    public async Task MessagingFriendAcceptanceReusesRoomAndRemovalKeepsHistory()
    {
        await using var f = await Fixture.Create();
        var message = await f.Chat.SendDirect(f.A.Id, f.B.Profile!.Id, new(Guid.NewGuid(), "Before friendship"), [], default);
        var friends = new FriendshipService(f.Db); var pending = await friends.SendAsync(f.A.Id, f.B.Id, default);
        await friends.DecideAsync(f.B.Id, pending.FriendshipId!.Value, "accept", pending.FriendshipVersion!.Value, default);
        Assert.Single(await f.Db.Conversations.ToListAsync()); Assert.Equal("friends", (await f.Chat.Get(f.A.Id, message.ConversationId, default)).Relationship);
        var accepted = await f.Db.Friendships.SingleAsync();
        await friends.DecideAsync(f.A.Id, accepted.Id, "remove", accepted.Version, default);
        Assert.Equal("strangers", (await f.Chat.Get(f.A.Id, message.ConversationId, default)).Relationship);
        Assert.Single((await f.Chat.History(f.B.Id, message.ConversationId, null, 50, default)).Items);
    }
    [Fact]
    public async Task MessagingReadsAreMonotonicAndReactionRecallNeverExposeOldContent()
    {
        await using var f = await Fixture.Create();
        var one = await f.Chat.SendDirect(f.A.Id, f.B.Profile!.Id, new(Guid.NewGuid(), "Secret"), [], default);
        var two = await f.Chat.Send(f.A.Id, one.ConversationId, new(Guid.NewGuid(), "Second", one.Id), [], default);
        Assert.Equal(2, await f.Chat.Unread(f.B.Id, default));
        await f.Chat.Read(f.B.Id, one.ConversationId, new(two.Id), default); await f.Chat.Read(f.B.Id, one.ConversationId, new(one.Id), default);
        Assert.Equal(0, await f.Chat.Unread(f.B.Id, default));
        await f.Chat.React(f.B.Id, one.Id, "❤️", default); await f.Chat.React(f.B.Id, one.Id, "👍", default);
        Assert.Equal("👍", (await f.Db.MessageReactions.SingleAsync()).ReactionType);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.Edit(f.B.Id, one.Id, "Not mine", default));
        await f.Chat.Edit(f.A.Id, one.Id, "Edited secret", default); await f.Chat.Recall(f.A.Id, one.Id, default);
        var history = await f.Chat.History(f.B.Id, one.ConversationId, null, 50, default);
        Assert.Null(history.Items[0].Content); Assert.Empty(history.Items[0].Reactions); Assert.Null(history.Items[1].ReplyTo!.Content);
        Assert.True(history.Items[1].ReplyTo!.Recalled);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.React(f.B.Id, one.Id, "❤️", default));
    }
    [Fact]
    public async Task MessagingBlockPreventsSendingWithoutDeletingHistory()
    {
        await using var f = await Fixture.Create(); var message = await f.Chat.SendDirect(f.A.Id, f.B.Profile!.Id, new(Guid.NewGuid(), "Hello"), [], default);
        await f.Chat.Block(f.B.Id, f.A.Id, true, default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.Send(f.A.Id, message.ConversationId, new(Guid.NewGuid(), "Blocked"), [], default));
        Assert.Single((await f.Chat.History(f.B.Id, message.ConversationId, null, 50, default)).Items);
        await f.Chat.Block(f.B.Id, f.A.Id, false, default);
        await f.Chat.Send(f.A.Id, message.ConversationId, new(Guid.NewGuid(), "Unblocked"), [], default);
    }
    [Fact]
    public async Task MessagingGroupRolesAndProjectLinksKeepMembershipIndependent()
    {
        await using var f = await Fixture.Create(); await f.Befriend(f.A, f.B); await f.Befriend(f.A, f.C);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.CreateGroup(f.A.Id, new("Too small", null, [f.B.Id]), default));
        var group = await f.Chat.CreateGroup(f.A.Id, new("Team", null, [f.B.Id, f.C.Id]), default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.UpdateGroup(f.B.Id, group.Id, new("Intruder", null), default));
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.RemoveMember(f.A.Id, group.Id, f.A.Id, default));
        var project = await f.Project(); await f.Chat.LinkProject(f.A.Id, group.Id, project.Id, default);
        await f.Chat.RemoveMember(f.A.Id, group.Id, f.C.Id, default);
        await f.Chat.LinkProject(f.A.Id, group.Id, null, default); Assert.Null((await f.Chat.Get(f.A.Id, group.Id, default)).Project);
        var other = await f.Chat.CreateGroup(f.A.Id, new("Project room", null, [f.B.Id], project.Id), default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.LinkProject(f.A.Id, group.Id, project.Id, default));
        Assert.NotEqual(group.Id, other.Id); Assert.Equal(2, (await f.Chat.Get(f.A.Id, group.Id, default)).Members.Count(m => m.LeftAt == null));
        await f.Chat.Role(f.A.Id, group.Id, f.B.Id, 2, default); await f.Chat.RemoveMember(f.A.Id, group.Id, f.A.Id, default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.History(f.A.Id, group.Id, null, 50, default));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApprovedProjectApplicantCanBeManuallyAddedToLinkedChatWithoutFriendship(bool linkExistingGroup)
    {
        await using var f = await Fixture.Create();
        var project = await f.Project();
        ChatConversation group;
        if (linkExistingGroup)
        {
            await f.Befriend(f.A, f.B); await f.Befriend(f.A, f.C);
            group = await f.Chat.CreateGroup(f.A.Id, new("Existing group", null, [f.B.Id, f.C.Id]), default);
            await f.Chat.LinkProject(f.A.Id, group.Id, project.Id, default);
        }
        else group = await f.Chat.CreateGroup(f.A.Id, new("Project group", null, [f.B.Id], project.Id), default);

        var hub = new ProjectHubService(f.Db);
        await hub.RecruitmentAsync(project.Id, f.A.Id, new() { Status = 1,
            Positions = [new() { Id = "developer", Name = "Developer", Quantity = 5 }] }, default);
        var application = await hub.RequestAsync(project.Id, f.D.Id, new() { PositionId = "developer" }, default);
        var pending = await Assert.ThrowsAsync<ProjectException>(() => f.Chat.AddMembers(f.A.Id, group.Id, new([], [f.D.Profile!.Id]), default));
        Assert.Equal("CHAT_MEMBER_FORBIDDEN", pending.Code);

        await hub.DecideAsync(application.Id, f.A.Id, new("accept"), default);
        Assert.Contains((await hub.GetAsync(project.Id, f.A.Id, default)).Members,
            member => member.UserId == f.D.Profile!.Id && member.Status == 0);
        Assert.DoesNotContain((await f.Chat.Get(f.A.Id, group.Id, default)).Members,
            member => member.Person.AccountId == f.D.Id && member.LeftAt == null);
        Assert.False(await f.Db.Friendships.AnyAsync(friendship =>
            friendship.RequesterAccountId == f.D.Id || friendship.ReceiverAccountId == f.D.Id));
        var forbidden = await Assert.ThrowsAsync<ProjectException>(() => f.Chat.AddMembers(f.B.Id, group.Id, new([], [f.D.Profile!.Id]), default));
        Assert.Equal("CHAT_ADMIN_REQUIRED", forbidden.Code);

        await f.Chat.AddMembers(f.A.Id, group.Id, new([], [f.D.Profile!.Id]), default);
        await f.Chat.AddMembers(f.A.Id, group.Id, new([], [f.D.Profile!.Id]), default);
        Assert.Single((await f.Chat.Get(f.D.Id, group.Id, default)).Members,
            member => member.Person.AccountId == f.D.Id && member.LeftAt == null);
    }
    [Fact]
    public async Task MessagingFilesAndRepliesRequireMembershipAndSameConversation()
    {
        await using var f = await Fixture.Create();
        var file = new ChatUploadedFile(Guid.NewGuid(), 0, "photo.png", 100, "image/png", "private/photo.png");
        var message = await f.Chat.SendDirect(f.A.Id, f.B.Profile!.Id, new(Guid.NewGuid(), null), [file], default);
        Assert.Equal(file.Id, (await f.Chat.Attachment(f.B.Id, file.Id, default)).Id);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.Attachment(f.C.Id, file.Id, default));
        var other = await f.Chat.SendDirect(f.A.Id, f.C.Profile!.Id, new(Guid.NewGuid(), "Other"), [], default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.Send(f.A.Id, other.ConversationId, new(Guid.NewGuid(), "Cross-room", message.Id), [], default));
        await f.Chat.Recall(f.A.Id, message.Id, default);
        await Assert.ThrowsAsync<ProjectException>(() => f.Chat.Attachment(f.B.Id, file.Id, default));
        Assert.Empty((await f.Chat.History(f.B.Id, message.ConversationId, null, 50, default)).Items.Single().Attachments);
    }
    [Fact]
    public async Task MessagingHistoryUsesStableCursorAndDoesNotDuplicatePages()
    {
        await using var f = await Fixture.Create(); var one = await f.Chat.SendDirect(f.A.Id, f.B.Profile!.Id, new(Guid.NewGuid(), "1"), [], default);
        for (var i = 2; i <= 5; i++) await f.Chat.Send(f.A.Id, one.ConversationId, new(Guid.NewGuid(), i.ToString()), [], default);
        var last = await f.Chat.History(f.B.Id, one.ConversationId, null, 2, default); Assert.True(last.HasMore); Assert.Equal(new long[] { 4, 5 }, last.Items.Select(m => m.Number));
        var previous = await f.Chat.History(f.B.Id, one.ConversationId, last.NextCursor, 2, default); Assert.Equal(new long[] { 2, 3 }, previous.Items.Select(m => m.Number));
    }
    [Fact]
    public async Task MessagingPushDeviceReassignmentAndAcknowledgementArePerDevice()
    {
        await using var f = await Fixture.Create();
        await f.Chat.RegisterDevice(f.B.Id, new("device-b-web", "valid-fcm-token-1", "Web"), default);
        await f.Chat.RegisterDevice(f.B.Id, new("device-b-native", "valid-fcm-token-2", "Android"), default);
        var message = await f.Chat.SendDirect(f.A.Id, f.B.Profile!.Id, new(Guid.NewGuid(), "Push me"), [], default);
        Assert.Equal(2, await f.Db.Set<NotificationDelivery>().CountAsync(d => d.Reason == "chat.push"));
        await f.Chat.Acknowledge(f.B.Id, "device-b-web", message.Id, default);
        Assert.Single(await f.Db.Set<NotificationDelivery>().Where(d => d.Reason == "chat.push" && d.DeliveredAt == null).ToListAsync());
        await f.Chat.RegisterDevice(f.C.Id, new("device-c-web", "valid-fcm-token-1", "Web"), default);
        Assert.DoesNotContain(await f.Db.PushDevices.Where(d => d.AccountId == f.B.Id).ToListAsync(), d => d.Token == "valid-fcm-token-1");
        await f.Chat.UnregisterDevice(f.B.Id, "device-b-native", default); Assert.False((await f.Db.PushDevices.SingleAsync(d => d.DeviceId == "device-b-native")).IsActive);
    }
    [Fact]
    public async Task InactiveTargetIsNotAnAuthenticationFailureAndMissingProjectMembersAreRejected()
    {
        await using var f = await Fixture.Create(); await f.Befriend(f.A, f.B); await f.Befriend(f.A, f.C);
        f.C.Status = AccountStatus.Suspended; await f.Db.SaveChangesAsync();
        var failure = await Assert.ThrowsAsync<ProjectException>(() => f.Chat.CreateGroup(f.A.Id, new("Group", null, [f.B.Id, f.C.Id]), default));
        Assert.Equal(404, failure.Status); Assert.True((await f.Chat.Active(f.A.Id, default)).Status == AccountStatus.Active);
        f.Db.ChangeTracker.Clear(); f.C.Status = AccountStatus.Active; f.Db.Accounts.Update(f.C); await f.Db.SaveChangesAsync();
        var group = await f.Chat.CreateGroup(f.A.Id, new("Group", null, [f.B.Id, f.C.Id]), default);
        var invalid = await Assert.ThrowsAsync<ProjectException>(() => f.Chat.AddMembers(f.A.Id, group.Id, new([], [Guid.NewGuid()]), default)); Assert.Equal(400, invalid.Status);
    }
}
