using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ProjectTaskService(UniNetDbContext db, IConfiguration configuration) : IProjectTaskService
{
    private sealed record ValidatedEvidence(
        string FileUrl,
        string? PublicId,
        string? ResourceType,
        string? FileName,
        string? Format);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    {
        if (!condition)
            throw new ProjectException(code, message, status);
    }

    private static ProjectException NotFound() => new("PROJECT_TASK_NOT_FOUND", "Task not found.", 404);
    private static ProjectException Forbidden() => new("FORBIDDEN", "Only the project leader can perform this action.", 403);

    private async Task<UserProfile> GetProfileAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);
        Require(profile is not null, "PROFILE_NOT_FOUND", "User profile not found.", 404);

        var account = profile.Account;
        Require(account is not null && account.Status == AccountStatus.Active,
            "ACCOUNT_INACTIVE", "Only active accounts can access project tasks.", 403);
        return profile;
    }

    private async Task<Project> GetProjectAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        Require(project is not null, "PROJECT_NOT_FOUND", "Project not found.", 404);
        return project;
    }

    private async Task EnsureProjectParticipantAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var profile = await GetProfileAsync(accountId, ct);
        var project = await db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        Require(project is not null, "PROJECT_NOT_FOUND", "Project not found.", 404);

        var isCreator = project.CreatorId == profile.Id;
        var isMember = await db.ProjectMembers.AsNoTracking()
            .AnyAsync(m => m.ProjectId == projectId && m.UserId == profile.Id && m.Status == ProjectMemberStatus.Active, ct);

        Require(isCreator || isMember, "PROJECT_ACCESS_DENIED", "You are not authorized to access this project.", 403);
    }

    private async Task EnsureProjectLeaderAsync(Guid projectId, Guid accountId, CancellationToken ct)
    {
        var profile = await GetProfileAsync(accountId, ct);
        var project = await GetProjectAsync(projectId, ct);
        Require(project.CreatorId == profile.Id, "FORBIDDEN", "Only the project leader can perform this action.", 403);
    }

    private static void ValidateTitle(string? title)
    {
        Require(!string.IsNullOrWhiteSpace(title) && title.Trim().Length <= 255,
            "INVALID_TASK_TITLE", "Task title must be between 1 and 255 characters.");
    }

    private static void ValidateDescription(string? description)
    {
        Require(!string.IsNullOrWhiteSpace(description) && description.Trim().Length <= 2000,
            "INVALID_TASK_DESCRIPTION", "Task description must be between 1 and 2000 characters.");
    }

    private static bool IsTerminalStatus(ProjectTaskStatus status)
        => status is ProjectTaskStatus.Completed or ProjectTaskStatus.Cancelled;

    private static ProjectTaskStatus ResolveEffectiveStatus(ProjectTask task, DateTimeOffset now)
    {
        if (task.Status == ProjectTaskStatus.Overdue)
            return ProjectTaskStatus.Overdue;

        if (IsTerminalStatus(task.Status))
            return task.Status;

        return task.Deadline <= now ? ProjectTaskStatus.Overdue : task.Status;
    }

    private static void ValidateStatus(ProjectTaskStatus status)
    {
        Require(Enum.IsDefined(status), "INVALID_TASK_STATUS", "Task status is invalid.");
        Require(status != ProjectTaskStatus.Overdue, "INVALID_TASK_STATUS", "Task status cannot be set to Overdue; overdue is determined automatically.");
    }

    private static DateTimeOffset NormalizeDeadline(DateTimeOffset deadline)
        => deadline.ToUniversalTime();

    private static List<Guid> NormalizeUserIds(List<Guid>? userIds)
    {
        if (userIds is null)
            return [];

        return userIds.Distinct().ToList();
    }

    private async Task<List<ProjectMember>> GetValidAssignedMembersAsync(Guid projectId, List<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return [];

        var creatorId = await db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => (Guid?)p.CreatorId)
            .FirstOrDefaultAsync(ct);

        var members = await db.ProjectMembers
            .Include(m => m.User)
            .Where(m => m.ProjectId == projectId && m.Status == ProjectMemberStatus.Active && userIds.Contains(m.UserId))
            .ToListAsync(ct);

        var validIds = members.Select(m => m.UserId).ToHashSet();
        if (creatorId.HasValue && userIds.Contains(creatorId.Value))
            validIds.Add(creatorId.Value);

        var invalid = userIds.Where(id => !validIds.Contains(id)).Distinct().ToList();
        Require(invalid.Count == 0, "INVALID_TASK_ASSIGNEE", "Only active project members can be assigned to a task.", 400);

        return members;
    }

    private async Task<ProjectTask> GetTaskInProjectAsync(Guid projectId, Guid taskId, CancellationToken ct)
    {
        var task = await db.ProjectTasks.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct);
        return task ?? throw new ProjectException("PROJECT_TASK_NOT_FOUND", "Task not found.", 404);
    }

    private async Task<UserProfile> EnsureAssignedMemberAsync(Guid projectId, Guid taskId, Guid accountId, CancellationToken ct)
    {
        var profile = await GetProfileAsync(accountId, ct);
        var projectExists = await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, ct);
        Require(projectExists, "PROJECT_NOT_FOUND", "Project not found.", 404);
        var task = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.Id == taskId && t.ProjectId == projectId)
            .Select(t => new
            {
                t.Id,
                t.Project.CreatorId,
                IsAssigned = t.Assignments.Any(a => a.UserId == profile.Id)
            })
            .SingleOrDefaultAsync(ct);
        if (task is null)
            throw new ProjectException("PROJECT_TASK_NOT_FOUND", "Task not found.", 404);

        var isActiveParticipant = task.CreatorId == profile.Id ||
            await db.ProjectMembers.AsNoTracking().AnyAsync(m =>
                m.ProjectId == projectId &&
                m.UserId == profile.Id &&
                m.Status == ProjectMemberStatus.Active, ct);
        Require(task.IsAssigned && isActiveParticipant,
            "TASK_REPORT_SUBMISSION_DENIED",
            "Only active project members assigned to this task can submit reports.",
            403);
        return profile;
    }

    private async Task<TaskReport> GetTaskReportAsync(
        Guid projectId, Guid taskId, Guid reportId, CancellationToken ct)
    {
        var report = await db.TaskReports
            .AsSplitQuery()
            .Include(r => r.SubmittedBy)
            .Include(r => r.Evidence)
            .Include(r => r.Reviews).ThenInclude(r => r.Reviewer)
            .SingleOrDefaultAsync(r =>
                r.Id == reportId &&
                r.ProjectTaskId == taskId &&
                r.ProjectTask.ProjectId == projectId, ct);
        return report ?? throw new ProjectException("TASK_REPORT_NOT_FOUND", "Task report not found.", 404);
    }

    private static (string Content, List<ValidatedEvidence> Evidence) ValidateReport(
        SubmitTaskReportRequest request,
        Guid submitterAccountId,
        string? cloudName)
    {
        if (request is null)
            throw new ProjectException("INVALID_TASK_REPORT", "Report body is required.");
        Require(!string.IsNullOrWhiteSpace(request.Content) && request.Content.Trim().Length <= 5000,
            "INVALID_TASK_REPORT_CONTENT", "Report content must be between 1 and 5000 characters.");
        var evidence = request.Evidence;
        if (evidence is null)
            throw new ProjectException("INVALID_TASK_EVIDENCE", "Evidence list is required.");

        var validatedEvidence = new List<ValidatedEvidence>(evidence.Count);
        foreach (var item in evidence)
        {
            Require(item is not null, "INVALID_TASK_EVIDENCE_URL", "Evidence item is required.");
            var fileUrl = item.FileUrl.Trim();
            Require(!string.IsNullOrWhiteSpace(fileUrl) && fileUrl.Length <= 2048 &&
                Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                string.IsNullOrEmpty(uri.UserInfo),
                "INVALID_TASK_EVIDENCE_URL",
                "Evidence URLs must be absolute HTTPS URLs without embedded credentials.");

            var metadataCount = new[] { item.PublicId, item.ResourceType, item.FileName, item.Format }
                .Count(value => value is not null);
            if (metadataCount == 0)
            {
                validatedEvidence.Add(new(fileUrl!, null, null, null, null));
                continue;
            }

            var publicId = item.PublicId?.Trim();
            var resourceType = item.ResourceType?.Trim();
            var fileName = item.FileName?.Trim();
            var format = item.Format?.Trim().ToLowerInvariant();
            Require(
                !string.IsNullOrWhiteSpace(publicId) &&
                !string.IsNullOrWhiteSpace(resourceType) &&
                !string.IsNullOrWhiteSpace(format) &&
                IsValidCloudinaryEvidence(
                    fileUrl!,
                    publicId!,
                    resourceType!,
                    fileName,
                    format!,
                    submitterAccountId,
                    cloudName),
                "INVALID_TASK_EVIDENCE_METADATA",
                "Uploaded evidence metadata must match a secure Cloudinary asset uploaded by the current user.");

            validatedEvidence.Add(new(
                fileUrl!,
                publicId,
                resourceType,
                fileName,
                format));
        }

        return (request.Content.Trim(), validatedEvidence);
    }

    private static bool IsValidCloudinaryEvidence(
        string fileUrl,
        string publicId,
        string resourceType,
        string? fileName,
        string format,
        Guid accountId,
        string? cloudName)
    {
        var supportedFormat = resourceType switch
        {
            "image" => format is "png" or "jpg" or "webp",
            "raw" => format is "pdf" or "docx" or "xlsx" or "pptx" or "txt",
            _ => false
        };
        if (!supportedFormat || string.IsNullOrWhiteSpace(cloudName) || publicId.Length > 255)
            return false;

        var prefix = resourceType == "image"
            ? $"uninet/images/{accountId:N}/"
            : $"uninet/evidence/{accountId:N}/";
        if (!publicId.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var storedName = publicId[prefix.Length..];
        var expectedName = resourceType == "image" ? storedName : Path.GetFileNameWithoutExtension(storedName);
        var expectedExtension = resourceType == "image" ? string.Empty : $".{format}";
        if (!Regex.IsMatch(expectedName, "^[a-f0-9]{32}$", RegexOptions.CultureInvariant) ||
            resourceType == "image" && storedName != expectedName ||
            resourceType == "raw" && !storedName.EndsWith(expectedExtension, StringComparison.Ordinal))
            return false;

        if (fileName is not null)
        {
            if (fileName.Length > 255 || fileName.Any(char.IsControl) ||
                fileName.Contains('/') || fileName.Contains('\\') || fileName is "." or "..")
                return false;
            var fileExtension = Path.GetExtension(fileName);
            var validFileExtension = resourceType == "image"
                ? format == "jpg"
                    ? string.Equals(fileExtension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(fileExtension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(fileExtension, $".{format}", StringComparison.OrdinalIgnoreCase)
                : string.Equals(fileExtension, $".{format}", StringComparison.OrdinalIgnoreCase);
            if (!validFileExtension)
                return false;
        }

        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host != "res.cloudinary.com" ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 5 ||
            !string.Equals(segments[0], cloudName, StringComparison.Ordinal) ||
            !string.Equals(segments[1], resourceType, StringComparison.Ordinal) ||
            !string.Equals(segments[2], "upload", StringComparison.Ordinal) ||
            !Regex.IsMatch(segments[3], "^v[0-9]+$", RegexOptions.CultureInvariant))
            return false;

        var deliveredAsset = string.Join('/', segments.Skip(4));
        var expectedAsset = resourceType == "image" ? $"{publicId}.{format}" : publicId;
        return string.Equals(deliveredAsset, expectedAsset, StringComparison.Ordinal);
    }

    private static TaskReportResponse MapReport(TaskReport report)
        => new(
            report.Id,
            report.ProjectTaskId,
            report.SubmittedById,
            report.SubmittedBy.FullName,
            report.Version,
            report.Content,
            report.Status,
            report.SubmittedAt,
            report.UpdatedAt,
            report.Evidence
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.Id)
                .Select(e => new TaskEvidenceResponse(
                    e.Id,
                    e.FileUrl,
                    e.CreatedAt,
                    e.PublicId,
                    e.ResourceType,
                    e.FileName,
                    e.Format))
                .ToList(),
            report.Reviews
                .OrderBy(r => r.ReviewedAt)
                .ThenBy(r => r.Id)
                .Select(r => new TaskReportReviewResponse(
                    r.Id,
                    r.ReviewerId,
                    r.Reviewer.FullName,
                    r.Decision,
                    r.Feedback,
                    r.ReviewedAt))
                .ToList());

    private static ProjectTaskResponse Map(ProjectTask task)
    {
        var assignments = task.Assignments
            .OrderBy(a => a.User.FullName)
            .ThenBy(a => a.UserId)
            .Select(a => new ProjectTaskAssignmentInfo(
                a.Id,
                a.UserId,
                a.User.FullName,
                a.User.Nickname,
                a.User.AvatarUrl,
                a.AssignedAt))
            .ToList();

        var effectiveStatus = ResolveEffectiveStatus(task, DateTimeOffset.UtcNow);

        return new ProjectTaskResponse(
            task.Id,
            task.ProjectId,
            task.Title,
            task.Description,
            effectiveStatus,
            task.Deadline.ToUniversalTime(),
            task.CreatorId,
            task.Creator.FullName,
            task.CreatedAt,
            task.UpdatedAt,
            assignments);
    }

    public async Task<ProjectTaskListResponse> GetProjectTasksAsync(Guid projectId, Guid accountId, CancellationToken ct = default)
    {
        await EnsureProjectParticipantAsync(projectId, accountId, ct);

        var tasks = await db.ProjectTasks
            .AsNoTracking()
            .Include(t => t.Creator)
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .Where(t => t.ProjectId == projectId)
            .ToListAsync(ct);

        var orderedTasks = tasks
            .OrderBy(t => t.Deadline)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        return new ProjectTaskListResponse(projectId, orderedTasks.Count, orderedTasks.Select(Map).ToList());
    }

    public async Task<ProjectTaskResponse> GetProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, CancellationToken ct = default)
    {
        await EnsureProjectParticipantAsync(projectId, accountId, ct);

        var task = await db.ProjectTasks
            .Include(t => t.Creator)
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct);

        Require(task is not null, "PROJECT_TASK_NOT_FOUND", "Task not found.", 404);
        return Map(task);
    }

    public async Task<ProjectTaskResponse> CreateProjectTaskAsync(Guid projectId, Guid accountId, CreateProjectTaskRequest request, CancellationToken ct = default)
    {
        await EnsureProjectLeaderAsync(projectId, accountId, ct);

        ValidateTitle(request.Title);
        ValidateDescription(request.Description);
        ValidateStatus(request.Status);
        Require(request.Deadline != default, "INVALID_TASK_DEADLINE", "Task deadline is required.");

        var normalizedDeadline = NormalizeDeadline(request.Deadline);

        var creator = await GetProfileAsync(accountId, ct);
        var assignedUserIds = NormalizeUserIds(request.AssignedUserIds);
        await GetValidAssignedMembersAsync(projectId, assignedUserIds, ct);

        var task = new ProjectTask
        {
            ProjectId = projectId,
            CreatorId = creator.Id,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Status = request.Status,
            Deadline = normalizedDeadline,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.ProjectTasks.Add(task);
        await db.SaveChangesAsync(ct);

        if (assignedUserIds.Count > 0)
        {
            var assignments = assignedUserIds
                .Select(userId => new ProjectTaskAssignment
                {
                    ProjectTaskId = task.Id,
                    UserId = userId,
                    AssignedAt = DateTimeOffset.UtcNow
                })
                .ToList();

            db.ProjectTaskAssignments.AddRange(assignments);
            await db.SaveChangesAsync(ct);
        }

        var createdTask = await db.ProjectTasks
            .Include(t => t.Creator)
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .SingleAsync(t => t.Id == task.Id, ct);

        return Map(createdTask);
    }

    public async Task<ProjectTaskResponse> UpdateProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, UpdateProjectTaskRequest request, CancellationToken ct = default)
    {
        await EnsureProjectLeaderAsync(projectId, accountId, ct);

        var task = await db.ProjectTasks
            .Include(t => t.Creator)
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct);

        Require(task is not null, "PROJECT_TASK_NOT_FOUND", "Task not found.", 404);

        if (request.Title is not null)
            ValidateTitle(request.Title);
        if (request.Description is not null)
            ValidateDescription(request.Description);
        if (request.Status is not null)
        {
            ValidateStatus(request.Status.Value);
            if (IsTerminalStatus(task.Status) && task.Status != request.Status.Value)
                throw new ProjectException("INVALID_TASK_STATUS_TRANSITION", "Completed or cancelled tasks cannot be reopened or changed.", 400);
        }
        if (request.Deadline is not null)
            Require(request.Deadline != default, "INVALID_TASK_DEADLINE", "Task deadline is required.");

        if (request.Title is not null)
            task.Title = request.Title.Trim();
        if (request.Description is not null)
            task.Description = request.Description.Trim();
        if (request.Status is not null)
            task.Status = request.Status.Value;
        if (request.Deadline is not null)
            task.Deadline = NormalizeDeadline(request.Deadline.Value);

        task.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.AssignedUserIds is not null)
        {
            var desiredIds = NormalizeUserIds(request.AssignedUserIds);
            await GetValidAssignedMembersAsync(projectId, desiredIds, ct);

            var existingAssignments = await db.ProjectTaskAssignments
                .Where(a => a.ProjectTaskId == taskId)
                .ToListAsync(ct);

            foreach (var assignment in existingAssignments)
                db.ProjectTaskAssignments.Remove(assignment);

            if (desiredIds.Count > 0)
            {
                var replacements = desiredIds.Select(userId => new ProjectTaskAssignment
                {
                    ProjectTaskId = task.Id,
                    UserId = userId,
                    AssignedAt = DateTimeOffset.UtcNow
                }).ToList();

                db.ProjectTaskAssignments.AddRange(replacements);
            }
        }

        await db.SaveChangesAsync(ct);
        return Map(await db.ProjectTasks
            .Include(t => t.Creator)
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .SingleAsync(t => t.Id == taskId, ct));
    }

    public async Task DeleteProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, CancellationToken ct = default)
    {
        await EnsureProjectLeaderAsync(projectId, accountId, ct);

        var task = await db.ProjectTasks
            .SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct);

        Require(task is not null, "PROJECT_TASK_NOT_FOUND", "Task not found.", 404);

        db.ProjectTasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProjectTaskResponse> AssignUsersAsync(Guid projectId, Guid taskId, Guid accountId, List<Guid> userIds, CancellationToken ct = default)
    {
        await EnsureProjectLeaderAsync(projectId, accountId, ct);

        var task = await db.ProjectTasks
            .Include(t => t.Creator)
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct);

        Require(task is not null, "PROJECT_TASK_NOT_FOUND", "Task not found.", 404);

        var desiredIds = NormalizeUserIds(userIds);
        await GetValidAssignedMembersAsync(projectId, desiredIds, ct);

        var existingAssignments = await db.ProjectTaskAssignments
            .Where(a => a.ProjectTaskId == taskId)
            .ToListAsync(ct);

        foreach (var assignment in existingAssignments)
            db.ProjectTaskAssignments.Remove(assignment);

        if (desiredIds.Count > 0)
        {
            var replacements = desiredIds.Select(userId => new ProjectTaskAssignment
            {
                ProjectTaskId = task.Id,
                UserId = userId,
                AssignedAt = DateTimeOffset.UtcNow
            }).ToList();

            db.ProjectTaskAssignments.AddRange(replacements);
        }

        task.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var updatedTask = await db.ProjectTasks
            .Include(t => t.Creator)
            .Include(t => t.Assignments).ThenInclude(a => a.User)
            .SingleAsync(t => t.Id == taskId, ct);

        return Map(updatedTask);
    }

    public async Task<TaskReportListResponse> GetTaskReportsAsync(
        Guid projectId, Guid taskId, Guid accountId, int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        await EnsureProjectParticipantAsync(projectId, accountId, ct);
        await GetTaskInProjectAsync(projectId, taskId, ct);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.TaskReports.AsNoTracking()
            .Where(r => r.ProjectTaskId == taskId);
        var totalItems = await query.CountAsync(ct);
        var reports = await query
            .OrderBy(r => r.SubmittedById)
            .ThenBy(r => r.Version)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue))
            .Take(pageSize)
            .AsSplitQuery()
            .Include(r => r.SubmittedBy)
            .Include(r => r.Evidence)
            .Include(r => r.Reviews).ThenInclude(r => r.Reviewer)
            .ToListAsync(ct);

        return new TaskReportListResponse(
            taskId,
            page,
            pageSize,
            totalItems,
            (int)Math.Ceiling(totalItems / (double)pageSize),
            reports.Select(MapReport).ToList());
    }

    public async Task<TaskReportResponse> GetTaskReportAsync(
        Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default)
    {
        await EnsureProjectParticipantAsync(projectId, accountId, ct);
        await GetTaskInProjectAsync(projectId, taskId, ct);
        return MapReport(await GetTaskReportAsync(projectId, taskId, reportId, ct));
    }

    public async Task<TaskReportResponse> SubmitTaskReportAsync(
        Guid projectId, Guid taskId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default)
    {
        var submitter = await EnsureAssignedMemberAsync(projectId, taskId, accountId, ct);
        var (content, evidence) = ValidateReport(request, submitter.AccountId, configuration["Cloudinary:CloudName"]);
        var latestVersion = await db.TaskReports.AsNoTracking()
            .Where(r => r.ProjectTaskId == taskId && r.SubmittedById == submitter.Id)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);
        Require(latestVersion is null,
            "TASK_REPORT_ALREADY_SUBMITTED",
            "A new report may only be submitted once; use the revision endpoint after a revision request.",
            409);

        var now = DateTimeOffset.UtcNow;
        var report = new TaskReport
        {
            ProjectTaskId = taskId,
            SubmittedById = submitter.Id,
            Version = 1,
            Content = content,
            Status = TaskReportStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
            Evidence = evidence.Select(item => new TaskEvidence
            {
                FileUrl = item.FileUrl,
                PublicId = item.PublicId,
                ResourceType = item.ResourceType,
                FileName = item.FileName,
                Format = item.Format,
                CreatedAt = now
            }).ToList()
        };
        db.TaskReports.Add(report);
        await db.SaveChangesAsync(ct);

        return MapReport(await GetTaskReportAsync(projectId, taskId, report.Id, ct));
    }

    public async Task<TaskReportResponse> ReviseTaskReportAsync(
        Guid projectId,
        Guid taskId,
        Guid reportId,
        Guid accountId,
        SubmitTaskReportRequest request,
        CancellationToken ct = default)
    {
        var submitter = await EnsureAssignedMemberAsync(projectId, taskId, accountId, ct);
        var previous = await GetTaskReportAsync(projectId, taskId, reportId, ct);
        Require(previous.SubmittedById == submitter.Id,
            "TASK_REPORT_ACCESS_DENIED", "Only the report submitter can submit a revision.", 403);

        var latest = await db.TaskReports.AsNoTracking()
            .Where(r => r.ProjectTaskId == taskId && r.SubmittedById == submitter.Id)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);
        Require(latest?.Id == previous.Id && previous.Status == TaskReportStatus.RevisionRequested,
            "TASK_REPORT_REVISION_NOT_ALLOWED",
            "Only the latest report that requires revision can be revised.",
            409);

        var (content, evidence) = ValidateReport(request, submitter.AccountId, configuration["Cloudinary:CloudName"]);
        var now = DateTimeOffset.UtcNow;
        var revised = new TaskReport
        {
            ProjectTaskId = taskId,
            SubmittedById = submitter.Id,
            Version = previous.Version + 1,
            Content = content,
            Status = TaskReportStatus.Submitted,
            SubmittedAt = now,
            UpdatedAt = now,
            Evidence = evidence.Select(item => new TaskEvidence
            {
                FileUrl = item.FileUrl,
                PublicId = item.PublicId,
                ResourceType = item.ResourceType,
                FileName = item.FileName,
                Format = item.Format,
                CreatedAt = now
            }).ToList()
        };
        db.TaskReports.Add(revised);
        await db.SaveChangesAsync(ct);

        return MapReport(await GetTaskReportAsync(projectId, taskId, revised.Id, ct));
    }

    public async Task<TaskReportResponse> ReviewTaskReportAsync(
        Guid projectId,
        Guid taskId,
        Guid reportId,
        Guid accountId,
        ReviewTaskReportRequest request,
        CancellationToken ct = default)
    {
        await EnsureProjectLeaderAsync(projectId, accountId, ct);
        if (request is null || !Enum.IsDefined(request.Decision))
            throw new ProjectException("INVALID_TASK_REPORT_DECISION", "Report review decision is invalid.");

        var reviewer = await GetProfileAsync(accountId, ct);
        var report = await GetTaskReportAsync(projectId, taskId, reportId, ct);
        Require(report.SubmittedById != reviewer.Id,
            "TASK_REPORT_SELF_REVIEW", "A report submitter cannot review their own report.", 403);
        Require(report.Status == TaskReportStatus.Submitted,
            "TASK_REPORT_NOT_REVIEWABLE", "Only submitted reports can be reviewed.", 409);

        var feedback = request.Feedback?.Trim();
        if (request.Decision == TaskReportReviewDecision.RevisionRequested)
            Require(!string.IsNullOrWhiteSpace(feedback),
                "TASK_REPORT_FEEDBACK_REQUIRED", "Feedback is required when requesting a revision.");
        Require(feedback is null || feedback.Length <= 2000,
            "INVALID_TASK_REPORT_FEEDBACK", "Review feedback cannot exceed 2000 characters.");

        var now = DateTimeOffset.UtcNow;
        report.Status = request.Decision switch
        {
            TaskReportReviewDecision.Approved => TaskReportStatus.Approved,
            TaskReportReviewDecision.RevisionRequested => TaskReportStatus.RevisionRequested,
            _ => throw new ProjectException("INVALID_TASK_REPORT_DECISION", "Report review decision is invalid.")
        };
        report.UpdatedAt = now;
        db.TaskReportReviews.Add(new TaskReportReview
        {
            TaskReportId = report.Id,
            TaskReport = report,
            ReviewerId = reviewer.Id,
            Decision = request.Decision,
            Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback,
            ReviewedAt = now
        });
        await db.SaveChangesAsync(ct);

        return MapReport(await GetTaskReportAsync(projectId, taskId, reportId, ct));
    }
}
