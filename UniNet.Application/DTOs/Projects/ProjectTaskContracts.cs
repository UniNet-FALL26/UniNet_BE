using UniNet.Domain.Enums;

namespace UniNet.Application;

public record CreateProjectTaskRequest(
    string Title,
    string Description,
    DateTimeOffset Deadline,
    ProjectTaskStatus Status = ProjectTaskStatus.ToDo,
    List<Guid>? AssignedUserIds = null
);

public record UpdateProjectTaskRequest(
    string? Title = null,
    string? Description = null,
    DateTimeOffset? Deadline = null,
    ProjectTaskStatus? Status = null,
    List<Guid>? AssignedUserIds = null
);

public record ProjectTaskAssignmentInfo(
    Guid AssignmentId,
    Guid UserId,
    string FullName,
    string Nickname,
    string? AvatarUrl,
    DateTimeOffset AssignedAt
);

public record ProjectTaskResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Description,
    ProjectTaskStatus Status,
    DateTimeOffset Deadline,
    Guid CreatorId,
    string CreatorFullName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<ProjectTaskAssignmentInfo> AssignedUsers
);

public record ProjectTaskListResponse(
    Guid ProjectId,
    int TotalTasks,
    List<ProjectTaskResponse> Tasks
);

public record TaskEvidenceRequest(
    string FileUrl,
    string? PublicId = null,
    string? ResourceType = null,
    string? FileName = null,
    string? Format = null);

public record SubmitTaskReportRequest(
    string Content,
    List<TaskEvidenceRequest> Evidence
);

public record ReviewTaskReportRequest(
    TaskReportReviewDecision Decision,
    string? Feedback = null
);

public record TaskEvidenceResponse(
    Guid Id,
    string FileUrl,
    DateTimeOffset CreatedAt,
    string? PublicId = null,
    string? ResourceType = null,
    string? FileName = null,
    string? Format = null
);

public record TaskReportReviewResponse(
    Guid Id,
    Guid ReviewerId,
    string ReviewerFullName,
    TaskReportReviewDecision Decision,
    string? Feedback,
    DateTimeOffset ReviewedAt
);

public record TaskReportResponse(
    Guid Id,
    Guid ProjectTaskId,
    Guid SubmittedById,
    string SubmittedByFullName,
    Guid ReportGroupId,
    Guid? ParentReportId,
    int Version,
    string Content,
    TaskReportStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset UpdatedAt,
    List<TaskEvidenceResponse> Evidence,
    List<TaskReportReviewResponse> Reviews
);

public record TaskReportListResponse(
    Guid ProjectTaskId,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages,
    List<TaskReportResponse> Reports
);
