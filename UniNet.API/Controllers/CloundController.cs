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
public sealed class CloundController(
    ICloudImageStorage images,
    UniNetDbContext db,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost, RequestSizeLimit(UploadFileReader.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadFileReader.MaxRequestBytes)]
    public async Task<ActionResult<ImageUploadResult>> Upload(IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var owner)
            || !await db.Accounts.AnyAsync(a => a.Id == owner && a.Status == AccountStatus.Active, ct))
            throw new AuthException("UNAUTHORIZED", "Tài khoản không hợp lệ.", 401);
        var maxVideoBytes = UploadFileReader.GetMaxVideoBytes(configuration);
        var upload = await UploadFileReader.Read(file, ct, maxVideoBytes);
        var fileId = Guid.NewGuid().ToString("N");
        var publicId = upload.ResourceType switch
        {
            "image" => $"uninet/images/{owner:N}/{fileId}",
            "raw" => $"uninet/evidence/{owner:N}/{fileId}.{upload.Format}",
            "video" => $"uninet/evidence/{owner:N}/{fileId}",
            _ => throw new InvalidOperationException("Unsupported upload resource type.")
        };
        Response.Headers.CacheControl = "no-store";
        var result = await images.Upload(
            new(upload.Bytes, publicId, upload.Format, upload.ResourceType), ct);
        return Ok(result with { FileName = upload.FileName });
    }
}
