using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed class EvidenceModerationChecks(IEvidenceContentReviewer content, IEvidenceUrlChecker urls)
{
    private static readonly Regex Links = new(@"(?:\b[a-z][a-z0-9+.\-]*://|\b(?:javascript|data|vbscript|mailto):|\bwww\.)[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MarkdownLinks = new(@"\[[^\]]*\]\(([^\s)]+)(?:\s+[^)]*)?\)", RegexOptions.CultureInvariant);

    public static void ValidatePolicy(EvidencePolicySnapshot policy)
    {
        if (string.IsNullOrWhiteSpace(policy.Version) || string.IsNullOrWhiteSpace(policy.ContentInstructions) ||
            policy.ForbiddenTerms.Length == 0 || policy.ForbiddenTerms.Any(string.IsNullOrWhiteSpace) ||
            policy.TermMatchMode is not ("Substring" or "WholeWord") ||
            policy.TextNormalization is not ("FormC" or "StripDiacritics") || policy.CaseSensitive == null ||
            string.IsNullOrWhiteSpace(policy.GeminiModel) || policy.Scope != "ReportTextAndUrls" ||
            policy.ThreatTypes.Length == 0 || policy.ThreatTypes.Any(string.IsNullOrWhiteSpace) ||
            policy.PlatformTypes.Length == 0 || policy.PlatformTypes.Any(string.IsNullOrWhiteSpace))
            throw new VerificationAiException("Configure the versioned evidence content policy, matching rules and Safe Browsing lists.",
                false, "EVIDENCE_POLICY_NOT_CONFIGURED");
    }

    public async Task<List<EvidenceCheckResult>> RunAsync(EvidenceSourceSnapshot source, EvidencePolicySnapshot policy,
        CancellationToken ct)
    {
        var results = new List<EvidenceCheckResult>();
        foreach (var task in source.Tasks)
        foreach (var report in task.Reports)
        {
            ct.ThrowIfCancellationRequested();
            var text = Normalize(report.Content, policy);
            var terms = policy.ForbiddenTerms.Where(term => Matches(text, Normalize(term, policy), policy)).ToArray();
            if (terms.Length > 0)
                results.Add(new(task.ProjectTaskId, report.TaskReportId, null, "ReportText", null,
                    EvidenceCheckStatus.Rejected, "Forbidden term matched under configured literal matching policy.",
                    "ConfiguredTerms", DateTimeOffset.UtcNow, System.Text.Json.JsonSerializer.Serialize(terms)));
            else if (string.IsNullOrWhiteSpace(report.Content))
                results.Add(new(task.ProjectTaskId, report.TaskReportId, null, "ReportText", null,
                    EvidenceCheckStatus.NotChecked, "Report text is empty.", "None", DateTimeOffset.UtcNow));
            else
            {
                try
                {
                    var verdict = await content.ReviewContentAsync(report.Content, policy, ct);
                    if (string.IsNullOrWhiteSpace(verdict.Reason) || string.IsNullOrWhiteSpace(verdict.Proof))
                        throw new VerificationAiException("Content provider returned incomplete evidence.", true, "AI_INVALID_RESPONSE");
                    results.Add(new(task.ProjectTaskId, report.TaskReportId, null, "ReportText", null,
                        verdict.Passed ? EvidenceCheckStatus.Passed : EvidenceCheckStatus.Rejected,
                        verdict.Reason, "Gemini:" + policy.GeminiModel, DateTimeOffset.UtcNow, verdict.Proof));
                }
                catch (VerificationAiException ex) { results.Add(Error(task, report, null, "ReportText", null, "Gemini", ex)); }
            }
            var links = Links.Matches(report.Content).Select(m => TrimLink(m.Value))
                .Concat(MarkdownLinks.Matches(report.Content).Select(m => m.Groups[1].Value).Where(url => !Links.IsMatch(url)));
            var targets = links.Select(url => (EvidenceId: (Guid?)null, Url: url))
                .Concat(report.Evidence.Select(e => (EvidenceId: (Guid?)e.TaskEvidenceId, Url: e.FileUrl)))
                .Distinct().OrderBy(t => t.EvidenceId).ThenBy(t => t.Url, StringComparer.Ordinal);
            foreach (var target in targets)
            {
                try
                {
                    var verdict = await urls.CheckAsync(target.Url, policy, ct);
                    if (string.IsNullOrWhiteSpace(verdict.Reason) || string.IsNullOrWhiteSpace(verdict.Proof))
                        throw new VerificationAiException("URL provider returned incomplete evidence.", true, "URL_INVALID_RESPONSE");
                    results.Add(new(task.ProjectTaskId, report.TaskReportId, target.EvidenceId, "Url", target.Url,
                        verdict.Passed ? EvidenceCheckStatus.Passed : EvidenceCheckStatus.Rejected,
                        verdict.Reason, "GoogleSafeBrowsing:v4", DateTimeOffset.UtcNow, verdict.Proof));
                }
                catch (VerificationAiException ex) { results.Add(Error(task, report, target.EvidenceId, "Url", target.Url, "GoogleSafeBrowsing:v4", ex)); }
            }
            // File inspection is explicitly excluded by the agreed scope, never counted as a passed check.
            foreach (var file in report.Evidence)
                results.Add(new(task.ProjectTaskId, report.TaskReportId, file.TaskEvidenceId, "FileContent", file.FileUrl,
                    EvidenceCheckStatus.NotChecked, "Out of scope: file content is not downloaded, read or inspected.", "None", DateTimeOffset.UtcNow));
        }
        return results;
    }

    private static EvidenceCheckResult Error(EvidenceTaskSnapshot task, EvidenceReportSnapshot report, Guid? evidenceId,
        string kind, string? url, string provider, VerificationAiException ex) => new(task.ProjectTaskId, report.TaskReportId,
            evidenceId, kind, url, EvidenceCheckStatus.Error, ex.Message, provider, DateTimeOffset.UtcNow,
            ErrorCode: ex.Code, Transient: ex.Transient);

    private static string Normalize(string value, EvidencePolicySnapshot policy)
    {
        if (policy.TextNormalization == "StripDiacritics")
        {
            value = string.Concat(value.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
                .Replace('đ', 'd').Replace('Đ', 'D');
        }
        value = value.Normalize(NormalizationForm.FormC);
        return policy.CaseSensitive == true ? value : value.ToUpperInvariant();
    }

    private static bool Matches(string text, string term, EvidencePolicySnapshot policy) => policy.TermMatchMode == "Substring"
        ? text.Contains(term, StringComparison.Ordinal)
        : Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(term) + @"(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant);

    private static string TrimLink(string value)
    {
        value = value.TrimEnd('.', ',', ';');
        foreach (var pair in new[] { (Open: '(', Close: ')'), (Open: '[', Close: ']'), (Open: '{', Close: '}') })
            while (value.EndsWith(pair.Close) && value.Count(c => c == pair.Close) > value.Count(c => c == pair.Open))
                value = value[..^1];
        return value;
    }
}
