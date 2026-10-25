using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/skills"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSkillsController(AdminSkillsService skills) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var actor) ? actor : throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);
    [HttpGet] public Task<AdminPage<AdminSkillItem>> List([FromQuery] AdminSkillQuery query, CancellationToken ct) => skills.ListAsync(Actor, query, ct);
    [HttpPost] public async Task<ActionResult<AdminSkillItem>> Create(SaveAdminSkill request, CancellationToken ct) => StatusCode(201, await skills.SaveAsync(Actor, null, request, ct));
    [HttpPut("{id:guid}")] public Task<AdminSkillItem> Update(Guid id, SaveAdminSkill request, CancellationToken ct) => skills.SaveAsync(Actor, id, request, ct);
}
