using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController, Route("api/projects/{projectId:guid}")]
public sealed class ProjectTasksController(IProjectTaskService tasks) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);

    [Authorize(Roles = "Student"), HttpGet("tasks")]
    public Task<ProjectTaskListResponse> GetProjectTasks(Guid projectId, CancellationToken ct)
        => tasks.GetProjectTasksAsync(projectId, AccountId, ct);

    [Authorize(Roles = "Student"), HttpGet("tasks/{taskId:guid}")]
    public Task<ProjectTaskResponse> GetProjectTask(Guid projectId, Guid taskId, CancellationToken ct)
        => tasks.GetProjectTaskAsync(projectId, taskId, AccountId, ct);

    [Authorize(Roles = "Student"), HttpPost("tasks")]
    public async Task<IActionResult> CreateProjectTask(Guid projectId, CreateProjectTaskRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.CreateProjectTaskAsync(projectId, AccountId, request, ct);
            return CreatedAtAction(nameof(GetProjectTask), new { projectId, taskId = result.Id }, result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPut("tasks/{taskId:guid}")]
    public async Task<IActionResult> UpdateProjectTask(Guid projectId, Guid taskId, UpdateProjectTaskRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.UpdateProjectTaskAsync(projectId, taskId, AccountId, request, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPatch("tasks/{taskId:guid}/status")]
    public async Task<IActionResult> UpdateTaskStatus(
        Guid projectId, Guid taskId, UpdateProjectTaskStatusRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.UpdateTaskStatusAsync(
                projectId, taskId, AccountId, request.Status, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpDelete("tasks/{taskId:guid}")]
    public async Task<IActionResult> DeleteProjectTask(Guid projectId, Guid taskId, CancellationToken ct)
    {
        try
        {
            await tasks.DeleteProjectTaskAsync(projectId, taskId, AccountId, ct);
            return NoContent();
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPatch("tasks/{taskId:guid}/assignments")]
    public async Task<IActionResult> AssignMembers(Guid projectId, Guid taskId, [FromBody] List<Guid> userIds, CancellationToken ct)
    {
        try
        {
            var result = await tasks.AssignUsersAsync(projectId, taskId, AccountId, userIds, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPost("tasks/{taskId:guid}/reports")]
    public async Task<IActionResult> SubmitTaskReport(
        Guid projectId, Guid taskId, SubmitTaskReportRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.SubmitTaskReportAsync(projectId, taskId, AccountId, request, ct);
            return CreatedAtAction(
                nameof(GetTaskReport),
                new { projectId, taskId, reportId = result.Id },
                result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPost("tasks/{taskId:guid}/reports/drafts")]
    public async Task<IActionResult> CreateTaskReportDraft(
        Guid projectId, Guid taskId, SubmitTaskReportRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.CreateTaskReportDraftAsync(projectId, taskId, AccountId, request, ct);
            return CreatedAtAction(
                nameof(GetTaskReport),
                new { projectId, taskId, reportId = result.Id },
                result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpGet("tasks/{taskId:guid}/reports")]
    public Task<TaskReportListResponse> GetTaskReports(
        Guid projectId, Guid taskId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => tasks.GetTaskReportsAsync(projectId, taskId, AccountId, page, pageSize, ct);

    [Authorize(Roles = "Student"), HttpGet("tasks/{taskId:guid}/reports/{reportId:guid}")]
    public Task<TaskReportResponse> GetTaskReport(
        Guid projectId, Guid taskId, Guid reportId, CancellationToken ct)
        => tasks.GetTaskReportAsync(projectId, taskId, reportId, AccountId, ct);

    [Authorize(Roles = "Student"), HttpPut("tasks/{taskId:guid}/reports/{reportId:guid}")]
    public async Task<IActionResult> ReviseTaskReport(
        Guid projectId, Guid taskId, Guid reportId, SubmitTaskReportRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.ReviseTaskReportAsync(projectId, taskId, reportId, AccountId, request, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPut("tasks/{taskId:guid}/reports/{reportId:guid}/draft")]
    public async Task<IActionResult> UpdateTaskReportDraft(
        Guid projectId, Guid taskId, Guid reportId, SubmitTaskReportRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.UpdateTaskReportDraftAsync(
                projectId, taskId, reportId, AccountId, request, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPost("tasks/{taskId:guid}/reports/{reportId:guid}/submit")]
    public async Task<IActionResult> SubmitTaskReportDraft(
        Guid projectId, Guid taskId, Guid reportId, CancellationToken ct)
    {
        try
        {
            var result = await tasks.SubmitTaskReportDraftAsync(
                projectId, taskId, reportId, AccountId, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPost("tasks/{taskId:guid}/reports/{reportId:guid}/withdraw")]
    public async Task<IActionResult> WithdrawTaskReport(
        Guid projectId, Guid taskId, Guid reportId, CancellationToken ct)
    {
        try
        {
            var result = await tasks.WithdrawTaskReportAsync(
                projectId, taskId, reportId, AccountId, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpDelete("tasks/{taskId:guid}/reports/{reportId:guid}")]
    public async Task<IActionResult> DeleteTaskReportDraft(
        Guid projectId, Guid taskId, Guid reportId, CancellationToken ct)
    {
        try
        {
            await tasks.DeleteTaskReportDraftAsync(projectId, taskId, reportId, AccountId, ct);
            return NoContent();
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    [Authorize(Roles = "Student"), HttpPost("tasks/{taskId:guid}/reports/{reportId:guid}/reviews")]
    public async Task<IActionResult> ReviewTaskReport(
        Guid projectId, Guid taskId, Guid reportId, ReviewTaskReportRequest request, CancellationToken ct)
    {
        try
        {
            var result = await tasks.ReviewTaskReportAsync(projectId, taskId, reportId, AccountId, request, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }
}
