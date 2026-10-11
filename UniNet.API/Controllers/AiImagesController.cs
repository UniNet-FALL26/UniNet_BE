using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.API.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/ai-images")]
public sealed class AiImagesController(PortraitSessions sessions, UniNetDbContext db, IPortraitCloudStorage cloud) : ControllerBase
{
    private async Task<Guid> Owner(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            || !await db.Accounts.AnyAsync(a => a.Id == id && a.Role == AccountRole.Student && a.Status == AccountStatus.Active, ct))
            throw new AuthException("UNAUTHORIZED", "Tài khoản không hợp lệ.", 401);
        return id;
    }
    [HttpPost("remove-background"), RequestSizeLimit(ProfileImageStorage.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProfileImageStorage.MaxBytes + 65536)]
    public async Task<IActionResult> Process(IFormFile file, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await sessions.Process(await Owner(ct), file, ct);
        return Ok(new { success = true, data = result });
    }
    public sealed record ConfirmRequest(Guid SessionId);
    [HttpPost("confirm"), RequestSizeLimit(1024)]
    public async Task<IActionResult> Confirm(ConfirmRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { success = true, data = await sessions.Confirm(await Owner(ct), request.SessionId, db, cloud, ct) });
    }
    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> Release(Guid id, CancellationToken ct)
    {
        sessions.Release(await Owner(ct), id); return NoContent();
    }
}
