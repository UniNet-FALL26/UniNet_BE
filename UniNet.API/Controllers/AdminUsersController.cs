using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin,Moderator,Verificator"), Route("api/admin/users"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminUsersController(AdminUsersService users) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    [HttpGet] public Task<AdminPage<AdminUserItem>> List([FromQuery] AdminUserQuery query, CancellationToken ct) => users.ListAsync(Actor, query, ct);
    [HttpGet("stats")] public Task<AdminUserStats> Stats(CancellationToken ct) => users.StatsAsync(Actor, ct);
    [HttpGet("{id:guid}")] public Task<AdminUserDetail> Detail(Guid id, CancellationToken ct) => users.DetailAsync(Actor, id, ct);
    [HttpPost("{id:guid}/lock")] public Task<AdminUserItem> Lock(Guid id, ChangeAccountStatusRequest request, CancellationToken ct) => users.ChangeStatusAsync(Actor, id, true, request.Reason, ct);
    [HttpPost("{id:guid}/unlock")] public Task<AdminUserItem> Unlock(Guid id, ChangeAccountStatusRequest request, CancellationToken ct) => users.ChangeStatusAsync(Actor, id, false, request.Reason, ct);
}

[ApiController, Authorize(Roles = "Admin,Moderator,Verificator"), Route("api/admin/management"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminManagementController(AdminPermissionService permissions) : ControllerBase
{
    [HttpGet] public Task<AdminCapabilities> Capabilities(CancellationToken ct) => permissions.CapabilitiesAsync(Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value), ct);
}

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/permissions"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPermissionsController(AdminPermissionService permissions, ILogger<AdminPermissionsController> logger) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    [HttpGet("{id:guid}")] public Task<List<AdminPermissionEntry>> Get(Guid id, CancellationToken ct) => permissions.GetAsync(Actor, id, ct);
    [HttpPut("{id:guid}")] public async Task<IActionResult> Set(Guid id, PermissionAssignment[] request, CancellationToken ct) {
        await permissions.SetManyAsync(Actor, id, request, ct);
        foreach (var change in request) logger.LogInformation("Admin {Actor} changed permission {Permission} for staff {Target} to {Granted}", Actor, change.Permission, id, change.Granted);
        return NoContent();
    }
}
