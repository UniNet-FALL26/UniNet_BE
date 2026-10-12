using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController, Route("api/projects")]
public sealed class ProjectMemberController(IProjectMemberService members) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var accountId)
        ? accountId
        : throw new ProjectMemberException("UNAUTHORIZED", "Invalid login session.", 401);

    [AllowAnonymous, HttpGet("{projectId:guid}/members")]
    public Task<ProjectMembersListResponse> GetProjectMembers(Guid projectId, CancellationToken ct)
        => members.GetProjectMembersAsync(projectId, ct);

    [Authorize(Roles = "Student"), HttpGet("{projectId:guid}/recommended-members")]
    public Task<List<RecommendedProjectMemberResponse>> GetRecommendedMembers(Guid projectId, CancellationToken ct)
        => members.GetRecommendedMembersAsync(projectId, AccountId, ct);

    [Authorize(Roles = "Student"), HttpPatch("{projectId:guid}/members/{userId:guid}/kick")]
    public Task<ProjectMemberResponse> Kick(Guid projectId, Guid userId, CancellationToken ct)
        => members.KickAsync(projectId, userId, AccountId, ct);

    [Authorize(Roles = "Student"), HttpPatch("{projectId:guid}/leave")]
    public Task<ProjectMemberResponse> Leave(Guid projectId, CancellationToken ct)
        => members.LeaveAsync(projectId, AccountId, ct);
}
