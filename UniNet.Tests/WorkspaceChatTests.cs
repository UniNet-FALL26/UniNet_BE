using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using UniNet.API.Hubs;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class WorkspaceChatTests
{
    [Fact]
    public async Task ActiveProjectMemberCanReadAndSendMessagesButOutsiderCannot()
    {
        var (connection, db, chat) = await SetupAsync();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectAsync(db);
            var channel = await chat.CreateChannelAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateWorkspaceChannelRequest("general", "Project updates"),
                default);

            var created = await chat.SendMessageAsync(
                scenario.Project.Id,
                channel.Id,
                scenario.MemberAccount.Id,
                new SendWorkspaceMessageRequest("  Hello team  "),
                default);
            var page = await chat.GetMessagesAsync(
                scenario.Project.Id, channel.Id, scenario.MemberAccount.Id, 1, 20, default);

            Assert.Equal("Hello team", created.Content);
            Assert.Equal(scenario.MemberProfile.Id, created.SenderId);
            Assert.Single(page.Data);
            Assert.Equal(created.Id, page.Data[0].Id);

            var exception = await Assert.ThrowsAsync<ProjectException>(() =>
                chat.GetChannelsAsync(scenario.Project.Id, scenario.OutsiderAccount.Id, default));
            Assert.Equal("PROJECT_ACCESS_DENIED", exception.Code);

            var otherProject = new Project
            {
                CreatorId = scenario.MemberProfile.Id,
                Creator = scenario.MemberProfile,
                Title = "Other workspace",
                ProjectField = "Software",
                Description = "Cross-project channel test",
                MemberTarget = 1,
                RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(30),
                Visibility = ProjectVisibility.Private,
                Status = ProjectStatus.Active,
                RecruitmentStatus = RecruitmentStatus.Closed
            };
            var otherChannel = new WorkspaceChannel
            {
                Project = otherProject,
                CreatedById = scenario.MemberProfile.Id,
                Name = "private",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Projects.Add(otherProject);
            db.WorkspaceChannels.Add(otherChannel);
            await db.SaveChangesAsync();
            var crossProjectException = await Assert.ThrowsAsync<ProjectException>(() =>
                chat.GetChannelAsync(
                    scenario.Project.Id,
                    otherChannel.Id,
                    scenario.LeaderAccount.Id,
                    default));
            Assert.Equal("WORKSPACE_CHANNEL_NOT_FOUND", crossProjectException.Code);
        }
    }

    [Fact]
    public async Task OnlyProjectLeaderCanManageChannelsAndArchivedChannelsCannotReceiveMessages()
    {
        var (connection, db, chat) = await SetupAsync();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectAsync(db);
            var exception = await Assert.ThrowsAsync<ProjectException>(() => chat.CreateChannelAsync(
                scenario.Project.Id,
                scenario.MemberAccount.Id,
                new CreateWorkspaceChannelRequest("general"),
                default));
            Assert.Equal("FORBIDDEN", exception.Code);

            var channel = await chat.CreateChannelAsync(
                scenario.Project.Id,
                scenario.LeaderAccount.Id,
                new CreateWorkspaceChannelRequest("general"),
                default);
            await chat.ArchiveChannelAsync(
                scenario.Project.Id, channel.Id, scenario.LeaderAccount.Id, default);

            var sendException = await Assert.ThrowsAsync<ProjectException>(() => chat.SendMessageAsync(
                scenario.Project.Id,
                channel.Id,
                scenario.MemberAccount.Id,
                new SendWorkspaceMessageRequest("No longer active"),
                default));
            Assert.Equal("WORKSPACE_CHANNEL_NOT_FOUND", sendException.Code);
        }
    }

    [Fact]
    public async Task LiveKitTokenUsesServerDerivedRoomIdentityAndRestrictedPermissions()
    {
        var (connection, db, _) = await SetupAsync();
        await using (connection)
        await using (db)
        {
            var scenario = await CreateProjectAsync(db);
            const string apiKey = "test-livekit-key";
            const string apiSecret = "test-livekit-secret-0123456789abcdef";
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LIVEKIT_URL"] = "wss://livekit.example.test",
                ["LIVEKIT_API_KEY"] = apiKey,
                ["LIVEKIT_API_SECRET"] = apiSecret
            }).Build();
            var tokens = new LiveKitTokenService(new WorkspaceAccessService(db), configuration);

            var response = await tokens.CreateProjectRoomTokenAsync(
                scenario.Project.Id,
                scenario.MemberAccount.Id,
                default);
            var decoded = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(
                response.Token, new TokenValidationParameters
            {
                NameClaimType = JwtRegisteredClaimNames.Sub,
                ValidateIssuer = true,
                ValidIssuer = apiKey,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);
            using var payload = JsonDocument.Parse(
                Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(response.Token.Split('.')[1])));
            var video = payload.RootElement.GetProperty("video");

            Assert.Equal($"uninet-project-{scenario.Project.Id:N}", response.RoomName);
            Assert.Equal(scenario.MemberProfile.Id.ToString("D"), response.Identity);
            Assert.Equal("wss://livekit.example.test", response.Url);
            Assert.Equal(response.Identity, decoded.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
            Assert.Equal(response.RoomName, video.GetProperty("room").GetString());
            Assert.True(video.GetProperty("roomJoin").GetBoolean());
            Assert.True(video.GetProperty("canSubscribe").GetBoolean());
            Assert.True(video.GetProperty("canPublish").GetBoolean());
            Assert.False(video.GetProperty("canPublishData").GetBoolean());
            Assert.DoesNotContain(apiSecret, response.Token);

            var forbidden = await Assert.ThrowsAsync<ProjectException>(() =>
                tokens.CreateProjectRoomTokenAsync(
                    scenario.Project.Id,
                    scenario.OutsiderAccount.Id,
                    default));
            Assert.Equal("PROJECT_ACCESS_DENIED", forbidden.Code);
        }
    }

    [Fact]
    public void RemovingMemberSubscriptionsDoesNotRemoveOtherProjectSubscriptions()
    {
        var tracker = new WorkspaceConnectionTracker();
        var projectId = Guid.NewGuid();
        var userProfileId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var otherProjectChannelId = Guid.NewGuid();
        tracker.Add("connection-1", projectId, userProfileId, channelId);
        tracker.Add("connection-1", Guid.NewGuid(), userProfileId, otherProjectChannelId);

        var removed = Assert.Single(tracker.GetMemberSubscriptions(projectId, userProfileId));

        Assert.Equal(("connection-1", channelId), removed);
        Assert.True(tracker.Remove("connection-1", channelId));
        Assert.Empty(tracker.GetMemberSubscriptions(projectId, userProfileId));
        Assert.True(tracker.Remove("connection-1", otherProjectChannelId));
    }

    private static async Task<(SqliteConnection Connection, UniNetDbContext Db, WorkspaceChatService Chat)> SetupAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseSqlite(connection)
            .Options);
        await db.Database.EnsureCreatedAsync();
        return (connection, db, new WorkspaceChatService(db, new WorkspaceAccessService(db)));
    }

    private static async Task<(Account LeaderAccount, UserProfile LeaderProfile, Account MemberAccount,
        UserProfile MemberProfile, Account OutsiderAccount, Project Project)> CreateProjectAsync(UniNetDbContext db)
    {
        var leaderAccount = CreateAccount("leader");
        var leaderProfile = CreateProfile(leaderAccount, "leader");
        var memberAccount = CreateAccount("member");
        var memberProfile = CreateProfile(memberAccount, "member");
        var outsiderAccount = CreateAccount("outsider");
        _ = CreateProfile(outsiderAccount, "outsider");

        var project = new Project
        {
            CreatorId = leaderProfile.Id,
            Creator = leaderProfile,
            Title = "Workspace",
            ProjectField = "Software",
            Description = "Workspace chat test",
            MemberTarget = 3,
            RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(30),
            Visibility = ProjectVisibility.Public,
            Status = ProjectStatus.Active,
            RecruitmentStatus = RecruitmentStatus.Open
        };
        db.Accounts.AddRange(leaderAccount, memberAccount, outsiderAccount);
        db.Projects.Add(project);
        db.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = project.Id,
            UserId = memberProfile.Id,
            Role = "Member",
            Status = ProjectMemberStatus.Active,
            JoinedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return (leaderAccount, leaderProfile, memberAccount, memberProfile, outsiderAccount, project);
    }

    private static Account CreateAccount(string name) => new()
    {
        Email = $"{name}@example.com",
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
            Nickname = name,
            IsVerified = true
        };
        account.Profile = profile;
        return profile;
    }
}
