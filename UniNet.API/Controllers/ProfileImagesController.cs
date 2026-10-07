using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.API.Services;

namespace UniNet.API.Controllers;

[ApiController, Route("api/profile/images")]
public sealed class ProfileImagesController(ProfileImageStorage images) : ControllerBase
{
    [Authorize, HttpPost, RequestSizeLimit(ProfileImageStorage.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProfileImageStorage.MaxBytes + 65536)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out _))
            throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);
        var name = await images.Save(file, ct);
        return Ok(new { url = Url.Action(nameof(Read), "ProfileImages", new { name }, Request.Scheme) });
    }

    [AllowAnonymous, HttpGet("{name}")]
    public IActionResult Read(string name)
    {
        var path = images.Find(name);
        if (path is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return PhysicalFile(path, ProfileImageStorage.ContentType(name));
    }
}
