using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace UniNet.Application.Services;

internal static class ContributionCanonicalJson
{
    public static string Hash(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, document.RootElement);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }
    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var p in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); Write(writer, p.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array) { writer.WriteStartArray(); foreach (var x in value.EnumerateArray()) Write(writer, x); writer.WriteEndArray(); }
        else if (value.ValueKind == JsonValueKind.Number) writer.WriteRawValue(value.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
        else value.WriteTo(writer);
    }
}
