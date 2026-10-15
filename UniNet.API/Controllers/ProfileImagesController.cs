using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using UniNet.API.Services;

namespace UniNet.API.Controllers;

[ApiController, Route("api/profile/images")]
public sealed class ProfileImagesController(ProfileImageStorage localImages, ICloudImageStorage cloudImages, UniNetDbContext db) : ControllerBase
{
    [Authorize, HttpPost, RequestSizeLimit(ProfileImageStorage.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProfileImageStorage.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var owner)
            || !await db.Accounts.AnyAsync(a => a.Id == owner && a.Status == AccountStatus.Active, ct))
            throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);
        var image = await ImageFileReader.Read(file, ct);
        var publicId = $"uninet/profile-images/{owner:N}/{Guid.NewGuid():N}";
        var result = await cloudImages.Upload(new(image.Bytes, publicId, image.Format, "image"), ct);
        return Ok(new { url = result.ImageUrl });
    }

    [AllowAnonymous, HttpGet("{name}")]
    public IActionResult Read(string name)
    {
        var path = localImages.Find(name);
        if (path is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return PhysicalFile(path, ProfileImageStorage.ContentType(name));
    }
}
