using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/project-hub")]
public sealed class ProjectHubController(ProjectHubService projects) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) && id != Guid.Empty
        ? id : throw new ProjectException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet("mine")]
    public Task<HubPage> Mine([FromQuery] int page = 1, [FromQuery] int pageSize = 12, [FromQuery] string? q = null, CancellationToken ct = default)
        => projects.MineAsync(AccountId, page, pageSize, q, ct);
    [HttpGet("discover")]
    public Task<HubPage> Discover([FromQuery] int page = 1, [FromQuery] int pageSize = 12, [FromQuery] string? q = null, CancellationToken ct = default)
        => projects.DiscoverAsync(AccountId, page, pageSize, q, ct);
    [HttpGet("people")]
    public Task<HubItems<HubPerson>> People([FromQuery] string? q = null, CancellationToken ct = default)
        => projects.PeopleAsync(AccountId, q, ct);
    [HttpGet("{id:guid}")]
    public Task<HubProject> Get(Guid id, CancellationToken ct) => projects.GetAsync(id, AccountId, ct);
    [HttpPost]
    public async Task<ActionResult<HubProject>> Create(CreateHubProjectRequest request, CancellationToken ct)
    {
        var project = await projects.CreateAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = project.Id }, project);
    }
    [HttpPatch("{id:guid}")]
    public Task<HubProject> Update(Guid id, UpdateHubProjectRequest request, CancellationToken ct)
        => projects.UpdateAsync(id, AccountId, request, ct);
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    { await projects.DeleteAsync(id, AccountId, ct); return NoContent(); }
    [HttpPost("{id:guid}/resources/{kind}")]
    public Task<HubProject> AddResource(Guid id, string kind, HubFile request, CancellationToken ct)
        => projects.AddResourceAsync(id, AccountId, kind, request, ct);
    [HttpPut("{id:guid}/recruitment")]
    public Task<HubProject> Recruitment(Guid id, SaveHubRecruitmentRequest request, CancellationToken ct)
        => projects.RecruitmentAsync(id, AccountId, request, ct);
    [HttpGet("{id:guid}/requests")]
    public Task<HubItems<HubRequest>> Requests(Guid id, CancellationToken ct) => projects.RequestsAsync(id, AccountId, ct);
    [HttpGet("requests/mine")]
    public Task<HubItems<HubRequest>> MineRequests(CancellationToken ct, [FromQuery] Guid? projectId = null) => projects.MyRequestsAsync(AccountId, ct, projectId);
    [HttpPost("{id:guid}/requests")]
    public Task<HubRequest> SendRequest(Guid id, CreateHubRequest request, CancellationToken ct)
        => projects.RequestAsync(id, AccountId, request, ct);
    [HttpPost("requests/{requestId:guid}/decision")]
    public Task<HubRequest> Decide(Guid requestId, HubDecisionRequest request, CancellationToken ct)
        => projects.DecideAsync(requestId, AccountId, request, ct);
    [HttpPatch("{id:guid}/members/{userId:guid}")]
    public Task<HubProject> Member(Guid id, Guid userId, UpdateHubMemberRequest request, CancellationToken ct)
        => projects.MemberAsync(id, userId, AccountId, request, ct);
    [HttpPut("{id:guid}/review")]
    public Task<HubProject> Review(Guid id, SaveHubReviewRequest request, CancellationToken ct)
        => projects.ReviewAsync(id, AccountId, request, ct);
    [HttpPost("{id:guid}/complete")]
    public Task<HubProject> Complete(Guid id, CancellationToken ct) => projects.CompleteAsync(id, AccountId, ct);
}
