namespace UniNet.Tests;

internal static class GitHubAppDisposableDatabase
{
    public static string Name(string fallback)
    {
        var name = Environment.GetEnvironmentVariable("UNINET_GITHUBAPP_TEST_DATABASE");
        if (name is null) return fallback;
        if (!name.StartsWith("uninet_step5b_", StringComparison.Ordinal) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("The override must identify a dedicated Step 5B disposable database.");
        return name;
    }
}
