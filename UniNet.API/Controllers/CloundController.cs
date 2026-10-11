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
    [HttpPost, RequestSizeLimit(ImageFileReader.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImageFileReader.MaxBytes + 65536)]
    public async Task<ActionResult<ImageUploadResult>> Upload(IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var owner)
            || !await db.Accounts.AnyAsync(a => a.Id == owner && a.Status == AccountStatus.Active, ct))
            throw new AuthException("UNAUTHORIZED", "Tài khoản không hợp lệ.", 401);
        var image = await ImageFileReader.Read(file, ct);
        var publicId = $"uninet/images/{owner:N}/{Guid.NewGuid():N}";
        Response.Headers.CacheControl = "no-store";
        return Ok(await images.Upload(new(image.Bytes, publicId, image.Format), ct));
    }
}
