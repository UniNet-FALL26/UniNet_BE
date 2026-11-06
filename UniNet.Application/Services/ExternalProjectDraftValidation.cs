using System.Globalization;
using System.Text.RegularExpressions;
using UniNet.Application.DTOs.Projects;

namespace UniNet.Application.Services;

public static class ExternalProjectDraftValidation
{
    public static ExternalProjectDraftData Validate(ExternalProjectDraftData d)
    {
        static string? Text(string? value, int max)
        {
            value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (value?.Length > max) throw Error("INVALID_DRAFT_DATA", $"Text exceeds {max} characters.");
            return value;
        }
        static string? Date(string? value)
        {
            value = Text(value, 10);
            if (value == null) return null;
            if (!TryDate(value, false, out _)) throw Error("INVALID_IMPLEMENTATION_DATE", "Use YYYY, YYYY-MM or YYYY-MM-DD.");
            if (TryDate(value, false, out var earliest) && earliest > DateOnly.FromDateTime(DateTime.UtcNow))
                throw Error("INVALID_IMPLEMENTATION_DATE", "An imported completed project's dates cannot be in the future.");
            return value;
        }
        static string? Url(string? value)
        {
            value = Text(value, 2048);
            if (value != null && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0))
                throw Error("INVALID_DRAFT_URL", "Use an HTTP or HTTPS URL without credentials.");
            return value;
        }
        if (d.SkillIds == null || d.EvidenceFileIds == null || d.Features == null || d.Members == null ||
            d.SkillIds.Count > 30 || d.EvidenceFileIds.Count > 10 || d.Features.Count > 40 || d.Members.Count > 30 ||
            d.SkillIds.Contains(Guid.Empty) || d.EvidenceFileIds.Contains(Guid.Empty) ||
            d.SkillIds.Distinct().Count() != d.SkillIds.Count || d.EvidenceFileIds.Distinct().Count() != d.EvidenceFileIds.Count)
            throw Error("INVALID_DRAFT_DATA", "Invalid project collections.");
        var result = d with
        {
            Title = Text(d.Title, 255), Description = Text(d.Description, 2000), ProjectField = Text(d.ProjectField, 150),
            StartDate = Date(d.StartDate), EndDate = Date(d.EndDate), RepositoryUrl = Url(d.RepositoryUrl), DemoUrl = Url(d.DemoUrl),
            SubmitterRole = Text(d.SubmitterRole, 255), SubmitterContribution = Text(d.SubmitterContribution, 4000), LeaderName = Text(d.LeaderName, 255),
            Features = d.Features.Select(f => Text(f, 1000) ?? throw Error("INVALID_DRAFT_DATA", "Feature cannot be empty.")).ToList(),
            Members = d.Members.Select(m => m == null ? throw Error("INVALID_DRAFT_DATA", "Invalid member declaration.") :
                new DeclaredProjectMember(Text(m.Name, 255) ?? throw Error("INVALID_DRAFT_DATA", "Member name cannot be empty."), Text(m.Role, 255))).ToList()
        };
        if (result.StartDate != null && result.EndDate != null && TryDate(result.StartDate, false, out var start) &&
            TryDate(result.EndDate, true, out var end) && start > end)
            throw Error("INVALID_IMPLEMENTATION_DATE", "Project end must not be before project start.");
        return result;
    }

    private static bool TryDate(string value, bool latest, out DateOnly date)
    {
        date = default;
        if (!Regex.IsMatch(value, @"^[0-9]{4}(-[0-9]{2})?(-[0-9]{2})?$")) return false;
        var full = value.Length == 4 ? value + (latest ? "-12-31" : "-01-01") : value.Length == 7 ? value + "-01" : value;
        if (!DateOnly.TryParseExact(full, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return false;
        if (latest && value.Length == 7) date = new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        return true;
    }

    public static List<string> MissingFields(ExternalProjectDraftData d)
    {
        var missing = new List<string>();
        foreach (var (name, value) in new[] { ("title", d.Title), ("description", d.Description), ("projectField", d.ProjectField),
            ("startDate", d.StartDate), ("endDate", d.EndDate), ("submitterRole", d.SubmitterRole), ("submitterContribution", d.SubmitterContribution) })
            if (string.IsNullOrWhiteSpace(value)) missing.Add(name);
        if (d.SubmitterIsLeader == null) missing.Add("submitterIsLeader");
        if (d.SubmitterIsLeader == false && string.IsNullOrWhiteSpace(d.LeaderName)) missing.Add("leaderName");
        if (d.SkillIds.Count == 0) missing.Add("skillIds");
        if (d.EvidenceFileIds.Count == 0) missing.Add("evidenceFileIds");
        return missing;
    }
    private static ProjectException Error(string code, string message) => new(code, message);
}
