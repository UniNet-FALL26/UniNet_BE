namespace UniNet.Application.Interfaces;

public interface IProjectTaskService
{
    Task<ProjectTaskListResponse> GetProjectTasksAsync(Guid projectId, Guid accountId, CancellationToken ct = default);
    Task<ProjectTaskResponse> GetProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, CancellationToken ct = default);
    Task<ProjectTaskResponse> CreateProjectTaskAsync(Guid projectId, Guid accountId, CreateProjectTaskRequest request, CancellationToken ct = default);
    Task<ProjectTaskResponse> UpdateProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, UpdateProjectTaskRequest request, CancellationToken ct = default);
    Task DeleteProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, CancellationToken ct = default);
    Task<ProjectTaskResponse> AssignUsersAsync(Guid projectId, Guid taskId, Guid accountId, List<Guid> userIds, CancellationToken ct = default);
    Task<TaskReportListResponse> GetTaskReportsAsync(Guid projectId, Guid taskId, Guid accountId, int page = 1, int pageSize = 10, CancellationToken ct = default);
    Task<TaskReportResponse> GetTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default);
    Task<TaskReportResponse> SubmitTaskReportAsync(Guid projectId, Guid taskId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default);
    Task<TaskReportResponse> CreateTaskReportDraftAsync(Guid projectId, Guid taskId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default);
    Task<TaskReportResponse> UpdateTaskReportDraftAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default);
    Task<TaskReportResponse> SubmitTaskReportDraftAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default);
    Task<TaskReportResponse> WithdrawTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default);
    Task DeleteTaskReportDraftAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default);
    Task<TaskReportResponse> ReviseTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default);
    Task<TaskReportResponse> ReviewTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, ReviewTaskReportRequest request, CancellationToken ct = default);
}
