using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Route("api/projects")]
public sealed class ProjectsController(IProjectService projects, AuthService auth, ProjectModerationService moderation) : ControllerBase
{
    [Authorize(Roles = "Student"), HttpPost("{id:guid}/moderation")]
    public async Task<IActionResult> SubmitModeration(Guid id, CancellationToken ct)
    {
        var result = await moderation.SubmitProjectAsync(id, AccountId, ct);
        return Ok(result);
    }

    [Authorize, HttpGet("{id:guid}/moderation")]
    public async Task<IActionResult> GetModeration(Guid id, CancellationToken ct)
    {
        var result = await moderation.GetLatestProjectModerationAsync(id, AccountId, ct);
        return Ok(result);
    }

    private Guid AccountId =>
        Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
                   throw new ProjectException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401));

    private AccountRole? GetUserRole()
    {
        var roleClaim = User.FindFirst("role");
        return roleClaim?.Value switch
        {
            "Student" => AccountRole.Student,
            "Partner" => AccountRole.Partner,
            _ => null
        };
    }

    /// <summary>
    /// Tạo dự án mới (chỉ Student được tạo)
    /// </summary>
    [Authorize(Roles = "Student"), HttpPost]
    public async Task<IActionResult> CreateProject(CreateProjectRequest request, CancellationToken ct)
    {
        try
        {
            var result = await projects.CreateProjectAsync(AccountId, request, ct);
            return CreatedAtAction(nameof(GetProjectDetails), new { id = result.Id }, result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Khám phá tất cả dự án ở trạng thái đang mở (Visibility=Public, RecruitmentStatus=Open, đã qua moderation)
    /// </summary>
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> DiscoverProjects(
        [FromQuery] string? keyword,
        [FromQuery] string? field,
        [FromQuery] string? skillNames,
        [FromQuery] RecruitmentStatus? recruitmentStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        try
        {
            var parsedSkillNames = skillNames?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var result = await projects.DiscoverPublicProjectsAsync(
                keyword, field, parsedSkillNames, recruitmentStatus, page, pageSize, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Xem chi tiết thông tin project
    /// </summary>
    [Authorize(Roles = "Student,Moderator"), HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProjectDetails(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await projects.GetProjectDetailsAsync(id, AccountId, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Cập nhật thông tin dự án (chỉ creator được cập nhật)
    /// </summary>
    [Authorize(Roles = "Student"), HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProject(Guid id, UpdateProjectRequest request, CancellationToken ct)
    {
        try
        {
            var result = await projects.UpdateProjectAsync(id, AccountId, request, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Xóa dự án (chỉ creator được xóa, điều kiện: Pending hoặc Expired + 0 members)
    /// </summary>
    [Authorize(Roles = "Student"), HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteProject(Guid id, CancellationToken ct)
    {
        try
        {
            await projects.DeleteProjectAsync(id, AccountId, ct);
            return NoContent();
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy những project của user (những project được tạo hoặc đang tham gia)
    /// </summary>
    [Authorize, HttpGet("me")]
    public async Task<IActionResult> GetUserProjects(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        try
        {
            var (projectsList, total) = await projects.ListUserProjectsAsync(AccountId, page, pageSize, ct);
            var totalPages = (total + pageSize - 1) / pageSize;
            return Ok(new
            {
                page,
                pageSize,
                totalItems = total,
                totalPages,
                projects = projectsList
            });
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }

    /// <summary>
    /// Lấy người tạo và các thành viên đang hoạt động của dự án
    /// </summary>
    [AllowAnonymous, HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetProjectMembers(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await projects.GetProjectMembersAsync(id, ct);
            return Ok(result);
        }
        catch (ProjectException ex)
        {
            return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message });
        }
    }
}
