using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniNet.API.Services;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/images")]
public sealed class CloundController(ICloudImageStorage images, UniNetDbContext db) : ControllerBase
{
    [HttpPost, RequestSizeLimit(UploadFileReader.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadFileReader.MaxRequestBytes)]
    public async Task<ActionResult<ImageUploadResult>> Upload(IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var owner)
            || !await db.Accounts.AnyAsync(a => a.Id == owner && a.Status == AccountStatus.Active, ct))
            throw new AuthException("UNAUTHORIZED", "Tài khoản không hợp lệ.", 401);
        var upload = await UploadFileReader.Read(file, ct);
        var publicId = upload.ResourceType == "image"
            ? $"uninet/images/{owner:N}/{Guid.NewGuid():N}"
            : $"uninet/evidence/{owner:N}/{Guid.NewGuid():N}.{upload.Format}";
        Response.Headers.CacheControl = "no-store";
        var result = await images.Upload(
            new(upload.Bytes, publicId, upload.Format, upload.ResourceType), ct);
        return Ok(result with { FileName = upload.FileName });
    }
}
