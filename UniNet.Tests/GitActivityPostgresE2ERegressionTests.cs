using Microsoft.EntityFrameworkCore;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class GitActivityPostgresE2ERegressionTests
{
    private sealed class E2EPostgresFactAttribute : FactAttribute
    {
        public E2EPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNINET_GIT_ACTIVITY_E2E_POSTGRES")))
                Skip = "Requires the existing isolated UniNet Git Activity E2E database.";
        }
    }

    [E2EPostgresFact]
    public async Task ProjectActivityCanBeReadFromTheConfiguredE2EDatabase()
    {
        var connection = Environment.GetEnvironmentVariable("UNINET_GIT_ACTIVITY_E2E_POSTGRES");
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection!);
        Assert.Equal("127.0.0.1", builder.Host);
        Assert.Equal(55432, builder.Port);
        Assert.Equal("uninet_e2e", builder.Database);
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(connection).Options);
        var result = await new ProjectGitActivityService(db).GetAsync(
            Guid.Parse("295ff35a-e0e9-45dd-a4b0-0deb893e36fa"),
            Guid.Parse("ad3b101e-fc07-4061-9e43-b1b539cd1719"), 1, 50, null, null, null, default);
        Assert.True(result.TotalCount > 0);
        Assert.Contains(result.Items, x => x.ActivityType == "commit.created");
        Assert.All(result.Items, x => Assert.Equal("trangtanduoc/uninet-github-e2e", x.RepositoryFullName));
    }
}
