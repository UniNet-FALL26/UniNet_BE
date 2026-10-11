namespace UniNet.API.Services;

public static class LocalEnvConfiguration
{
    public static Dictionary<string, string?> Read(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return values;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var separator = line.IndexOf('=');
            if (separator < 1) continue;
            var name = line[..separator].Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$")) continue;
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'')) value = value[1..^1];
            values[name.Replace("__", ":")] = value;
        }
        return values;
    }

    public static void Apply(ConfigurationManager configuration, string contentRoot, string[] args)
    {
        var path = Path.Combine(contentRoot, ".env");
        var parent = Directory.GetParent(contentRoot)?.FullName;
        if (!File.Exists(path) && parent is not null && File.Exists(Path.Combine(parent, "UniNet.sln"))) path = Path.Combine(parent, ".env");
        configuration.AddInMemoryCollection(Read(path));
        // Explicit deployment environment and CLI flags always override local file defaults.
        configuration.AddEnvironmentVariables();
        if (args.Length > 0) configuration.AddCommandLine(args);
    }
}
