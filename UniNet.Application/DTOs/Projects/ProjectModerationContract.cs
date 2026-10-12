using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UniNet.Domain.Enums;

namespace UniNet.Application.DTOs.Projects
{
    public record ReviewProjectRequest(string? ReviewNote = null);

    public record PendingProjectModerationResponse(
        ProjectDetailResponse Project,
        ProjectModerationResponse Moderation,
        string? ResultJson,
        DateTimeOffset SubmittedAt
    );

    public record PendingProjectModerationsResponse(
        int Page,
        int PageSize,
        int TotalItems,
        int TotalPages,
        List<PendingProjectModerationResponse> Projects
    );

    public record ProjectModerationResponse(
        Guid Id,
        int AttemptNumber,
        ProjectModerationStatus Status,
        ModerationCheckResult ContentResult,
        ModerationCheckResult LinkResult,
        decimal? Confidence,
        string? ViolationReason,
        DateTimeOffset? CheckedAt,
        DateTimeOffset? ReviewedAt
    );

    public record ProjectModerationInput(
        string Title,
        string? Description,
        string? ProjectField,
        List<string> Technologies,
        int MemberTarget,
        string? ExpectedOutput,
        List<RoleRequirementRequest> RoleRequirements,
        List<string> Links
    );

    public record AiModerationResult(
        string ContentResult,
        string LinkResult,
        bool IsSpam,
        bool IsSuspicious,
        bool IsPotentiallyDuplicate,
        decimal Confidence,
        string Reason,
        List<string> Flags
    );
    

    // ============ ROLE REQUIREMENT INPUT ============

    public record ProjectRoleModerationInput(
        string Role,
        int Quantity,
        string? Requirements
    );
    public sealed class GeminiOptions
    {
        public string ApiKey { get; set; } = null!;

        public string Model { get; set; } = "gemini-3.5-flash-lite";
    }
}
