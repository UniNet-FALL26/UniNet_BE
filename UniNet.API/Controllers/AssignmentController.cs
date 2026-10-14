using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/assignments")]
public sealed class AssignmentController(IAssignmentService assignments) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        && id != Guid.Empty ? id : throw new AssignmentException("UNAUTHORIZED", "Invalid login session.", 401);

    [HttpGet, Authorize(Roles = "Admin")]
    public Task<AssignmentsResponse> GetAllAssignments([FromQuery] VerificationAssignmentStatus? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => assignments.GetAllAssignmentsAsync(AccountId, status, page, pageSize, ct);

    [HttpGet("me"), Authorize(Roles = "Verificator")]
    public Task<AssignmentsResponse> GetMyAssignments([FromQuery] VerificationAssignmentStatus? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => assignments.GetMyAssignmentsAsync(AccountId, status, page, pageSize, ct);

    [HttpGet("{id:guid}"), Authorize(Roles = "Admin,Verificator")]
    public Task<AssignmentDetailResponse> GetAssignment(Guid id, CancellationToken ct)
        => assignments.GetDetailAsync(id, AccountId, ct);

    [HttpPut("{id:guid}"), Authorize(Roles = "Admin")]
    public Task<AssignmentResponse> UpdateAssignment(Guid id, [FromBody] UpdateAssignmentRequest request,
        CancellationToken ct)
        => assignments.UpdateAsync(id, AccountId, request, ct);

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<ActionResult<AssignmentResponse>> CreateAssignment([FromBody] CreateAssignmentRequest request,
        CancellationToken ct)
    {
        var assignment = await assignments.CreateAsync(AccountId, request, ct);
        return CreatedAtAction(nameof(GetAssignment), new { id = assignment.Id }, assignment);
    }

    [HttpPost("{id:guid}/replace"), Authorize(Roles = "Admin")]
    public async Task<ActionResult<AssignmentResponse>> ReplaceVerificator(Guid id,
        [FromBody] ReplaceVerificatorRequest request, CancellationToken ct)
    {
        var assignment = await assignments.ReplaceVerificatorAsync(id, AccountId, request, ct);
        return CreatedAtAction(nameof(GetAssignment), new { id = assignment.Id }, assignment);
    }

    [HttpGet("availability"), Authorize(Roles = "Admin")]
    public Task<List<SuitableVerificatorResponse>> GetAvailability([FromQuery] Guid projectVerificationId,
        CancellationToken ct)
        => assignments.GetSuitableVerificatorsAsync(projectVerificationId, AccountId, ct);
}
