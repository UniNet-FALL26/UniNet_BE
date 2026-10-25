using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/notifications")]
public sealed class NotificationsController(NotificationService service, NotificationTargets targets) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) && id != Guid.Empty
        ? id : throw new ProjectVerificationException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);
    [HttpGet] public Task<NotificationPage> Get([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] bool unreadOnly = false, [FromQuery] string? category = null, CancellationToken ct = default) => service.GetMineAsync(AccountId, page, pageSize, unreadOnly, category, ct);
    [HttpGet("unread-count")] public async Task<object> Count(CancellationToken ct) => new { count = await service.UnreadCountAsync(AccountId, ct) };
    [HttpPatch("{id:guid}/read")] public async Task<IActionResult> Read(Guid id, CancellationToken ct) { await service.ReadAsync(AccountId, id, ct); return NoContent(); }
    [HttpPatch("read-all")] public async Task<IActionResult> ReadAll(CancellationToken ct) { await service.ReadAsync(AccountId, null, ct); return NoContent(); }
    [HttpGet("{id:guid}/target")] public async Task<object> Target(Guid id, CancellationToken ct) => new { targetUrl = await targets.ResolveAsync(AccountId, id, ct) };
}
