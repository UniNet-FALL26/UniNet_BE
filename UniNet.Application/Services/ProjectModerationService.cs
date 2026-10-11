using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectModerationService(
    IAIModerationService aiModerationService, UniNetDbContext db, IConfiguration configuration)
{
    public async Task<PendingProjectModerationsResponse> GetPendingProjectsAsync(
        int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ProjectException("INVALID_PAGINATION", "Page must be positive and pageSize must be between 1 and 100.");

        var query = db.ProjectModerations.AsNoTracking()
            .Where(m => m.Status == ProjectModerationStatus.ReviewRequired &&
                !db.ProjectModerations.Any(other => other.ProjectId == m.ProjectId &&
                    other.AttemptNumber > m.AttemptNumber));
        var total = await query.CountAsync(ct);
        var pending = await query
            .Include(m => m.Project).ThenInclude(p => p.Creator)
            .Include(m => m.Project).ThenInclude(p => p.RoleRequirements)
            .Include(m => m.Project).ThenInclude(p => p.ProjectSkills).ThenInclude(s => s.Skill)
            .Include(m => m.Project).ThenInclude(p => p.Members)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new(page, pageSize, total, (int)Math.Ceiling((double)total / pageSize),
            pending.Select(m => new PendingProjectModerationResponse(
                ProjectMapping.ToDetailResponse(m.Project, null), ToResponse(m), m.ResultJson, m.CreatedAt)).ToList());
    }

    public Task<ProjectModerationResponse> ApproveProjectAsync(
        Guid projectId, Guid accountId, ReviewProjectRequest request, CancellationToken ct)
        => ReviewProjectAsync(projectId, accountId, request, ProjectModerationStatus.Approved, ct);

    public Task<ProjectModerationResponse> RejectProjectAsync(
        Guid projectId, Guid accountId, ReviewProjectRequest request, CancellationToken ct)
        => ReviewProjectAsync(projectId, accountId, request, ProjectModerationStatus.Rejected, ct);

    private async Task<ProjectModerationResponse> ReviewProjectAsync(
        Guid projectId, Guid accountId, ReviewProjectRequest request, ProjectModerationStatus decision, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw new ProjectException("PROJECT_NOT_FOUND", "Project not found.", 404);

        var note = request.ReviewNote?.Trim();
        if (decision == ProjectModerationStatus.Rejected && string.IsNullOrWhiteSpace(note))
            throw new ProjectException("REVIEW_NOTE_REQUIRED", "A review note explaining the rejection is required.");

        var reviewerId = await db.UserProfiles.Where(p => p.AccountId == accountId)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct)
            ?? throw new ProjectException("PROFILE_NOT_FOUND", "Moderator profile not found.", 403);
        var latest = await db.ProjectModerations.AsNoTracking().Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.AttemptNumber).FirstOrDefaultAsync(ct);
        if (latest == null || latest.Status != ProjectModerationStatus.ReviewRequired)
            throw new ProjectException("INVALID_MODERATION_STATE", "The latest moderation attempt must require review.", 409);

        var now = DateTimeOffset.UtcNow;
        // Compare and update in one statement so concurrent reviewers cannot both succeed.
        var updated = await db.ProjectModerations
            .Where(m => m.Id == latest.Id && m.Status == ProjectModerationStatus.ReviewRequired &&
                !db.ProjectModerations.Any(other => other.ProjectId == m.ProjectId &&
                    other.AttemptNumber > m.AttemptNumber))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, decision)
                .SetProperty(m => m.ReviewedByUserId, reviewerId)
                .SetProperty(m => m.ReviewNote, note)
                .SetProperty(m => m.ReviewAt, now)
                .SetProperty(m => m.UpdatedAt, now), ct);
        if (updated != 1)
            throw new ProjectException("INVALID_MODERATION_STATE", "This moderation attempt has already been reviewed or superseded.", 409);

        var projectStatus = decision == ProjectModerationStatus.Approved ? ProjectStatus.Active : ProjectStatus.Rejected;
        await db.Projects.Where(p => p.Id == projectId).ExecuteUpdateAsync(setters => setters
            .SetProperty(p => p.Status, projectStatus)
            .SetProperty(p => p.UpdatedAt, now), ct);
        await transaction.CommitAsync(ct);

        latest.Status = decision;
        latest.ReviewAt = now;
        return ToResponse(latest);
    }

    internal Task<ProjectModerationResponse> ModerateNewProjectAsync(Guid projectId, Guid accountId, CancellationToken ct)
        => ModerateProjectAsync(projectId, accountId, isInitialAttempt: true, ct);

    public Task<ProjectModerationResponse> SubmitProjectAsync(Guid projectId, Guid accountId, CancellationToken ct)
        => ModerateProjectAsync(projectId, accountId, isInitialAttempt: false, ct);

    private async Task<ProjectModerationResponse> ModerateProjectAsync(
        Guid projectId, Guid accountId, bool isInitialAttempt, CancellationToken ct)
    {
        var threshold = configuration.GetValue<decimal?>("ProjectModeration:AutoApproveConfidence")
            ?? throw new InvalidOperationException("ProjectModeration:AutoApproveConfidence is required.");
        if (threshold < 0m || threshold > 1m)
            throw new InvalidOperationException("ProjectModeration:AutoApproveConfidence must be between 0 and 1.");

        Project project;
        ProjectModeration moderation;
        ProjectModerationInput input;
        // Only serialize attempt creation; never hold a transaction during an AI call.
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            project = await LoadAuthorizedProjectAsync(projectId, accountId, ct);
            if (project.Status is not (ProjectStatus.Pending or ProjectStatus.Rejected))
                throw new ProjectException("INVALID_PROJECT_STATE", "Chỉ có thể gửi duyệt dự án Pending hoặc Rejected.", 400);
            if (await db.ProjectModerations.AnyAsync(m => m.ProjectId == projectId &&
                (m.Status == ProjectModerationStatus.Pending || m.Status == ProjectModerationStatus.Processing), ct))
                throw new ProjectException("MODERATION_IN_PROGRESS", "Dự án đang được kiểm duyệt.", 409);

            input = BuildInput(project);
            var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
            var latest = await db.ProjectModerations.Where(m => m.ProjectId == projectId)
                .OrderByDescending(m => m.AttemptNumber).FirstOrDefaultAsync(ct);
            if (isInitialAttempt && latest != null)
                throw new ProjectException("MODERATION_ALREADY_EXISTS", "Dự án đã có lần kiểm duyệt đầu tiên.", 409);
            if (!isInitialAttempt)
            {
                if (latest == null)
                    throw new ProjectException("INITIAL_MODERATION_REQUIRED", "Dự án chưa có lần kiểm duyệt đầu tiên.", 409);
                if (latest.Status == ProjectModerationStatus.ReviewRequired)
                    throw new ProjectException("MODERATION_AWAITING_REVIEW", "Dự án đang chờ Moderator xem xét.", 409);
                if (latest.Status != ProjectModerationStatus.Error &&
                    (latest.Status is not (ProjectModerationStatus.Rejected or ProjectModerationStatus.Approved) ||
                     latest.ContentHash == contentHash))
                    throw new ProjectException("INVALID_RESUBMISSION", "Vui lòng chỉnh sửa nội dung dự án trước khi gửi duyệt lại.", 409);
            }

            var now = DateTimeOffset.UtcNow;
            moderation = new ProjectModeration
            {
                Id = Guid.NewGuid(), ProjectId = projectId,
                AttemptNumber = (latest?.AttemptNumber ?? 0) + 1,
                Status = ProjectModerationStatus.Pending,
                ContentResult = ModerationCheckResult.Pending, LinkResult = ModerationCheckResult.Pending,
                ContentHash = contentHash,
                EngineVersion = string.IsNullOrWhiteSpace(configuration["Gemini:Model"])
                    ? "gemini-3.1-flash-lite" : configuration["Gemini:Model"],
                CreatedAt = now, UpdatedAt = now
            };
            db.ProjectModerations.Add(moderation);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        try
        {
            moderation.Status = ProjectModerationStatus.Processing;
            moderation.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var result = await aiModerationService.AnalyzeProjectAsync(input, ct);
            moderation.ContentResult = MapCheckResult(result.ContentResult);
            moderation.LinkResult = MapCheckResult(result.LinkResult);
            moderation.Confidence = result.Confidence is >= 0m and <= 1m ? result.Confidence : null;
            moderation.ViolationReason = result.Reason;
            moderation.ResultJson = JsonSerializer.Serialize(result);

            if (moderation.ContentResult == ModerationCheckResult.Error ||
                moderation.LinkResult == ModerationCheckResult.Error || moderation.Confidence == null)
            {
                moderation.Status = ProjectModerationStatus.Error;
            }
            else if (moderation.ContentResult == ModerationCheckResult.Passed &&
                     moderation.LinkResult == ModerationCheckResult.Passed &&
                     !result.IsSpam && !result.IsSuspicious && !result.IsPotentiallyDuplicate &&
                     result.Confidence >= threshold)
            {
                moderation.Status = ProjectModerationStatus.Approved;
                project.Status = ProjectStatus.Active;
                project.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                moderation.Status = ProjectModerationStatus.ReviewRequired;
            }
        }
        catch (Exception ex)
        {
            moderation.Status = ProjectModerationStatus.Error;
            moderation.ContentResult = ModerationCheckResult.Error;
            moderation.LinkResult = ModerationCheckResult.Error;
            moderation.Confidence = null;
            moderation.ViolationReason = ex.Message;
            moderation.ResultJson = JsonSerializer.Serialize(new { error = ex.Message });
        }

        moderation.CheckedAt = DateTimeOffset.UtcNow;
        moderation.UpdatedAt = moderation.CheckedAt.Value;
        // Persist terminal errors even when the caller disconnected during processing.
        await db.SaveChangesAsync(CancellationToken.None);
        return ToResponse(moderation);
    }

    public async Task<ProjectModerationResponse> GetLatestProjectModerationAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        await LoadAuthorizedProjectAsync(projectId, accountId, ct);
        var latest = await db.ProjectModerations.AsNoTracking().Where(m => m.ProjectId == projectId)
            .OrderByDescending(m => m.AttemptNumber).FirstOrDefaultAsync(ct)
            ?? throw new ProjectException("MODERATION_NOT_FOUND", "Dự án chưa có kết quả kiểm duyệt.", 404);
        return ToResponse(latest);
    }

    private static ProjectModerationResponse ToResponse(ProjectModeration moderation) => new(
        moderation.Id, moderation.AttemptNumber, moderation.Status,
        moderation.ContentResult, moderation.LinkResult, moderation.Confidence,
        moderation.ViolationReason, moderation.CheckedAt, moderation.ReviewAt);

    private async Task<Project> LoadAuthorizedProjectAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var project = await db.Projects.Include(p => p.RoleRequirements)
            .Include(p => p.ProjectSkills).ThenInclude(s => s.Skill)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectException("PROJECT_NOT_FOUND", "Dự án không tồn tại.", 404);
        // CreatorId and member UserId reference profiles, while JWT contains AccountId.
        var profileId = await db.UserProfiles.Where(p => p.AccountId == accountId)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (profileId == null || (project.CreatorId != profileId &&
            !await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == profileId &&
                m.Status == ProjectMemberStatus.Active && m.Role == "Leader", ct)))
            throw new ProjectException("UNAUTHORIZED", "Chỉ Leader hoặc người tạo dự án có thể thực hiện hành động này.", 403);
        return project;
    }

    private static ProjectModerationInput BuildInput(Project project)
    {
        var roles = project.RoleRequirements.OrderBy(r => r.Id)
            .Select(r => new RoleRequirementRequest(r.Role, r.Quantity, r.Requirements)).ToList();
        var text = string.Join("\n", new[] { project.Title, project.Description, project.ProjectField }
            .Concat(roles.Select(r => $"{r.Role}\n{r.Requirements}")));
        var links = Regex.Matches(text, "https?://[^\\s<>\"']+", RegexOptions.IgnoreCase)
            .Select(m => m.Value.TrimEnd('.', ',', ';', ')', ']')).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
        return new(project.Title, project.Description, project.ProjectField,
            project.ProjectSkills.Select(s => s.Skill.Name).OrderBy(s => s, StringComparer.Ordinal).ToList(),
            project.MemberTarget, project.ExpectedOutput?.ToString("O"), roles, links);
    }

    private static ModerationCheckResult MapCheckResult(string? result) => result?.Trim().ToLowerInvariant() switch
    {
        "safe" => ModerationCheckResult.Passed,
        "suspicious" or "unsafe" => ModerationCheckResult.Failed,
        _ => ModerationCheckResult.Error
    };

    public async Task<AiModerationResult> AnalyzeProjectAsync(
        ProjectModerationInput input,
        CancellationToken cancellationToken = default)
    {
        return await aiModerationService.AnalyzeProjectAsync(input, cancellationToken);
    }
}
