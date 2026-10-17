using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class EvidencePostgresFactAttribute : FactAttribute
{
    public EvidencePostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")))
            Skip = "Set UNINET_TEST_POSTGRES to an isolated PostgreSQL test database; never use the application's connection string.";
    }
}

public sealed class EvidenceModerationPostgresTests
{
    private sealed class BlockedContent : IEvidenceContentReviewer
    {
        public int Calls;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<EvidenceContentVerdict> ReviewContentAsync(string text, EvidencePolicySnapshot policy, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); Entered.TrySetResult(); await Release.Task.WaitAsync(ct);
            return new(true, "Policy satisfied", "{}");
        }
    }

    private sealed class SafeUrls : IEvidenceUrlChecker
    {
        public Task<EvidenceUrlVerdict> CheckAsync(string url, EvidencePolicySnapshot policy, CancellationToken ct) =>
            Task.FromResult(new EvidenceUrlVerdict(true, "No threat list matches", "{}"));
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly string schema = "evidence_test_" + Guid.NewGuid().ToString("N");
        private readonly string connection = Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")!;
        public string ScopedConnection => new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        public UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(ScopedConnection).Options);
        public async Task CreateAsync()
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin); await create.ExecuteNonQueryAsync();
            await using var db = Open();
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
        }
        public async ValueTask DisposeAsync()
        {
            await using var admin = new NpgsqlConnection(connection); await admin.OpenAsync();
            // Only the random schema created by this fixture is removed.
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", admin); await drop.ExecuteNonQueryAsync();
        }
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["EvidenceModeration:PolicyVersion"] = "test-v1", ["EvidenceModeration:ContentInstructions"] = "Reject prohibited content",
        ["EvidenceModeration:ForbiddenTerms:0"] = "forbidden", ["EvidenceModeration:TermMatchMode"] = "WholeWord",
        ["EvidenceModeration:TextNormalization"] = "FormC", ["EvidenceModeration:CaseSensitive"] = "false",
        ["SafeBrowsing:ThreatTypes:0"] = "MALWARE", ["SafeBrowsing:PlatformTypes:0"] = "ANY_PLATFORM"
    }).Build();

    private static async Task<(Guid AccountId, Guid ProjectId)> SeedAsync(UniNetDbContext db)
    {
        var owner = new Account { Email = "owner@test.local", Role = AccountRole.Student,
            Profile = new() { FullName = "Owner", Nickname = "Owner" } };
        var project = new Project { Creator = owner.Profile!, Title = "Project", Description = "Work",
            ProjectField = "IT", MemberTarget = 1, Status = ProjectStatus.Active };
        db.Accounts.Add(owner); db.Projects.Add(project);
        db.TaskReports.Add(new() { ProjectTask = new() { Project = project, Creator = owner.Profile!, Title = "Task",
            Description = "Work", Deadline = DateTimeOffset.UtcNow }, SubmittedBy = owner.Profile!, ReportGroupId = Guid.NewGuid(), Version = 1,
            Content = "Completed work", Evidence = [new() { FileUrl = "https://example.com/proof" }] });
        await db.SaveChangesAsync(); return (owner.Id, project.Id);
    }

    [EvidencePostgresFact]
    public async Task ConcurrentCompletionProducesOneSnapshotAndOneCompletedTransition()
    {
        await using var database = new Database(); await database.CreateAsync();
        await using var seed = database.Open(); var ids = await SeedAsync(seed);
        async Task<bool> Complete()
        {
            await using var db = database.Open();
            try
            {
                await new ProjectService(db, null!, Configuration()).UpdateStatusAsync(ids.AccountId,
                    new(ids.ProjectId, ProjectStatus.Completed), default); return true;
            }
            catch (ProjectException ex) when (ex.Status == 409) { return false; }
        }
        var outcomes = await Task.WhenAll(Complete(), Complete());
        Assert.Single(outcomes, v => v);
        await using var check = database.Open();
        Assert.Single(await check.ProjectEvidenceModerations.ToListAsync());
        Assert.Equal(ProjectStatus.Completed, (await check.Projects.SingleAsync()).Status);
    }

    [EvidencePostgresFact]
    public async Task SecondWorkerCannotClaimAnUnexpiredLease()
    {
        await using var database = new Database(); await database.CreateAsync();
        await using var seed = database.Open(); var ids = await SeedAsync(seed); var config = Configuration();
        await new ProjectService(seed, null!, config).UpdateStatusAsync(ids.AccountId, new(ids.ProjectId, ProjectStatus.Completed), default);
        var content = new BlockedContent(); var now = DateTimeOffset.UtcNow;
        await using var firstDb = database.Open(); await using var secondDb = database.Open();
        var first = new EvidenceModerationTasks(firstDb, config, new(content, new SafeUrls()), new(firstDb));
        var second = new EvidenceModerationTasks(secondDb, config, new(content, new SafeUrls()), new(secondDb));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var running = first.RunAsync(now, deadline.Token);
        try
        {
            await content.Entered.Task.WaitAsync(deadline.Token);
            await second.RunAsync(now, deadline.Token);
            Assert.Equal(1, content.Calls);
        }
        finally { content.Release.TrySetResult(); await running; }
        await using var check = database.Open();
        Assert.Single(await check.EvidenceModerationAttempts.ToListAsync());
        Assert.Equal(EvidenceModerationStatus.Passed, (await check.ProjectEvidenceModerations.SingleAsync()).Status);
    }
}
