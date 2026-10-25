using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/notification-broadcasts")]
public sealed class NotificationBroadcastsController(NotificationBroadcastService service, UniNetDbContext db) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) && id != Guid.Empty ? id
        : throw new ProjectVerificationException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);
    [HttpGet("permissions")] public async Task<object> Permissions(CancellationToken ct) => new { canManage = await NotificationPermissionPolicy.HasAsync(db, AccountId, NotificationPermissionPolicy.Manage, ct) };
    [HttpGet] public Task<List<NotificationBroadcastDto>> Get(CancellationToken ct) => service.ListAsync(AccountId, ct);
    [HttpGet("{id:guid}")] public Task<NotificationBroadcastDto> GetOne(Guid id, CancellationToken ct) => service.GetAsync(AccountId, id, ct);
    [HttpPost] public Task<NotificationBroadcastDto> Create(CreateNotificationBroadcast request, CancellationToken ct) => service.CreateAsync(AccountId, request, ct);
    [HttpPost("{id:guid}/preview")] public async Task<object> Preview(Guid id, CancellationToken ct) => new { recipientCount = await service.PreviewAsync(AccountId, id, ct) };
    [HttpPost("{id:guid}/publish")] public async Task<IActionResult> Publish(Guid id, CancellationToken ct) => Accepted(await service.PublishAsync(AccountId, id, ct));
    [HttpPost("{id:guid}/cancel")] public Task<NotificationBroadcastDto> Cancel(Guid id, CancellationToken ct) => service.CancelAsync(AccountId, id, ct);
}
