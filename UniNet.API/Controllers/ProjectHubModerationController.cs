using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin,Moderator"), Route("api/project-hub")]
public sealed class ProjectHubModerationController(ProjectModerationService moderation, UniNetDbContext db) : ControllerBase
{
    private const ProjectModerationPurpose Purpose = ProjectModerationPurpose.ProjectHubSubmission;

    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new ProjectException("UNAUTHORIZED", "Invalid authentication session.", 401);

    [HttpGet("pending")]
    public async Task<PendingProjectModerationsResponse> Pending(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
    {
        await NotificationPermissionPolicy.RequireAsync(db, AccountId, NotificationPermissionPolicy.ProjectReview, ct);
        return await moderation.GetPendingProjectsAsync(page, pageSize, ct, Purpose);
    }

    [HttpPost("{projectId:guid}/approve")]
    public Task<ProjectModerationResponse> Approve(Guid projectId, ReviewProjectRequest request, CancellationToken ct)
        => moderation.ApproveProjectAsync(projectId, AccountId, request, ct, Purpose);

    [HttpPost("{projectId:guid}/reject")]
    public Task<ProjectModerationResponse> Reject(Guid projectId, ReviewProjectRequest request, CancellationToken ct)
        => moderation.RejectProjectAsync(projectId, AccountId, request, ct, Purpose);
}
