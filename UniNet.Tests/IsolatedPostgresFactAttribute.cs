using Xunit;

namespace UniNet.Tests;

public sealed class IsolatedPostgresFactAttribute : FactAttribute
{
    public IsolatedPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNINET_TEST_POSTGRES")))
            Skip = "Set UNINET_TEST_POSTGRES to an isolated PostgreSQL test database; never use the application's connection string.";
    }
}

