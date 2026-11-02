using System.Text.Json;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public static class ProjectAssessmentPolicy
{
    // Published versions are immutable. Add a new version instead of editing this definition.
    public static AssessmentRubric Current => new("UAS-SOFTWARE-PROJECT", "1.0", 65m,
    [
        new("requirements", "Vấn đề và yêu cầu", 10),
        new("architecture", "Kiến trúc và thiết kế", 15),
        new("code_quality", "Chất lượng mã nguồn", 20),
        new("functionality", "Chức năng và hoàn thiện", 20),
        new("testing", "Kiểm thử và độ tin cậy", 15),
        new("security", "Bảo mật và dữ liệu", 10),
        new("documentation", "Tài liệu và triển khai", 5),
        new("applicability", "Tính ứng dụng", 5)
    ],
    [
        new("reviewable_evidence", "Có demo, sản phẩm hoặc tài liệu đủ để kiểm tra và có quyền chia sẻ cho mục đích đánh giá"),
        new("core_functions", "Kiểm tra được các chức năng chính trong phạm vi công bố"),
        new("no_serious_fabrication", "Không có bằng chứng giả mạo nghiêm trọng"),
        new("no_concealed_critical_vulnerabilities", "Không che giấu lỗ hổng nghiêm trọng đã xác nhận trong phạm vi đánh giá")
    ]);

    public static string CurrentSnapshot() => JsonSerializer.Serialize(Current);
    public static AssessmentRubric? ReadRubric(string? json) => json == null ? null : JsonSerializer.Deserialize<AssessmentRubric>(json);
    public static ProjectAssessmentResult? ReadResult(string? json) => json == null ? null : JsonSerializer.Deserialize<ProjectAssessmentResult>(json);

    public static ProjectAssessmentResult Evaluate(AssessmentRubric rubric, ProjectAssessmentInput? input)
    {
        Require(input != null, "ASSESSMENT_REQUIRED", "A criterion-based assessment is required.");
        Require(input!.RubricVersion == rubric.Version, "RUBRIC_VERSION_MISMATCH", "Use the rubric version pinned to this verification.");
        Require(input.Scores != null && input.Scores.All(s => s != null) &&
            input.Scores.Count == rubric.Criteria.Count &&
            input.Scores.Select(s => s.Code).Distinct(StringComparer.Ordinal).Count() == rubric.Criteria.Count &&
            input.Scores.All(s => rubric.Criteria.Any(c => c.Code == s.Code)),
            "INVALID_CRITERIA", "Provide every rubric criterion exactly once; unknown codes are not allowed.");
        Require(input.Conditions != null && input.Conditions.All(c => c != null) &&
            input.Conditions.Count == rubric.Conditions.Count &&
            input.Conditions.Select(c => c.Code).Distinct(StringComparer.Ordinal).Count() == rubric.Conditions.Count &&
            input.Conditions.All(c => rubric.Conditions.Any(g => g.Code == c.Code)),
            "INVALID_CONDITIONS", "Provide every mandatory condition exactly once; unknown codes are not allowed.");

        var scores = rubric.Criteria.Select(c => input.Scores!.Single(s => s.Code == c.Code)).Select(s =>
        {
            Require(s.Score == null || s.Score is >= 0 and <= 100 && decimal.Round(s.Score.Value, 2) == s.Score,
                "INVALID_CRITERION_SCORE", "Scores must be between 0 and 100 with at most two decimal places.");
            Require(Text(s.Reason, 4000), "INVALID_CRITERION_REASON", "Explain each score or the missing evidence (1–4000 characters).");
            Require(s.Score == null || Text(s.Evidence, 4000), "CRITERION_EVIDENCE_REQUIRED", "Scored criteria require an evidence reference (1–4000 characters).");
            Require(s.Evidence == null || s.Evidence.Length <= 4000, "INVALID_CRITERION_EVIDENCE", "Evidence references must not exceed 4000 characters.");
            return s with { Reason = s.Reason.Trim(), Evidence = s.Evidence?.Trim() };
        }).ToList();
        var conditions = rubric.Conditions.Select(c => input.Conditions!.Single(g => g.Code == c.Code)).Select(c =>
        {
            Require(Text(c.Reason, 4000), "INVALID_CONDITION_REASON", "Explain each mandatory condition decision (1–4000 characters).");
            return c with { Reason = c.Reason.Trim() };
        }).ToList();

        // Insufficient evidence takes precedence; do not turn unknowns into failed scores.
        var insufficient = scores.Any(s => !s.Score.HasValue) || conditions.Any(c => !c.Passed.HasValue);
        decimal? total = insufficient ? null : scores.Sum(s => s.Score!.Value * rubric.Criteria.Single(c => c.Code == s.Code).Weight / 100m);
        var outcome = insufficient ? ProjectVerificationStatus.InsufficientEvidence
            : total >= rubric.PassingScore && conditions.All(c => c.Passed == true)
                ? ProjectVerificationStatus.Verified : ProjectVerificationStatus.Failed;
        // Keep the exact weighted total (up to four decimals), including near the pass boundary.
        return new(rubric, scores, conditions, total, outcome);
    }

    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;
    private static void Require(bool condition, string code, string message)
    {
        if (!condition) throw new ReportException(code, message);
    }
}
