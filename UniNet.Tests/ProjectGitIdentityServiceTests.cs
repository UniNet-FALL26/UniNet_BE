using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Application.Common;
using UniNet.Application.DTOs;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using UniNet.API.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectGitIdentityServiceTests
{
    [Fact]
    public async Task CreatorWithoutMembershipCanLinkAndMemberCanLinkMultipleAccountsAcrossProjects()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var member = fixture.AddStudent("member");
        var project = fixture.AddProject(creator.Profile!);
        var otherProject = fixture.AddProject(creator.Profile!);
        fixture.AddMember(project, member.Profile!);
        fixture.AddMember(otherProject, member.Profile!);
        var creatorConnection = fixture.AddConnection(creator, 9001);
        var firstConnection = fixture.AddConnection(member, 1001);
        var secondConnection = fixture.AddConnection(member, 1002);
        await fixture.Db.SaveChangesAsync();

        var creatorIdentity = await fixture.Service.LinkAsync(creator.Id, project.Id,
            new(creatorConnection.Id), default);
        var firstIdentity = await fixture.Service.LinkAsync(member.Id, project.Id,
            new(firstConnection.Id), default);
        var idempotentLink = await fixture.Service.LinkAsync(member.Id, project.Id,
            new(firstConnection.Id), default);
        var secondIdentity = await fixture.Service.LinkAsync(member.Id, project.Id,
            new(secondConnection.Id), default);
        var otherProjectIdentity = await fixture.Service.LinkAsync(member.Id, otherProject.Id,
            new(firstConnection.Id), default);

        Assert.Equal(creator.Profile!.Id, creatorIdentity.UserProfileId);
        Assert.False(await fixture.Db.ProjectMembers.AnyAsync(x =>
            x.ProjectId == project.Id && x.UserId == creator.Profile.Id));
        Assert.Equal(firstIdentity.Id, idempotentLink.Id);
        Assert.Equal(3, (await fixture.Service.GetAsync(creator.Id, project.Id, 1, 50, null, null, default)).TotalCount);
        Assert.Equal(1002, secondIdentity.ExternalAccountId);
        Assert.Equal(project.Id, firstIdentity.ProjectId);
        Assert.Equal(otherProject.Id, otherProjectIdentity.ProjectId);
    }

    [Fact]
    public async Task OwnershipTransitionRequiresUnlinkAndPreservesBothOwnersAndHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var firstOwner = fixture.AddStudent("first-owner");
        var secondOwner = fixture.AddStudent("second-owner");
        var project = fixture.AddProject(creator.Profile!);
        fixture.AddMember(project, firstOwner.Profile!);
        fixture.AddMember(project, secondOwner.Profile!);
        var firstConnection = fixture.AddConnection(firstOwner, 12345);
        var secondConnection = fixture.AddConnection(secondOwner, 12345);
        await fixture.Db.SaveChangesAsync();

        var firstIdentity = await fixture.Service.LinkAsync(firstOwner.Id, project.Id,
            new(firstConnection.Id), default);
        var conflict = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.LinkAsync(secondOwner.Id, project.Id, new(secondConnection.Id), default));
        Assert.Equal("GIT_IDENTITY_OWNERSHIP_CONFLICT", conflict.Code);

        await fixture.Service.UnlinkAsync(firstOwner.Id, project.Id, firstIdentity.Id, default);
        var secondIdentity = await fixture.Service.LinkAsync(secondOwner.Id, project.Id,
            new(secondConnection.Id), default);
        var reactivationConflict = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.ReactivateAsync(firstOwner.Id, project.Id, firstIdentity.Id,
                new(firstConnection.Id), default));
        Assert.Equal("GIT_IDENTITY_OWNERSHIP_CONFLICT", reactivationConflict.Code);

        await fixture.Service.UnlinkAsync(secondOwner.Id, project.Id, secondIdentity.Id, default);
        var reactivated = await fixture.Service.ReactivateAsync(firstOwner.Id, project.Id, firstIdentity.Id,
            new(firstConnection.Id), default);
        var history = await fixture.Service.GetHistoryAsync(creator.Id, project.Id, firstIdentity.Id, 1, 50, default);

        Assert.Equal(ProjectGitMemberIdentityStatus.Active, reactivated.Status);
        Assert.Equal(firstIdentity.LinkedAt, reactivated.LinkedAt);
        Assert.Contains(history.Items, item => item.Action == ProjectGitMemberIdentityHistoryAction.Unlinked);
        Assert.Contains(history.Items, item => item.Action == ProjectGitMemberIdentityHistoryAction.Reactivated);
        Assert.Equal(2, await fixture.Db.ProjectGitMemberIdentities.CountAsync(x =>
            x.ProjectId == project.Id && x.ExternalAccountId == 12345));
    }

    [Fact]
    public async Task OwnershipCanMoveFromAtoBAndBackAfterVerifiedDepartureWithAuditableHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var firstOwner = fixture.AddStudent("first-owner");
        var secondOwner = fixture.AddStudent("second-owner");
        var project = fixture.AddProject(creator.Profile!);
        fixture.AddMember(project, firstOwner.Profile!);
        fixture.AddMember(project, secondOwner.Profile!);
        var firstConnection = fixture.AddConnection(firstOwner, 23456);
        var secondConnection = fixture.AddConnection(secondOwner, 23456);
        await fixture.Db.SaveChangesAsync();

        var firstIdentity = await fixture.Service.LinkAsync(firstOwner.Id, project.Id,
            new(firstConnection.Id), default);
        await fixture.Service.LinkAsync(firstOwner.Id, project.Id, new(firstConnection.Id), default);
        await fixture.Service.UnlinkAsync(firstOwner.Id, project.Id, firstIdentity.Id, default);
        await fixture.Service.UnlinkAsync(firstOwner.Id, project.Id, firstIdentity.Id, default);
        var secondIdentity = await fixture.Service.LinkAsync(secondOwner.Id, project.Id,
            new(secondConnection.Id), default);

        var firstOwnerConflict = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.ReactivateAsync(firstOwner.Id, project.Id, firstIdentity.Id,
                new(firstConnection.Id), default));
        Assert.Equal(409, firstOwnerConflict.Status);

        await new ProjectMemberService(fixture.Db).LeaveAsync(project.Id, secondOwner.Id);
        var returnedIdentity = await fixture.Service.ReactivateAsync(firstOwner.Id, project.Id, firstIdentity.Id,
            new(firstConnection.Id), default);
        await fixture.Service.ReactivateAsync(firstOwner.Id, project.Id, firstIdentity.Id,
            new(firstConnection.Id), default);

        var firstHistory = await fixture.Service.GetHistoryAsync(
            creator.Id, project.Id, firstIdentity.Id, 1, 50, default);
        var secondHistory = await fixture.Service.GetHistoryAsync(
            creator.Id, project.Id, secondIdentity.Id, 1, 50, default);
        var allIdentities = await fixture.Db.ProjectGitMemberIdentities.AsNoTracking()
            .Where(x => x.ProjectId == project.Id && x.ExternalAccountId == 23456)
            .ToListAsync();

        Assert.Equal(ProjectGitMemberIdentityStatus.Active, returnedIdentity.Status);
        Assert.Equal(ProjectGitMemberIdentityStatus.Inactive,
            allIdentities.Single(x => x.Id == secondIdentity.Id).Status);
        Assert.Equal(2, allIdentities.Count);
        Assert.Equal(1, firstHistory.Items.Count(x => x.Action == ProjectGitMemberIdentityHistoryAction.Linked));
        Assert.Equal(1, firstHistory.Items.Count(x => x.Action == ProjectGitMemberIdentityHistoryAction.Unlinked));
        Assert.Equal(1, firstHistory.Items.Count(x => x.Action == ProjectGitMemberIdentityHistoryAction.Reactivated));
        Assert.Contains(secondHistory.Items,
            x => x.Action == ProjectGitMemberIdentityHistoryAction.DeactivatedByDeparture);
    }

    [Fact]
    public async Task LinkRejectsProviderIdentityMismatchAndListRejectsInactiveMemberAndUnknownStatus()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var member = fixture.AddStudent("member");
        var project = fixture.AddProject(creator.Profile!);
        var membership = fixture.AddMember(project, member.Profile!);
        var connection = fixture.AddConnection(member, 34567);
        await fixture.Db.SaveChangesAsync();
        connection.ProviderAccountId = "76543";
        await fixture.Db.SaveChangesAsync();

        var mismatch = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.LinkAsync(member.Id, project.Id, new(connection.Id), default));
        Assert.Equal("GIT_OAUTH_ACCOUNT_MISMATCH", mismatch.Code);
        Assert.Equal(409, mismatch.Status);
        Assert.Empty(await fixture.Db.ProjectGitMemberIdentities.ToListAsync());

        membership.Status = ProjectMemberStatus.Left;
        membership.LeftAt = DateTimeOffset.UtcNow;
        await fixture.Db.SaveChangesAsync();
        var inactiveMember = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.GetAsync(member.Id, project.Id, 1, 50, null, null, default));
        Assert.Equal(403, inactiveMember.Status);

        var invalidStatus = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.GetAsync(creator.Id, project.Id, 1, 50, null,
                (ProjectGitMemberIdentityStatus)99, default));
        Assert.Equal("INVALID_GIT_IDENTITY_STATUS", invalidStatus.Code);
        Assert.Equal(400, invalidStatus.Status);
    }

    [Fact]
    public async Task ProviderRejectingCredentialCannotCreateIdentityAndRequiresReauthorization()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var project = fixture.AddProject(creator.Profile!);
        var connection = fixture.AddConnection(creator, 45679);
        await fixture.Db.SaveChangesAsync();
        fixture.Provider.RejectCredentials = true;

        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.LinkAsync(creator.Id, project.Id, new(connection.Id), default));

        Assert.Equal("GIT_OAUTH_REAUTHORIZATION_REQUIRED", error.Code);
        Assert.Equal(409, error.Status);
        Assert.Empty(await fixture.Db.ProjectGitMemberIdentities.AsNoTracking().ToListAsync());
        var storedConnection = await fixture.Db.GitHubOAuthConnections.AsNoTracking()
            .SingleAsync(x => x.Id == connection.Id);
        Assert.Equal(GitHubOAuthConnectionStatus.ReauthorizationRequired, storedConnection.Status);
        Assert.Equal("", storedConnection.AccessTokenEncrypted);
        Assert.Null(storedConnection.RefreshTokenEncrypted);
    }

    [Fact]
    public async Task MembershipDepartureRollsBackWhenIdentityHistoryCannotBeAppended()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var member = fixture.AddStudent("member");
        var project = fixture.AddProject(creator.Profile!);
        var membership = fixture.AddMember(project, member.Profile!);
        var connection = fixture.AddConnection(member, 45678);
        await fixture.Db.SaveChangesAsync();
        var identity = await fixture.Service.LinkAsync(member.Id, project.Id, new(connection.Id), default);
        var repositoryLink = fixture.AddRepositoryLink(project, member.Profile!, connection);
        await fixture.Db.SaveChangesAsync();
        var workspaceRevoker = new FakeWorkspaceMemberRevoker();

        await fixture.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER RejectIdentityHistory BEFORE INSERT ON ProjectGitMemberIdentityHistory " +
            "BEGIN SELECT RAISE(ABORT, 'history write rejected'); END;");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new ProjectMemberService(fixture.Db, workspaceRevoker).LeaveAsync(project.Id, member.Id));

        Assert.Equal(ProjectMemberStatus.Active, (await fixture.Db.ProjectMembers.AsNoTracking()
            .SingleAsync(x => x.Id == membership.Id)).Status);
        Assert.Equal(ProjectGitMemberIdentityStatus.Active, (await fixture.Db.ProjectGitMemberIdentities
            .AsNoTracking().SingleAsync(x => x.Id == identity.Id)).Status);
        Assert.DoesNotContain(await fixture.Db.ProjectGitMemberIdentityHistory.AsNoTracking().ToListAsync(),
            x => x.Action == ProjectGitMemberIdentityHistoryAction.DeactivatedByDeparture);
        Assert.Equal(GitHubOAuthConnectionStatus.Active, (await fixture.Db.GitHubOAuthConnections
            .AsNoTracking().SingleAsync(x => x.Id == connection.Id)).Status);
        var storedRepositoryLink = await fixture.Db.ProjectGitRepositories.AsNoTracking()
            .SingleAsync(x => x.Id == repositoryLink.Id);
        Assert.Null(storedRepositoryLink.UnlinkedAt);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, storedRepositoryLink.AccessStatus);
        Assert.Empty(workspaceRevoker.RevokedMembers);
    }

    [Fact]
    public async Task LeavingAndKickingDeactivateEveryActiveIdentityWithoutRevokingOAuth()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var leavingMember = fixture.AddStudent("leaving");
        var kickedMember = fixture.AddStudent("kicked");
        var project = fixture.AddProject(creator.Profile!);
        fixture.AddMember(project, leavingMember.Profile!);
        fixture.AddMember(project, kickedMember.Profile!);
        var leavingConnectionOne = fixture.AddConnection(leavingMember, 3001);
        var leavingConnectionTwo = fixture.AddConnection(leavingMember, 3002);
        var kickedConnection = fixture.AddConnection(kickedMember, 4001);
        await fixture.Db.SaveChangesAsync();
        var repositoryLink = fixture.AddRepositoryLink(project, leavingMember.Profile!, leavingConnectionOne);
        await fixture.Db.SaveChangesAsync();
        var leaveIdentityOne = await fixture.Service.LinkAsync(leavingMember.Id, project.Id,
            new(leavingConnectionOne.Id), default);
        var leaveIdentityTwo = await fixture.Service.LinkAsync(leavingMember.Id, project.Id,
            new(leavingConnectionTwo.Id), default);
        var kickIdentity = await fixture.Service.LinkAsync(kickedMember.Id, project.Id,
            new(kickedConnection.Id), default);

        var workspaceRevoker = new FakeWorkspaceMemberRevoker();
        await new ProjectMemberService(fixture.Db, workspaceRevoker).LeaveAsync(project.Id, leavingMember.Id);
        await new ProjectMemberService(fixture.Db, workspaceRevoker)
            .KickAsync(project.Id, kickedMember.Profile!.Id, creator.Id);

        var stored = await fixture.Db.ProjectGitMemberIdentities.AsNoTracking().ToListAsync();
        Assert.Equal(ProjectGitMemberIdentityStatus.Inactive,
            stored.Single(x => x.Id == leaveIdentityOne.Id).Status);
        Assert.Equal(ProjectGitMemberIdentityStatus.Inactive,
            stored.Single(x => x.Id == leaveIdentityTwo.Id).Status);
        Assert.Equal(ProjectGitMemberIdentityStatus.Inactive,
            stored.Single(x => x.Id == kickIdentity.Id).Status);
        Assert.Equal(GitHubOAuthConnectionStatus.Active,
            (await fixture.Db.GitHubOAuthConnections.SingleAsync(x => x.Id == kickedConnection.Id)).Status);
        Assert.Contains(await fixture.Db.ProjectGitMemberIdentityHistory.ToListAsync(),
            history => history.Action == ProjectGitMemberIdentityHistoryAction.DeactivatedByDeparture);
        Assert.Contains(await fixture.Db.ProjectGitMemberIdentityHistory.ToListAsync(),
            history => history.Action == ProjectGitMemberIdentityHistoryAction.DeactivatedByRemoval);
        var storedRepositoryLink = await fixture.Db.ProjectGitRepositories.AsNoTracking()
            .SingleAsync(x => x.Id == repositoryLink.Id);
        Assert.Null(storedRepositoryLink.UnlinkedAt);
        Assert.Equal(ProjectGitRepositoryAccessStatus.Active, storedRepositoryLink.AccessStatus);
        Assert.DoesNotContain(await fixture.Db.ProjectGitRepositoryHistory.AsNoTracking().ToListAsync(),
            history => history.ProjectGitRepositoryId == repositoryLink.Id &&
                history.Action == ProjectGitRepositoryHistoryAction.AuthorizationPending);
        Assert.Contains((project.Id, leavingMember.Profile!.Id), workspaceRevoker.RevokedMembers);
        Assert.Contains((project.Id, kickedMember.Profile.Id), workspaceRevoker.RevokedMembers);

        var cannotReactivateAfterLeaving = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.ReactivateAsync(leavingMember.Id, project.Id, leaveIdentityOne.Id,
                new(leavingConnectionOne.Id), default));
        Assert.Equal("GIT_PROJECT_MEMBERSHIP_REQUIRED", cannotReactivateAfterLeaving.Code);
    }

    [Fact]
    public async Task NonmemberCannotLinkAndAnotherMemberCannotUnlinkOrReadAcrossProjects()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var owner = fixture.AddStudent("owner");
        var outsider = fixture.AddStudent("outsider");
        var project = fixture.AddProject(creator.Profile!);
        var otherProject = fixture.AddProject(creator.Profile!);
        fixture.AddMember(project, owner.Profile!);
        var connection = fixture.AddConnection(owner, 5555);
        await fixture.Db.SaveChangesAsync();
        var identity = await fixture.Service.LinkAsync(owner.Id, project.Id, new(connection.Id), default);

        var outsideLink = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.LinkAsync(outsider.Id, project.Id, new(connection.Id), default));
        var wrongOwnerUnlink = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.UnlinkAsync(creator.Id, project.Id, identity.Id, default));
        var crossProjectRead = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.GetAsync(creator.Id, otherProject.Id, identity.Id, default));

        Assert.Equal("GIT_PROJECT_MEMBERSHIP_REQUIRED", outsideLink.Code);
        Assert.Equal("GIT_IDENTITY_NOT_OWNER", wrongOwnerUnlink.Code);
        Assert.Equal(404, crossProjectRead.Status);
    }

    [Fact]
    public async Task OAuthOwnershipAndActiveStatusAreRequiredAndRevocationDoesNotDeactivateIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var otherAccount = fixture.AddStudent("other");
        var project = fixture.AddProject(creator.Profile!);
        fixture.AddMember(project, otherAccount.Profile!);
        var connection = fixture.AddConnection(creator, 9876);
        await fixture.Db.SaveChangesAsync();

        var wrongOwner = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.LinkAsync(otherAccount.Id, project.Id, new(connection.Id), default));
        Assert.Equal("GIT_OAUTH_CONNECTION_NOT_FOUND", wrongOwner.Code);

        var identity = await fixture.Service.LinkAsync(creator.Id, project.Id, new(connection.Id), default);
        await new GitHubOAuthService(fixture.Db, fixture.Provider, fixture.Protector,
            Options.Create(Fixture.OAuthOptions())).DisconnectAsync(creator.Id, connection.Id, default);

        var stored = await fixture.Db.ProjectGitMemberIdentities.AsNoTracking().SingleAsync();
        Assert.Equal(ProjectGitMemberIdentityStatus.Active, stored.Status);
        var revokedConnection = await fixture.Db.GitHubOAuthConnections.AsNoTracking().SingleAsync();
        Assert.Equal(GitHubOAuthConnectionStatus.Revoked, revokedConnection.Status);
        Assert.Equal(identity.Id, stored.Id);
    }

    [Fact]
    public async Task RevokedOAuthConnectionCannotCreateOrReactivateIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var creator = fixture.AddStudent("creator");
        var project = fixture.AddProject(creator.Profile!);
        var connection = fixture.AddConnection(creator, 7890);
        await fixture.Db.SaveChangesAsync();
        var identity = await fixture.Service.LinkAsync(creator.Id, project.Id, new(connection.Id), default);
        await new GitHubOAuthService(fixture.Db, fixture.Provider, fixture.Protector,
            Options.Create(Fixture.OAuthOptions())).DisconnectAsync(creator.Id, connection.Id, default);
        await fixture.Service.UnlinkAsync(creator.Id, project.Id, identity.Id, default);

        var error = await Assert.ThrowsAsync<GitHubIntegrationException>(() =>
            fixture.Service.ReactivateAsync(creator.Id, project.Id, identity.Id, new(connection.Id), default));

        Assert.Equal("GIT_OAUTH_REAUTHORIZATION_REQUIRED", error.Code);
        Assert.Equal(ProjectGitMemberIdentityStatus.Inactive,
            (await fixture.Db.ProjectGitMemberIdentities.AsNoTracking().SingleAsync()).Status);
    }

    private sealed class Fixture(UniNetDbContext db, SqliteConnection connection)
        : IAsyncDisposable
    {
        public UniNetDbContext Db { get; } = db;
        public FakeProvider Provider { get; } = new();
        public FakeProtector Protector { get; } = new();
        public ProjectGitIdentityService Service { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(db, connection);
            fixture.Service = new ProjectGitIdentityService(db,
                new GitHubCredentialService(db, fixture.Provider, fixture.Protector), fixture.Provider);
            return fixture;
        }

        public Account AddStudent(string name)
        {
            var account = new Account
            {
                Email = $"{name}-{Guid.NewGuid():N}@example.test",
                Role = AccountRole.Student,
                Status = AccountStatus.Active,
                Profile = new UserProfile { FullName = name, Nickname = name }
            };
            Db.Accounts.Add(account);
            return account;
        }

        public Project AddProject(UserProfile creator)
        {
            var project = new Project
            {
                Creator = creator,
                Title = "Identity test project",
                ProjectField = "Engineering",
                Description = "Identity test",
                MemberTarget = 5,
                MinimumQuantity = 1,
                RecruitmentDeadline = DateTimeOffset.UtcNow.AddDays(1),
                Visibility = ProjectVisibility.Public,
                Status = ProjectStatus.Active,
                RecruitmentStatus = RecruitmentStatus.Open
            };
            Db.Projects.Add(project);
            return project;
        }

        public ProjectMember AddMember(Project project, UserProfile profile)
        {
            var member = new ProjectMember
            {
                Project = project,
                User = profile,
                Role = "Member",
                Status = ProjectMemberStatus.Active,
                JoinedAt = DateTimeOffset.UtcNow
            };
            Db.ProjectMembers.Add(member);
            return member;
        }

        public GitHubOAuthConnection AddConnection(Account account, long providerAccountId)
        {
            var token = $"token-{Guid.NewGuid():N}";
            Provider.Add(token, providerAccountId);
            var connection = new GitHubOAuthConnection
            {
                AccountId = account.Id,
                ProviderAccountId = providerAccountId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Login = $"github-{providerAccountId}",
                AccessTokenEncrypted = Protector.Protect(token),
                GrantedScopes = "repo",
                Status = GitHubOAuthConnectionStatus.Active
            };
            Db.GitHubOAuthConnections.Add(connection);
            return connection;
        }

        public ProjectGitRepository AddRepositoryLink(
            Project project, UserProfile linkedBy, GitHubOAuthConnection connection)
        {
            var repository = new GitHubRepository
            {
                ProviderRepositoryId = 987654,
                ProviderOwnerId = 87654,
                OwnerLogin = "repo-owner",
                Name = "repo",
                FullName = "repo-owner/repo",
                HtmlUrl = "https://github.com/repo-owner/repo",
                LastVerifiedAt = DateTimeOffset.UtcNow
            };
            Db.GitHubRepositories.Add(repository);
            var link = new ProjectGitRepository
            {
                ProjectId = project.Id,
                GitHubRepositoryId = repository.Id,
                OAuthConnectionId = connection.Id,
                LinkedByProfileId = linkedBy.Id,
                AccessStatus = ProjectGitRepositoryAccessStatus.Active
            };
            Db.ProjectGitRepositories.Add(link);
            return link;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }

        public static GitHubIntegrationOptions OAuthOptions() => new()
        {
            ClientId = "client",
            ClientSecret = "secret",
            CallbackUrl = "https://api.example.test/api/github/oauth/callback",
            KeyRingPath = Path.GetTempPath(),
            ReturnTargets = new() { ["expo-web"] = "https://app.example.test/oauth" }
        };
    }

    private sealed class FakeProtector : IGitHubTokenProtector
    {
        public string Protect(string token) => $"protected:{token}";
        public string Unprotect(string token) => token["protected:".Length..];
    }

    private sealed class FakeWorkspaceMemberRevoker : IWorkspaceMemberRevoker
    {
        public List<(Guid ProjectId, Guid UserProfileId)> RevokedMembers { get; } = [];

        public Task RevokeMemberAsync(Guid projectId, Guid userProfileId, CancellationToken ct = default)
        {
            RevokedMembers.Add((projectId, userProfileId));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvider : IGitHubProviderClient
    {
        private readonly Dictionary<string, long> accounts = new(StringComparer.Ordinal);
        public bool RejectCredentials { get; set; }
        public void Add(string token, long accountId) => accounts[token] = accountId;
        public Task<GitHubUser> GetAuthenticatedUserAsync(string token, CancellationToken ct)
        {
            if (RejectCredentials)
                throw new GitHubIntegrationException("GITHUB_CREDENTIAL_INVALID", "Invalid test credential.", 401);
            return Task.FromResult(new GitHubUser(accounts[token], $"github-{accounts[token]}", null, null));
        }
        public Task<GitHubOAuthTokens> ExchangeCodeAsync(string code, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<GitHubOAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubRemoteRepository>> GetRepositoriesAsync(
            string accessToken, int page, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<GitHubRemoteRepository> GetRepositoryAsync(
            string accessToken, long repositoryId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<GitHubSyncPage> GetSyncPageAsync(
            string accessToken, string repositoryFullName, string category, string? scope,
            int page, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
