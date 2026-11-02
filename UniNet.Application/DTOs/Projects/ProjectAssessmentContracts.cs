using UniNet.Domain.Enums;

namespace UniNet.Application;

public record AssessmentCriterion(string Code, string Name, decimal Weight);
public record AssessmentCondition(string Code, string Description);
public record AssessmentRubric(string Code, string Version, decimal PassingScore,
    List<AssessmentCriterion> Criteria, List<AssessmentCondition> Conditions);

// Scores use a 0–100 scale. Null means insufficient evidence, never zero.
public record CriterionScoreInput(string Code, decimal? Score, string Reason, string? Evidence = null);
// Null means the mandatory condition cannot yet be established.
public record ConditionCheckInput(string Code, bool? Passed, string Reason);
public record ProjectAssessmentInput(string RubricVersion,
    List<CriterionScoreInput> Scores, List<ConditionCheckInput> Conditions);
public record ProjectAssessmentResult(AssessmentRubric Rubric,
    List<CriterionScoreInput> Scores, List<ConditionCheckInput> Conditions,
    decimal? TotalScore, ProjectVerificationStatus Outcome);
