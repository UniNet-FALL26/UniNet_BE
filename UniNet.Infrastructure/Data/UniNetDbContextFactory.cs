using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UniNet.Infrastructure.Data;

public sealed class UniNetDbContextFactory : IDesignTimeDbContextFactory<UniNetDbContext>
{
    public UniNetDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__UniNet");
        if (string.IsNullOrWhiteSpace(connection)) connection = ReadLocalConnection();
        connection ??= "Host=localhost;Database=uninet;Username=postgres";
        return new(new DbContextOptionsBuilder<UniNetDbContext>().UseNpgsql(connection).Options);
    }

    private static string? ReadLocalConnection()
    {
        // EF can run from the repository root or startup project directory.
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                if (!File.Exists(Path.Combine(directory.FullName, "UniNet.Infrastructure", "UniNet.Infrastructure.csproj"))) continue;
                var path = Path.Combine(directory.FullName, ".env");
                if (!File.Exists(path)) return null;
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
                    var separator = line.IndexOf('=');
                    if (separator < 0 || line[..separator].Trim() != "ConnectionStrings__UniNet") continue;
                    var value = line[(separator + 1)..].Trim();
                    if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                        value = value[1..^1];
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
                return null;
            }
        }
        return null;
    }
}
