using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using UniNet.API.Hubs;
using UniNet.API.Services;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class MessagingPostgresTests
{
    [IsolatedPostgresFact]
    public async Task MessagingMigrationUpAndDownPreserveExistingAccounts()
    {
        var connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        var database = "uninet_chat_migration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {database}", admin).ExecuteNonQueryAsync();
        try {
            await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { Database = database, Pooling = false }.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261119031147_RemoveCareerProfileVisibility");
            var account = new Account { Email = "migration@chat.test", Profile = new() { FullName = "Preserved", Nickname = "Preserved" } }; db.Add(account); await db.SaveChangesAsync();
            await migrator.MigrateAsync(); Assert.True(await db.Accounts.AnyAsync(a => a.Id == account.Id)); Assert.Empty(await db.Conversations.ToListAsync());
            await migrator.MigrateAsync("20261119031147_RemoveCareerProfileVisibility"); Assert.True(await db.Accounts.AnyAsync(a => a.Id == account.Id));
        } finally { await new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin).ExecuteNonQueryAsync(); }
    }
    [IsolatedPostgresFact]
    public async Task ReverseFirstSendsAndRetriesProduceOneRoomWithOrderedMessages()
    {
        await using var store = await Store.Create();
        await using var a = store.Open(); await using var b = store.Open();
        var key = Guid.NewGuid();
        var sent = await Task.WhenAll(new MessagingService(a).SendDirect(store.A.Id, store.B.Profile!.Id, new(key, "Hello"), [], default),
            new MessagingService(b).SendDirect(store.B.Id, store.A.Profile!.Id, new(Guid.NewGuid(), "Hi"), [], default));
        Assert.Equal(sent[0].ConversationId, sent[1].ConversationId);
        await using var read = store.Open(); Assert.Single(await read.Conversations.ToListAsync());
        Assert.Equal(new long[] { 1, 2 }, await read.Messages.OrderBy(m => m.Number).Select(m => m.Number).ToArrayAsync());
        await using var c = store.Open(); await using var d = store.Open();
        var repeated = await Task.WhenAll(new MessagingService(c).Send(store.A.Id, sent[0].ConversationId, new(key, "Hello"), [], default),
            new MessagingService(d).SendDirect(store.A.Id, store.B.Profile.Id, new(key, "Hello"), [], default));
        Assert.All(repeated, message => Assert.Equal(sent[0].Id, message.Id));
        Assert.Equal(2, await read.Messages.CountAsync());
    }

    [IsolatedPostgresFact]
    public async Task AcceptFriendshipAndFirstSendRaceReusesOneRoom()
    {
        await using var store = await Store.Create();
        await using var setup = store.Open();
        var request = await new FriendshipService(setup).SendAsync(store.A.Id, store.B.Id, default);
        await using var accept = store.Open(); await using var send = store.Open();
        await Task.WhenAll(new FriendshipService(accept).DecideAsync(store.B.Id, request.FriendshipId!.Value, "accept", request.FriendshipVersion!.Value, default),
            new MessagingService(send).SendDirect(store.A.Id, store.B.Profile!.Id, new(Guid.NewGuid(), "Race"), [], default));
        await using var read = store.Open(); Assert.Single(await read.Conversations.ToListAsync()); Assert.Single(await read.Messages.ToListAsync());
        Assert.Equal("friends", (await new MessagingService(read).Get(store.B.Id, (await read.Conversations.SingleAsync()).Id, default)).Relationship);
    }

    [IsolatedPostgresFact]
    public async Task ConcurrentProjectGroupCreationAndLinkingEnforceOneRoom()
    {
        await using var store = await Store.Create();
        await using var setup = store.Open();
        var project = new Project { CreatorId = store.A.Profile!.Id, Title = "Test project", Description = "Local fixture", ProjectField = "IT" };
        setup.Projects.Add(project); setup.ProjectMembers.Add(new() { ProjectId = project.Id, UserId = store.B.Profile!.Id, Role = "Developer" }); await setup.SaveChangesAsync();
        await using var a = store.Open(); await using var b = store.Open();
        async Task<int> Create(UniNetDbContext db) { try { await new MessagingService(db).CreateGroup(store.A.Id, new("Project", null, [store.B.Id], project.Id), default); return 200; } catch (ProjectException ex) { return ex.Status; } }
        Assert.Equal(new[] { 200, 409 }, (await Task.WhenAll(Create(a), Create(b))).Order().ToArray());
        await using var read = store.Open(); var room = await read.Conversations.SingleAsync();
        Assert.Equal(room.Id, (await read.Projects.SingleAsync()).ChatRoomId);
        Assert.Equal(2, await read.ConversationMembers.CountAsync());
        await new MessagingService(read).LinkProject(store.A.Id, room.Id, null, default);
        Assert.Null((await read.Projects.SingleAsync()).ChatRoomId);
    }

    [IsolatedPostgresFact]
    public async Task DurableDeliveryRetriesAndRechecksMuteBlockAndDeviceAcknowledgement()
    {
        await using var store = await Store.Create(); var fake = new PushSender();
        var services = new ServiceCollection().AddScoped(_ => store.Open()).AddScoped<MessagingService>()
            .AddSingleton<IChatPushSender>(fake).AddSingleton<IHubContext<MessagingHub>>(new HubContext());
        await using var provider = services.BuildServiceProvider();
        var worker = new ChatDeliveryWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ChatDeliveryWorker>.Instance);
        await using var db = store.Open(); var chat = new MessagingService(db);
        await chat.RegisterDevice(store.B.Id, new("device-123", "test-token-" + Guid.NewGuid(), "Android"), default);
        var message = await chat.SendDirect(store.A.Id, store.B.Profile!.Id, new(Guid.NewGuid(), "Private"), [], default);
        await db.Set<NotificationDelivery>().Where(d => d.Reason == "chat.push").ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        for (var i = 0; i < 6 && await worker.ProcessOne(default); i++) { }
        Assert.Equal(1, fake.Attempts);
        var pending = await db.Set<NotificationDelivery>().AsNoTracking().SingleAsync(d => d.Reason == "chat.push"); Assert.Null(pending.DeliveredAt); Assert.Equal(1, pending.Attempts);
        await chat.Acknowledge(store.B.Id, "device-123", message.Id, default);
        await db.Set<NotificationDelivery>().Where(d => d.Reason == "chat.push").ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await worker.ProcessOne(default); Assert.Equal(1, fake.Attempts);
        await chat.Settings(store.B.Id, message.ConversationId, new(false, DateTimeOffset.UtcNow.AddHours(1)), default);
        await chat.Send(store.A.Id, message.ConversationId, new(Guid.NewGuid(), "Muted"), [], default);
        await db.Set<NotificationDelivery>().Where(d => d.Reason == "chat.push").ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        for (var i = 0; i < 10 && await worker.ProcessOne(default); i++) { }
        Assert.Equal(1, fake.Attempts);
        await chat.Settings(store.B.Id, message.ConversationId, new(false, null), default);
        var last = await chat.Send(store.A.Id, message.ConversationId, new(Guid.NewGuid(), "Before blocking"), [], default);
        await chat.Block(store.B.Id, store.A.Id, true, default);
        await db.Set<NotificationDelivery>().Where(d => d.Reason == "chat.push").ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        for (var i = 0; i < 10 && await worker.ProcessOne(default); i++) { }
        Assert.Equal(1, fake.Attempts); Assert.False((await chat.EventFor(store.B.Id, last.ConversationId, last.Id, "message", default)).Notify);
    }

    [Fact]
    public void FcmPreviewOptOutRemovesMessageContentAndWebHasOnePresenter()
    {
        var payload = new ChatPushPayload("chat_message", Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "Alice", null, "Group", null, "Secret body", "/messages");
        var hidden = JsonSerializer.Serialize(FcmChatPushSender.Payload("token", "Android", false, payload));
        Assert.DoesNotContain("Secret body", hidden); Assert.Contains("uninet-messages", hidden);
        using var android = JsonDocument.Parse(hidden);
        Assert.Equal("ic_uninet_notification", android.RootElement.GetProperty("message").GetProperty("android").GetProperty("notification").GetProperty("icon").GetString());
        using var web = JsonDocument.Parse(JsonSerializer.Serialize(FcmChatPushSender.Payload("token", "Web", true, payload)));
        Assert.False(web.RootElement.GetProperty("message").TryGetProperty("notification", out _));
        Assert.Equal(payload.MessageId.ToString(), web.RootElement.GetProperty("message").GetProperty("data").GetProperty("messageId").GetString());
        var large = payload with { SenderName = new string('ự', 255), ConversationName = new string('ự', 150), MessagePreview = new string('ự', 180), SenderAvatarUrl = "https://example.test/" + new string('x', 2000), ConversationAvatarUrl = "https://example.test/" + new string('x', 2000) };
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(FcmChatPushSender.Payload("token", "Android", true, large), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Length < 4096);
    }

    private sealed class PushSender : IChatPushSender { public int Attempts; public bool IsConfigured => true; public Task<ChatPushResult> Send(string token, string platform, bool preview, ChatPushPayload payload, CancellationToken ct) { Attempts++; return Task.FromResult(new ChatPushResult(false)); } }
    private sealed class Proxy : IClientProxy { public Task SendCoreAsync(string method, object?[] args, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class HubClients : IHubClients {
        public IClientProxy All => new Proxy(); public IClientProxy AllExcept(IReadOnlyList<string> ids) => All; public IClientProxy Client(string id) => All; public IClientProxy Clients(IReadOnlyList<string> ids) => All; public IClientProxy Group(string id) => All; public IClientProxy GroupExcept(string id, IReadOnlyList<string> ids) => All; public IClientProxy Groups(IReadOnlyList<string> ids) => All; public IClientProxy User(string id) => All; public IClientProxy Users(IReadOnlyList<string> ids) => All;
    }
    private sealed class HubContext : IHubContext<MessagingHub> { public IHubClients Clients => new HubClients(); public IGroupManager Groups => throw new NotSupportedException(); }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        private readonly string schema = "chat_test_" + Guid.NewGuid().ToString("N");
        public Account A = new() { Email = "alice@chat.test", Profile = new() { FullName = "Alice", Nickname = "Alice" } }, B = new() { Email = "bob@chat.test", Profile = new() { FullName = "Bob", Nickname = "Bob" } };
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, Pooling = false }.ConnectionString).Options);
        public static async Task<Store> Create() {
            var store = new Store(); await using var admin = new NpgsqlConnection(store.connection); await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE SCHEMA {store.schema}", admin).ExecuteNonQueryAsync();
            await using var db = store.Open(); await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript()); db.Accounts.AddRange(store.A, store.B); await db.SaveChangesAsync(); return store;
        }
        public async ValueTask DisposeAsync() { await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync(); await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync(); }
    }
}
