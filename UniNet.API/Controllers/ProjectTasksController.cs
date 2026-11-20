using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

// Keep old routes during client transition. There is no Task service, storage,
// upload or calculation path behind this compatibility boundary.
[ApiController, Authorize(Roles = "Student"), Route("api/projects/{projectId:guid}")]
[ProducesResponseType(StatusCodes.Status410Gone)]
public sealed class ProjectTasksController : ControllerBase
{
    private IActionResult Gone()
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out _))
            throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);
        var error = RetiredProjectFeatures.Gone();
        return StatusCode(error.Status, new { code = error.Code, message = error.Message });
    }

    [HttpGet("tasks")]
    public IActionResult GetProjectTasks(Guid projectId) => Gone();
    [HttpGet("tasks/{taskId:guid}")]
    public IActionResult GetProjectTask(Guid projectId, Guid taskId) => Gone();
    [HttpPost("tasks")]
    public IActionResult CreateProjectTask(Guid projectId, CreateProjectTaskRequest request) => Gone();
    [HttpPut("tasks/{taskId:guid}")]
    public IActionResult UpdateProjectTask(Guid projectId, Guid taskId, UpdateProjectTaskRequest request) => Gone();
    [HttpPatch("tasks/{taskId:guid}/status")]
    public IActionResult UpdateTaskStatus(Guid projectId, Guid taskId, UpdateProjectTaskStatusRequest request) => Gone();
    [HttpDelete("tasks/{taskId:guid}")]
    public IActionResult DeleteProjectTask(Guid projectId, Guid taskId) => Gone();
    [HttpPatch("tasks/{taskId:guid}/assignments")]
    public IActionResult AssignMembers(Guid projectId, Guid taskId, [FromBody] List<Guid> userIds) => Gone();
    [HttpPost("tasks/{taskId:guid}/reports")]
    public IActionResult SubmitTaskReport(Guid projectId, Guid taskId, SubmitTaskReportRequest request) => Gone();
    [HttpPost("tasks/{taskId:guid}/reports/drafts")]
    public IActionResult CreateTaskReportDraft(Guid projectId, Guid taskId, SubmitTaskReportRequest request) => Gone();
    [HttpGet("tasks/{taskId:guid}/reports")]
    public IActionResult GetTaskReports(Guid projectId, Guid taskId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10) => Gone();
    [HttpGet("tasks/{taskId:guid}/reports/{reportId:guid}")]
    public IActionResult GetTaskReport(Guid projectId, Guid taskId, Guid reportId) => Gone();
    [HttpPut("tasks/{taskId:guid}/reports/{reportId:guid}")]
    public IActionResult ReviseTaskReport(Guid projectId, Guid taskId, Guid reportId, SubmitTaskReportRequest request) => Gone();
    [HttpPut("tasks/{taskId:guid}/reports/{reportId:guid}/draft")]
    public IActionResult UpdateTaskReportDraft(Guid projectId, Guid taskId, Guid reportId, SubmitTaskReportRequest request) => Gone();
    [HttpPost("tasks/{taskId:guid}/reports/{reportId:guid}/submit")]
    public IActionResult SubmitTaskReportDraft(Guid projectId, Guid taskId, Guid reportId) => Gone();
    [HttpPost("tasks/{taskId:guid}/reports/{reportId:guid}/withdraw")]
    public IActionResult WithdrawTaskReport(Guid projectId, Guid taskId, Guid reportId) => Gone();
    [HttpDelete("tasks/{taskId:guid}/reports/{reportId:guid}")]
    public IActionResult DeleteTaskReportDraft(Guid projectId, Guid taskId, Guid reportId) => Gone();
    [HttpPost("tasks/{taskId:guid}/reports/{reportId:guid}/reviews")]
    public IActionResult ReviewTaskReport(Guid projectId, Guid taskId, Guid reportId, ReviewTaskReportRequest request) => Gone();
}
