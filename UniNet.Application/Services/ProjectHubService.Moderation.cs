using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed partial class ProjectHubService
{
    private async Task ModerateCreationAsync(Project project, CancellationToken ct)
    {
        var threshold = configuration.GetValue("ProjectModeration:AutoRejectConfidence",
            configuration.GetValue("ProjectModeration:AutoApproveConfidence", 0.9m));
        if (threshold is < 0m or > 1m)
            throw new InvalidOperationException("Project moderation confidence must be between 0 and 1.");

        var input = new ProjectModerationInput(project.Title, project.Description, null, [], 0, [], [])
        {
            CheckProhibitedKeywordsOnly = true
        };
        AiModerationResult result;
        try
        {
            result = await aiModeration.AnalyzeProjectAsync(input, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            result = new("error", "error", false, false, false, 0m,
                "AI moderation unavailable. Manual review required.", ["AI_UNAVAILABLE"]);
        }
        ct.ThrowIfCancellationRequested();

        var content = result.ContentResult?.Trim().ToLowerInvariant();
        var check = content switch
        {
            "safe" => ModerationCheckResult.Passed,
            "unsafe" or "suspicious" => ModerationCheckResult.Failed,
            _ => ModerationCheckResult.Error
        };
        decimal? confidence = check != ModerationCheckResult.Error && result.Confidence is >= 0m and <= 1m
            ? result.Confidence : null;
        var rejected = content == "unsafe" && confidence >= threshold;
        var now = DateTimeOffset.UtcNow;
        project.Status = rejected ? ProjectStatus.Rejected : ProjectStatus.Pending;
        project.UpdatedAt = now;
        project.Moderations.Add(new ProjectModeration
        {
            Purpose = ProjectModerationPurpose.ProjectHubSubmission,
            AttemptNumber = 1,
            Status = rejected ? ProjectModerationStatus.Rejected : ProjectModerationStatus.ReviewRequired,
            ContentResult = check,
            LinkResult = ModerationCheckResult.Pending,
            Confidence = confidence,
            ViolationReason = check == ModerationCheckResult.Error
                ? "AI moderation unavailable. Manual review required." : result.Reason,
            ResultJson = JsonSerializer.Serialize(new { content, confidence, result.Flags }),
            EngineVersion = configuration["Gemini:Model"] ?? "gemini-3.1-flash-lite",
            ContentHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input))),
            CreatedAt = project.CreatedAt,
            CheckedAt = now,
            UpdatedAt = now
        });
    }

    private Task<bool> HasHubModeration(Guid projectId, CancellationToken ct) =>
        db.ProjectModerations.AnyAsync(m => m.ProjectId == projectId &&
            m.Purpose == ProjectModerationPurpose.ProjectHubSubmission, ct);

    private async Task RequireApprovedSubmission(Project project, CancellationToken ct)
    {
        if (project.Status is (ProjectStatus.Pending or ProjectStatus.Rejected) && await HasHubModeration(project.Id, ct))
            throw new ProjectException("PROJECT_MODERATION_REQUIRED", "The project must be approved before this action.", 409);
    }
}
