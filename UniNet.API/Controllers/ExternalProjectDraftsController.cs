using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/external-project-drafts")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExternalProjectDraftsController(ExternalProjectDraftService service) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) && id != Guid.Empty
        ? id : throw new ProjectException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpPost]
    public async Task<ActionResult<ExternalProjectDraftResponse>> Create(CancellationToken ct)
    {
        var draft = await service.CreateAsync(AccountId, ct);
        return CreatedAtAction(nameof(Get), new { id = draft.DraftId }, draft);
    }
    [HttpGet("{id:guid}")]
    public Task<ExternalProjectDraftResponse> Get(Guid id, CancellationToken ct) => service.GetAsync(id, AccountId, ct);
    [HttpPatch("{id:guid}")]
    public Task<ExternalProjectDraftResponse> Update(Guid id, UpdateExternalProjectDraftRequest request, CancellationToken ct) => service.UpdateAsync(id, AccountId, request, ct);
    [HttpPost("{id:guid}/confirm")]
    public Task<ConfirmExternalProjectDraftResponse> Confirm(Guid id, ConfirmExternalProjectDraftRequest request, CancellationToken ct) => service.ConfirmAsync(id, AccountId, request, ct);
}
