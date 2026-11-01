using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

internal static class AssessmentTestData
{
    internal static ProjectAssessmentInput Complete(decimal score = 80) => new("1.0",
        ProjectAssessmentPolicy.Current.Criteria.Select(c => new CriterionScoreInput(c.Code, score,
            "Reviewed against published requirements", "Demo and source code at submitted revision")).ToList(),
        ProjectAssessmentPolicy.Current.Conditions.Select(c => new ConditionCheckInput(c.Code, true, "Checked during review")).ToList());
}

public sealed class ProjectAssessmentTests
{
    [Theory]
    [InlineData(65, ProjectVerificationStatus.Verified)]
    [InlineData(64.99, ProjectVerificationStatus.Failed)]
    [InlineData(0, ProjectVerificationStatus.Failed)]
    [InlineData(100, ProjectVerificationStatus.Verified)]
    public void PassingBoundary(decimal score, ProjectVerificationStatus expected)
    {
        var result = ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.Current, AssessmentTestData.Complete(score));
        Assert.Equal(score, result.TotalScore); Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public void AppliesWeightsAndDoesNotPassByRounding()
    {
        var input = AssessmentTestData.Complete(65);
        input.Scores[6] = input.Scores[6] with { Score = 64.99m };
        var result = ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.Current, input);
        Assert.Equal(64.9995m, result.TotalScore); Assert.Equal(ProjectVerificationStatus.Failed, result.Outcome);
        input = AssessmentTestData.Complete(0);
        input.Scores[2] = input.Scores[2] with { Score = 100 };
        Assert.Equal(20m, ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.Current, input).TotalScore);
    }

    [Fact]
    public void HighScoreCannotOverrideMandatoryFailure()
    {
        var input = AssessmentTestData.Complete(100);
        input.Conditions[2] = input.Conditions[2] with { Passed = false, Reason = "Confirmed fabricated evidence" };
        var result = ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.Current, input);
        Assert.Equal(100m, result.TotalScore); Assert.Equal(ProjectVerificationStatus.Failed, result.Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownScoreOrConditionIsInsufficientAndHasNoTotal(bool scoreMissing)
    {
        var input = AssessmentTestData.Complete();
        if (scoreMissing) input.Scores[0] = input.Scores[0] with { Score = null, Evidence = null, Reason = "Requirements document missing" };
        else input.Conditions[0] = input.Conditions[0] with { Passed = null, Reason = "Sharing rights cannot be established" };
        var result = ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.Current, input);
        Assert.Null(result.TotalScore); Assert.Equal(ProjectVerificationStatus.InsufficientEvidence, result.Outcome);
    }

    [Theory]
    [InlineData("missing", "INVALID_CRITERIA")]
    [InlineData("duplicate", "INVALID_CRITERIA")]
    [InlineData("unknown", "INVALID_CRITERIA")]
    [InlineData("range", "INVALID_CRITERION_SCORE")]
    [InlineData("precision", "INVALID_CRITERION_SCORE")]
    [InlineData("reason", "INVALID_CRITERION_REASON")]
    [InlineData("evidence", "CRITERION_EVIDENCE_REQUIRED")]
    [InlineData("condition", "INVALID_CONDITIONS")]
    [InlineData("conditionReason", "INVALID_CONDITION_REASON")]
    [InlineData("version", "RUBRIC_VERSION_MISMATCH")]
    public void RejectsMalformedAssessment(string defect, string code)
    {
        var input = AssessmentTestData.Complete();
        switch (defect)
        {
            case "missing": input.Scores.RemoveAt(0); break;
            case "duplicate": input.Scores[0] = input.Scores[1]; break;
            case "unknown": input.Scores[0] = input.Scores[0] with { Code = "unknown" }; break;
            case "range": input.Scores[0] = input.Scores[0] with { Score = 101 }; break;
            case "precision": input.Scores[0] = input.Scores[0] with { Score = 64.999m }; break;
            case "reason": input.Scores[0] = input.Scores[0] with { Score = null, Reason = " " }; break;
            case "evidence": input.Scores[0] = input.Scores[0] with { Evidence = null }; break;
            case "condition": input.Conditions[0] = input.Conditions[1]; break;
            case "conditionReason": input.Conditions[0] = input.Conditions[0] with { Reason = " " }; break;
            case "version": input = input with { RubricVersion = "2.0" }; break;
        }
        Assert.Equal(code, Assert.Throws<ReportException>(() => ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.Current, input)).Code);
    }

    [Fact]
    public void MissingAssessmentCannotUseOldOutcomeOnlyContract()
        => Assert.Equal("ASSESSMENT_REQUIRED", Assert.Throws<ReportException>(() => ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.Current, null)).Code);

    [Fact]
    public void UsesPinnedThresholdAndWeightsInsteadOfCurrentDefinition()
    {
        var pinned = ProjectAssessmentPolicy.Current with { PassingScore = 85 };
        var result = ProjectAssessmentPolicy.Evaluate(ProjectAssessmentPolicy.ReadRubric(JsonSerializer.Serialize(pinned))!, AssessmentTestData.Complete(80));
        Assert.Equal(ProjectVerificationStatus.Failed, result.Outcome);
        Assert.Equal(85m, result.Rubric.PassingScore);
    }

    [Fact]
    public void MigrationBackfillPinsExactlyThePublishedRubricAndDoesNotRewriteReports()
    {
        using var db = new UniNet.Infrastructure.Data.UniNetDbContext(
            new DbContextOptionsBuilder<UniNet.Infrastructure.Data.UniNetDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var type = assembly.Migrations.Single(m => m.Key.EndsWith("_AddProjectAssessmentRubric")).Value;
        var migration = assembly.CreateMigration(type, db.Database.ProviderName!);
        var backfill = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        var start = backfill.IndexOf("'{", StringComparison.Ordinal) + 1;
        var end = backfill.IndexOf("'::jsonb", start, StringComparison.Ordinal);
        var pinned = ProjectAssessmentPolicy.ReadRubric(backfill[start..end]);
        Assert.Equal(JsonSerializer.Serialize(ProjectAssessmentPolicy.Current), JsonSerializer.Serialize(pinned));
        Assert.Contains("v.\"Status\" = 0 AND v.\"FinalizedAt\" IS NULL", backfill);
        Assert.Contains("NOT EXISTS", backfill);
        Assert.DoesNotContain("UPDATE \"VerificationReports\"", backfill);
        var script = db.GetService<IMigrator>().GenerateScript("20261101142007_PreserveWithdrawalRejectionAudit");
        Assert.Contains("\"Status\" BETWEEN 0 AND 4", script);
        Assert.Contains("AssessmentJson", script);
    }
}

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task AssessmentSurvivesReloadAndAllReportMappings()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        var request = FinalReport(data.Assignment) with { Outcome = null };
        var result = await new ReportService(f.Db).SubmitAsync(data.Staff.AccountId, request);
        f.Db.ChangeTracker.Clear();
        var detail = await new ReportService(f.Db).GetDetailAsync(result.Id, f.Owner.Id);
        Assert.Equal(80m, detail.Assessment!.TotalScore); Assert.Equal("1.0", detail.Assessment.Rubric.Version);
        Assert.Equal(8, detail.Assessment.Scores.Count); Assert.Equal(4, detail.Assessment.Conditions.Count);
        var ownerDetail = await new ProjectVerificationService(f.Db, f.Configuration).GetDetailAsync(data.Case, f.Owner.Id);
        Assert.Equal("1.0", ownerDetail.AssessmentRubric!.Version);
        Assert.Equal(80m, Assert.Single(ownerDetail.FinalResult!.Reports).Assessment!.TotalScore);
        var assignment = await new AssignmentService(f.Db, f.Configuration).GetDetailAsync(data.Assignment, data.Staff.AccountId);
        Assert.Equal("1.0", assignment.AssessmentRubric!.Version);
        Assert.Equal(80m, assignment.Report!.Assessment!.TotalScore);
        var stored = await f.Db.VerificationReports.SingleAsync();
        stored.AssessmentJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task RejectsForgedOutcomeWithoutWritingReportOrFinalizing()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        var request = FinalReport(data.Assignment) with { Assessment = AssessmentTestData.Complete(40) };
        var error = await Assert.ThrowsAsync<ReportException>(() => new ReportService(f.Db).SubmitAsync(data.Staff.AccountId, request));
        Assert.Equal("REPORT_OUTCOME_MISMATCH", error.Code);
        f.Db.ChangeTracker.Clear(); Assert.Empty(await f.Db.VerificationReports.ToListAsync());
        Assert.Equal(ProjectVerificationStatus.Pending, (await f.Db.ProjectVerifications.SingleAsync()).Status);
        Assert.Empty(await f.Db.ReportAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task InsufficientEvidenceFinalizesAsDistinctResultWithoutRefund()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        var assessment = AssessmentTestData.Complete();
        assessment.Scores[4] = assessment.Scores[4] with { Score = null, Evidence = null, Reason = "No test report supplied" };
        await new ReportService(f.Db).SubmitAsync(data.Staff.AccountId,
            FinalReport(data.Assignment) with { Outcome = null, Assessment = assessment });
        f.Db.ChangeTracker.Clear();
        var detail = await new ProjectVerificationService(f.Db, f.Configuration).GetFinalResultAsync(data.Case, f.Owner.Id);
        Assert.Equal(ProjectVerificationStatus.InsufficientEvidence, detail.Status); Assert.NotNull(detail.FinalizedAt);
        Assert.Null(Assert.Single(detail.Reports).Assessment!.TotalScore);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public async Task CaseRubricIsImmutableAndLegacyReportsRemainUnscored()
    {
        await using var f = await Fixture.CreateAsync(); var data = await PrepareFinalReportAsync(f);
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.AssessmentRubricJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
        f.Db.ChangeTracker.Clear();
        var legacy = new UniNet.Domain.Entities.VerificationReport
        {
            Id = Guid.NewGuid(), AssignmentId = data.Assignment, ProjectVerificationId = data.Case,
            Summary = "Legacy report", Outcome = ProjectVerificationStatus.Verified,
            SubmittetAt = DateTimeOffset.UtcNow
        };
        f.Db.VerificationReports.Add(legacy); await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var response = await new ReportService(f.Db).GetDetailAsync(legacy.Id, f.Owner.Id);
        Assert.Equal(ProjectVerificationStatus.Verified, response.Outcome); Assert.Null(response.Assessment);
    }
}
