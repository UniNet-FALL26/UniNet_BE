using System.Text.Json;

namespace UniNet.Application;

public static class PartnerWebsiteLinks
{
    public static IReadOnlyList<string> ParseStored(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        try
        {
            var links = JsonSerializer.Deserialize<List<string>>(value);
            if (links is not null) return links.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray();
        }
        catch (JsonException) { }
        return [value.Trim()];
    }

    public static IReadOnlyList<string> FromRequest(IReadOnlyList<string>? websites, string? legacyWebsite)
    {
        IEnumerable<string?> source = websites is not null
            ? websites
            : string.IsNullOrWhiteSpace(legacyWebsite) ? Array.Empty<string?>() : new[] { legacyWebsite };
        return source.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string? NormalizeUrl(string value)
    {
        var candidate = value.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            candidate = $"https://{candidate}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host)) return null;
        return uri.ToString();
    }

    public static string? Serialize(IReadOnlyList<string> websites) =>
        websites.Count == 0 ? null : JsonSerializer.Serialize(websites);
}