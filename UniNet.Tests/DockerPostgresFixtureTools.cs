using System.Diagnostics;
using System.Text.Json;
using Npgsql;

namespace UniNet.Tests;

// Optional native-tool fallback for isolated migration backup/restore tests.
// The database connection must match the explicitly selected disposable container.
internal static class DockerPostgresFixtureTools
{
    public static async Task<bool> TryRunAsync(string tool, NpgsqlConnectionStringBuilder connection, string[] arguments)
    {
        var container = Environment.GetEnvironmentVariable("UNINET_POSTGRES_TEST_CONTAINER");
        if (container is null) return false;
        if (tool is not ("pg_dump" or "pg_restore") || !container.StartsWith("uninet-main-merge-", StringComparison.Ordinal) ||
            connection.Host != "127.0.0.1" || connection.Port is 55432 or 55433 || connection.Username != "postgres")
            throw new InvalidOperationException("Docker PostgreSQL tools require an explicitly verified disposable target.");
        var inspection = await Run("inspect", container);
        using var document = JsonDocument.Parse(inspection);
        var endpoint = document.RootElement[0].GetProperty("NetworkSettings").GetProperty("Ports").GetProperty("5432/tcp")[0];
        if (endpoint.GetProperty("HostIp").GetString() != "127.0.0.1" ||
            endpoint.GetProperty("HostPort").GetString() != connection.Port.ToString())
            throw new InvalidOperationException("Disposable container/connection port mismatch.");
        var remote = "/tmp/uninet-fixture-" + Guid.NewGuid().ToString("N") + ".dump";
        var args = arguments.ToArray();
        string local;
        if (tool == "pg_dump")
        {
            var index = Array.FindIndex(args, x => x.StartsWith("--file=", StringComparison.Ordinal));
            if (index < 0) throw new InvalidOperationException("A fixture dump file is required.");
            local = args[index][7..]; args[index] = "--file=" + remote;
        }
        else
        {
            local = args[^1]; args[^1] = remote;
            await Run("cp", local, container + ":" + remote);
        }
        try
        {
            await Run(["exec", "-e", "PGHOST=/var/run/postgresql", "-e", "PGPORT=5432", "-e", "PGUSER=postgres",
                "-e", "PGDATABASE=" + connection.Database, container, tool, .. args]);
            if (tool == "pg_dump") await Run("cp", container + ":" + remote, local);
        }
        finally { await Run("exec", container, "rm", "-f", remote); }
        return true;
    }

    private static async Task<string> Run(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(deadline.Token);
        if (process.ExitCode != 0) throw new InvalidOperationException("Disposable PostgreSQL tool failed: " + await error);
        await error; return await output;
    }
}
