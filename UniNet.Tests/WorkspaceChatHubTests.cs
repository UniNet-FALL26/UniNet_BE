using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Controllers;
using UniNet.API.Hubs;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class WorkspaceChatHubTests
{
    private const string Issuer = "workspace-chat-tests";
    private const string SigningKey = "isolated-workspace-chat-http-key-0123456789";
    private static readonly ConditionalWeakTable<ClientWebSocket, MemoryStream> ReceiveBuffers = new();

    [Fact]
    public async Task HubRejectsNonMembersAndPersistsBeforeBroadcastingToAuthorizedMembers()
    {
        await using var api = await TestApi.StartAsync();
        using var leader = api.Client(api.LeaderAccountId);
        using var member = api.Client(api.MemberAccountId);
        using var outsider = api.Client(api.OutsiderAccountId);

        using var unauthorizedQueryRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/projects/{api.ProjectId}/workspace/channels?access_token={api.Token(api.MemberAccountId)}");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Http.SendAsync(unauthorizedQueryRequest)).StatusCode);

        using (var outsiderSocket = await api.ConnectSignalRAsync(outsider))
        {
            await SendInvocationAsync(outsiderSocket, "1", "JoinChannel", api.ChannelId);
            using var rejected = await ReceiveMessageAsync(outsiderSocket);
            Assert.Equal(3, rejected.RootElement.GetProperty("type").GetInt32());
            Assert.Contains("PROJECT_ACCESS_DENIED", rejected.RootElement.GetProperty("error").GetString());
        }

        using (var memberSocket = await api.ConnectSignalRAsync(member))
        {
            await SendInvocationAsync(memberSocket, "1", "JoinChannel", api.ChannelId);
            using var joined = await ReceiveMessageAsync(memberSocket);
            Assert.Equal(3, joined.RootElement.GetProperty("type").GetInt32());

            await SendInvocationAsync(memberSocket, "2", "SendMessage", api.ChannelId, "Persist before broadcast");
            using var broadcast = await ReceiveMessageAsync(memberSocket);
            Assert.Equal("MessageReceived", broadcast.RootElement.GetProperty("target").GetString());
            var messageId = broadcast.RootElement.GetProperty("arguments")[0].GetProperty("id").GetGuid();

            await using (var scope = api.App.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UniNetDbContext>();
                var saved = await db.WorkspaceMessages.SingleAsync(message => message.Id == messageId);
                Assert.Equal("Persist before broadcast", saved.Content);
            }

            using var completed = await ReceiveNonPingMessageAsync(memberSocket);
            Assert.Equal(3, completed.RootElement.GetProperty("type").GetInt32());

            await using (var kickScope = api.App.Services.CreateAsyncScope())
                await kickScope.ServiceProvider.GetRequiredService<IProjectMemberService>()
                    .KickAsync(api.ProjectId, api.MemberProfileId, api.LeaderAccountId);

            using var leaderSocket = await api.ConnectSignalRAsync(leader);
            await SendInvocationAsync(leaderSocket, "1", "JoinChannel", api.ChannelId);
            using var leaderJoined = await ReceiveMessageAsync(leaderSocket);
            Assert.Equal(3, leaderJoined.RootElement.GetProperty("type").GetInt32());
            await SendInvocationAsync(leaderSocket, "2", "SendMessage", api.ChannelId, "After member removal");
            using var leaderBroadcast = await ReceiveMessageAsync(leaderSocket);
            Assert.Equal("MessageReceived", leaderBroadcast.RootElement.GetProperty("target").GetString());
            using var leaderCompleted = await ReceiveNonPingMessageAsync(leaderSocket);
            Assert.Equal(3, leaderCompleted.RootElement.GetProperty("type").GetInt32());

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var pendingRead = memberSocket.ReceiveAsync(new byte[4096], timeout.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingRead);
        }
    }

    [Fact]
    public async Task RestEndpointsPersistMessagesAndReturnProjectScopedLiveKitTokens()
    {
        await using var api = await TestApi.StartAsync();
        using var leader = api.Client(api.LeaderAccountId);
        using var member = api.Client(api.MemberAccountId);
        using var outsider = api.Client(api.OutsiderAccountId);

        var createResponse = await leader.PostAsJsonAsync(
            $"/api/projects/{api.ProjectId}/workspace/channels",
            new CreateWorkspaceChannelRequest("updates", "Project updates"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var channel = await createResponse.Content.ReadFromJsonAsync<WorkspaceChannelResponse>();
        Assert.NotNull(channel);

        var messageResponse = await member.PostAsJsonAsync(
            $"/api/projects/{api.ProjectId}/workspace/channels/{channel.Id}/messages",
            new SendWorkspaceMessageRequest("A persisted REST message"));
        Assert.Equal(HttpStatusCode.OK, messageResponse.StatusCode);
        var message = await messageResponse.Content.ReadFromJsonAsync<WorkspaceMessageResponse>();
        Assert.NotNull(message);
        Assert.Equal("Member", message.SenderName);
        Assert.Equal("A persisted REST message", message.Content);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await outsider.GetAsync($"/api/projects/{api.ProjectId}/workspace/channels")).StatusCode);

        using var tokenResponse = await member.PostAsync(
            $"/api/projects/{api.ProjectId}/workspace/livekit-token", null);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var liveKit = await tokenResponse.Content.ReadFromJsonAsync<LiveKitTokenResponse>();
        Assert.NotNull(liveKit);
        Assert.Equal($"uninet-project-{api.ProjectId:N}", liveKit.RoomName);
        Assert.DoesNotContain(api.LiveKitSecret, liveKit.Token);
    }

    private static async Task SendInvocationAsync(
        ClientWebSocket socket, string invocationId, string target, params object[] arguments)
    {
        var invocation = JsonSerializer.Serialize(new
        {
            type = 1,
            invocationId,
            target,
            arguments
        }) + "\u001e";
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(invocation),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveMessageAsync(ClientWebSocket socket)
    {
        var buffered = ReceiveBuffers.GetValue(socket, _ => new MemoryStream());
        var bytes = new byte[4096];
        while (true)
        {
            var data = buffered.ToArray();
            var separator = Array.IndexOf(data, (byte)0x1e);
            if (separator >= 0)
            {
                var message = Encoding.UTF8.GetString(data, 0, separator);
                buffered.SetLength(0);
                buffered.Write(data, separator + 1, data.Length - separator - 1);
                return JsonDocument.Parse(message);
            }
            var result = await socket.ReceiveAsync(bytes, CancellationToken.None);
            buffered.Write(bytes, 0, result.Count);
        }
    }

    private static async Task<JsonDocument> ReceiveNonPingMessageAsync(ClientWebSocket socket)
    {
        while (true)
        {
            var message = await ReceiveMessageAsync(socket);
            if (message.RootElement.GetProperty("type").GetInt32() != 6)
                return message;
            message.Dispose();
        }
    }

    private sealed class TestApi(
        WebApplication app,
        SqliteConnection connection,
        Guid projectId,
        Guid channelId,
        Guid leaderAccountId,
        Guid memberAccountId,
        Guid memberProfileId,
        Guid outsiderAccountId,
        string liveKitSecret) : IAsyncDisposable
    {
        public WebApplication App => app;
        public HttpClient Http { get; } = new() { BaseAddress = new Uri(app.Urls.Single()) };
        public Guid ProjectId => projectId;
        public Guid ChannelId => channelId;
        public Guid LeaderAccountId => leaderAccountId;
        public Guid MemberAccountId => memberAccountId;
        public Guid MemberProfileId => memberProfileId;
        public Guid OutsiderAccountId => outsiderAccountId;
        public string LiveKitSecret => liveKitSecret;

        public HttpClient Client(Guid accountId)
        {
            var client = new HttpClient { BaseAddress = Http.BaseAddress };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(accountId));
            return client;
        }

        public string Token(Guid accountId)
        {
            var jwt = new JwtSecurityToken(
                Issuer,
                Issuer,
                [
                    new Claim(JwtRegisteredClaimNames.Sub, accountId.ToString()),
                    new Claim(ClaimTypes.Role, "Student")
                ],
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(
                    new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        public async Task<ClientWebSocket> ConnectSignalRAsync(HttpClient client)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/workspace/negotiate?negotiateVersion=1");
            request.Headers.Authorization = client.DefaultRequestHeaders.Authorization;
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var negotiation = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var connectionToken = negotiation.RootElement.GetProperty("connectionToken").GetString();
            var httpUri = new Uri(Http.BaseAddress!, $"/hubs/workspace?id={Uri.EscapeDataString(connectionToken!)}" +
                $"&access_token={Uri.EscapeDataString(client.DefaultRequestHeaders.Authorization!.Parameter!)}");
            var socketUri = new UriBuilder(httpUri) { Scheme = "ws" }.Uri;
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(socketUri, CancellationToken.None);
            await socket.SendAsync(
                Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
            using var handshake = await ReceiveMessageAsync(socket);
            Assert.Equal(JsonValueKind.Object, handshake.RootElement.ValueKind);
            Assert.False(handshake.RootElement.TryGetProperty("error", out _));
            return socket;
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            await connection.DisposeAsync();
        }

        public static async Task<TestApi> StartAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<UniNetDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddScoped<IWorkspaceAccessService, WorkspaceAccessService>();
            builder.Services.AddScoped<IWorkspaceChatService, WorkspaceChatService>();
            builder.Services.AddScoped<IProjectMemberService, ProjectMemberService>();
            builder.Services.AddScoped<ILiveKitTokenService, LiveKitTokenService>();
            builder.Services.AddSingleton<IWorkspaceConnectionTracker, WorkspaceConnectionTracker>();
            builder.Services.AddScoped<IWorkspaceMemberRevoker, UniNet.API.Services.WorkspaceMemberRevoker>();
            builder.Configuration["LIVEKIT_URL"] = "wss://livekit.example.test";
            builder.Configuration["LIVEKIT_API_KEY"] = "test-livekit-key";
            builder.Configuration["LIVEKIT_API_SECRET"] = "test-livekit-secret-0123456789abcdef";
            builder.Services.AddControllers().AddApplicationPart(typeof(WorkspaceChatController).Assembly);
            builder.Services.AddSignalR();
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/hubs/workspace") &&
                            context.Request.Query.TryGetValue("access_token", out var token))
                            context.Token = token;
                        return Task.CompletedTask;
                    }
                };
                options.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidIssuer = Issuer,
                    ValidateAudience = true,
                    ValidAudience = Issuer,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    ValidateLifetime = true,
                    RoleClaimType = ClaimTypes.Role
                };
            });
            builder.Services.AddAuthorization();
            var app = builder.Build();
            app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
            {
                var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
                if (exception is ProjectException projectError)
                {
                    context.Response.StatusCode = projectError.Status;
                    await context.Response.WriteAsJsonAsync(new { code = projectError.Code, message = projectError.Message });
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }));
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            app.MapHub<WorkspaceChatHub>(
                "/hubs/workspace",
                options => options.CloseOnAuthenticationExpiration = true)
                .RequireAuthorization();

            await using (var db = app.Services.CreateAsyncScope())
            {
                var context = db.ServiceProvider.GetRequiredService<UniNetDbContext>();
                await context.Database.EnsureCreatedAsync();
                var leader = CreateAccount("hub-leader");
                var leaderProfile = CreateProfile(leader, "Leader");
                var member = CreateAccount("hub-member");
                var memberProfile = CreateProfile(member, "Member");
                var outsider = CreateAccount("hub-outsider");
                _ = CreateProfile(outsider, "Outsider");
                var project = new Project
                {
                    CreatorId = leaderProfile.Id,
                    Creator = leaderProfile,
                    Title = "Workspace hub",
                    ProjectField = "Software",
                    Description = "Hub integration test",
                    MemberTarget = 2,
                    RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(30),
                    Visibility = ProjectVisibility.Private,
                    Status = ProjectStatus.Active,
                    RecruitmentStatus = RecruitmentStatus.Closed
                };
                context.Accounts.AddRange(leader, member, outsider);
                context.Projects.Add(project);
                context.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = project.Id,
                    UserId = memberProfile.Id,
                    Role = "Member",
                    Status = ProjectMemberStatus.Active,
                    JoinedAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
                var channel = new WorkspaceChannel
                {
                    ProjectId = project.Id,
                    CreatedById = leaderProfile.Id,
                    Name = "general",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                context.WorkspaceChannels.Add(channel);
                await context.SaveChangesAsync();
                await app.StartAsync();
                return new TestApi(
                    app, connection, project.Id, channel.Id, leader.Id, member.Id, memberProfile.Id, outsider.Id,
                    "test-livekit-secret-0123456789abcdef");
            }
        }

        private static Account CreateAccount(string email) => new()
        {
            Email = $"{email}-{Guid.NewGuid():N}@example.test",
            Role = AccountRole.Student,
            Status = AccountStatus.Active
        };

        private static UserProfile CreateProfile(Account account, string name)
        {
            var profile = new UserProfile
            {
                Account = account,
                AccountId = account.Id,
                FullName = name,
                Nickname = name
            };
            account.Profile = profile;
            return profile;
        }
    }
}
