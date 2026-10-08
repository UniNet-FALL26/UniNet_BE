using Microsoft.EntityFrameworkCore;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

[CollectionDefinition("Design-time configuration", DisableParallelization = true)]
public sealed class DesignTimeConfigurationCollection;

[Collection("Design-time configuration")]
public sealed class DbContextFactoryTests : IDisposable
{
    private readonly string originalDirectory = Directory.GetCurrentDirectory();
    private readonly string? originalConnection = Environment.GetEnvironmentVariable("ConnectionStrings__UniNet");
    private readonly string root = Path.Combine(Path.GetTempPath(), "uninet-ef-tests-" + Guid.NewGuid().ToString("N"));
    private const string FileConnection = "Host=localhost;Database=file_test;Username=test;Password=example#with=equals";

    public DbContextFactoryTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "UniNet.Infrastructure"));
        File.WriteAllText(Path.Combine(root, "UniNet.Infrastructure", "UniNet.Infrastructure.csproj"), "<Project />");
        Environment.SetEnvironmentVariable("ConnectionStrings__UniNet", null);
        Directory.SetCurrentDirectory(root);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "\"")]
    [InlineData(false, "'")]
    public void ReadsBackendEnvFromRootOrInfrastructureWithoutExportingSecrets(bool infrastructureDirectory, string quote)
    {
        File.WriteAllText(Path.Combine(root, ".env"), $"# Other settings\nJwt__Key=unused\nConnectionStrings__UniNet={quote}{FileConnection}{quote}\n");
        if (infrastructureDirectory) Directory.SetCurrentDirectory(Path.Combine(root, "UniNet.Infrastructure"));
        using var db = new UniNetDbContextFactory().CreateDbContext([]);
        Assert.Equal(FileConnection, db.Database.GetConnectionString());
        Assert.Null(Environment.GetEnvironmentVariable("ConnectionStrings__UniNet"));
    }

    [Fact]
    public void ExplicitEnvironmentOverridesLocalFile()
    {
        File.WriteAllText(Path.Combine(root, ".env"), "ConnectionStrings__UniNet=" + FileConnection);
        const string overrideConnection = "Host=localhost;Database=override_test;Username=test;Password=explicit";
        Environment.SetEnvironmentVariable("ConnectionStrings__UniNet", overrideConnection);
        using var db = new UniNetDbContextFactory().CreateDbContext([]);
        Assert.Equal(overrideConnection, db.Database.GetConnectionString());
    }

    [Fact]
    public void IgnoresUnrelatedKeysAndSupportsExportPrefixAndWhitespace()
    {
        File.WriteAllText(Path.Combine(root, ".env"), $"ConnectionStrings__Other=ignored\n export ConnectionStrings__UniNet = '{FileConnection}' \n");
        using var db = new UniNetDbContextFactory().CreateDbContext([]);
        Assert.Equal(FileConnection, db.Database.GetConnectionString());
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(originalDirectory);
        Environment.SetEnvironmentVariable("ConnectionStrings__UniNet", originalConnection);
        Directory.Delete(root, recursive: true);
    }
}
