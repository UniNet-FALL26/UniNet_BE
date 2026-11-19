using Microsoft.Extensions.Configuration;
using UniNet.Domain.Enums;
using UniNet.Application.Interfaces;
using UniNet.Infrastructure.Data;
namespace UniNet.Application.Services;

public sealed class ProjectTaskService : IProjectTaskService
{
    public ProjectTaskService(UniNetDbContext db, IConfiguration configuration) { }
    public Task<ProjectTaskListResponse> GetProjectTasksAsync(Guid projectId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<ProjectTaskResponse> GetProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<ProjectTaskResponse> CreateProjectTaskAsync(Guid projectId, Guid accountId, CreateProjectTaskRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<ProjectTaskResponse> UpdateProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, UpdateProjectTaskRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<ProjectTaskResponse> UpdateTaskStatusAsync(Guid projectId, Guid taskId, Guid accountId, ProjectTaskStatus status, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task DeleteProjectTaskAsync(Guid projectId, Guid taskId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<ProjectTaskResponse> AssignUsersAsync(Guid projectId, Guid taskId, Guid accountId, List<Guid> userIds, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportListResponse> GetTaskReportsAsync(Guid projectId, Guid taskId, Guid accountId, int page = 1, int pageSize = 10, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> GetTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> SubmitTaskReportAsync(Guid projectId, Guid taskId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> CreateTaskReportDraftAsync(Guid projectId, Guid taskId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> UpdateTaskReportDraftAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> SubmitTaskReportDraftAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> WithdrawTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task DeleteTaskReportDraftAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> ReviseTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, SubmitTaskReportRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
    public Task<TaskReportResponse> ReviewTaskReportAsync(Guid projectId, Guid taskId, Guid reportId, Guid accountId, ReviewTaskReportRequest request, CancellationToken ct = default) => throw RetiredProjectFeatures.Gone();
}
