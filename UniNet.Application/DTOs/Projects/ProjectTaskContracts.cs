using UniNet.Domain.Enums;

namespace UniNet.Application;

// Request-only compatibility contracts for authenticated HTTP 410 routes.
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

public record UpdateProjectTaskStatusRequest(ProjectTaskStatus Status);

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
