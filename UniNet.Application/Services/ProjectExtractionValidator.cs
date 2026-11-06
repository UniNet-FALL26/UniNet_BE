using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;

namespace UniNet.Application.Services;

public static class ProjectExtractionValidator
{
    public static ProjectInformationExtraction ValidateAndMatch(ProjectInformationExtraction data,
        IReadOnlyList<ProjectDocumentText> documents, IReadOnlyList<ProjectExtractionSkillCatalogItem> catalog)
    {
        var segments = documents.SelectMany(d => d.Segments.Select(s => (Key: (d.FileId, s.Number), s.Text)))
            .ToDictionary(x => x.Key, x => Normalize(x.Text));
        SourcedProjectText? Text(SourcedProjectText? value, int max, bool literal = false)
        {
            if (value == null) return null;
            if (string.IsNullOrWhiteSpace(value.Value) || value.Value.Length > max || value.Sources == null ||
                value.Sources.Count is < 1 or > 10) throw Invalid();
            foreach (var source in value.Sources)
            {
                if (source == null || string.IsNullOrWhiteSpace(source.Quote) || source.Quote.Length > 2000 ||
                    !segments.TryGetValue((source.FileId, source.SegmentNumber), out var text) ||
                    !text.Contains(Normalize(source.Quote), StringComparison.Ordinal)) throw Invalid();
            }
            if (literal && !value.Sources.Any(s => Normalize(s.Quote).Contains(Normalize(value.Value), StringComparison.OrdinalIgnoreCase)))
                throw Invalid();
            return value with { Value = value.Value.Trim() };
        }
        SourcedProjectText? Date(SourcedProjectText? value)
        {
            var result = Text(value, 10);
            if (result == null) return null;
            var date = result.Value;
            var valid = Regex.IsMatch(date, "^[0-9]{4}$") && int.Parse(date, CultureInfo.InvariantCulture) is >= 1 and <= 9999 ||
                Regex.IsMatch(date, "^[0-9]{4}-[0-9]{2}$") && DateOnly.TryParseExact(date + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                Regex.IsMatch(date, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$") && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            if (!valid) throw Invalid();
            return result;
        }
        SourcedProjectText? Url(SourcedProjectText? value)
        {
            var result = Text(value, 2048, literal: true);
            if (result != null && (!Uri.TryCreate(result.Value, UriKind.Absolute, out var url) ||
                url.Scheme is not ("https" or "http") || url.UserInfo.Length > 0)) throw Invalid();
            return result;
        }
        if (data.Features == null || data.Skills == null || data.Members == null ||
            data.Features.Count > 40 || data.Skills.Count > 30 || data.Members.Count > 30) throw Invalid();
        var skills = data.Skills.Select(s =>
        {
            if (s == null) throw Invalid();
            var name = Text(s.Name, 100, literal: true) ?? throw Invalid();
            var matches = catalog.Where(c => string.Equals(Normalize(c.Name), Normalize(name.Value), StringComparison.OrdinalIgnoreCase)).ToList();
            // IDs supplied by the model are never trusted. Ambiguous or unknown names stay unmatched.
            return new ExtractedProjectSkill(name, matches.Count == 1 ? matches[0].Id : null, matches.Count == 1 ? matches[0].Name : null);
        }).ToList();
        var members = data.Members.Select(m => m == null ? throw Invalid() : new SuggestedProjectMember(
            Text(m.Name, 255, literal: true) ?? throw Invalid(), Text(m.Role, 255))).ToList();
        return data with
        {
            Title = Text(data.Title, 255), Description = Text(data.Description, 2000), ProjectField = Text(data.ProjectField, 255),
            StartDate = Date(data.StartDate), EndDate = Date(data.EndDate), RepositoryUrl = Url(data.RepositoryUrl), DemoUrl = Url(data.DemoUrl),
            Features = data.Features.Select(f => Text(f, 1000) ?? throw Invalid()).ToList(), Skills = skills, Members = members
        };
    }

    private static string Normalize(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    private static ProjectExtractionException Invalid() =>
        new("EXTRACTION_INVALID_RESPONSE", "Extracted information is malformed or lacks valid document citations.", true);
}
