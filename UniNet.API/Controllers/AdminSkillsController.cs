using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Application.Interfaces;
using UniNet.API.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin/skills"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminSkillsController(AdminSkillsService skills) : ControllerBase
{
    private Guid Actor => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var actor) ? actor : throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);
    [HttpGet] public Task<AdminPage<AdminSkillItem>> List([FromQuery] AdminSkillQuery query, CancellationToken ct) => skills.ListAsync(Actor, query, ct);
    [HttpPost] public async Task<ActionResult<AdminSkillItem>> Create(SaveAdminSkill request, CancellationToken ct) => StatusCode(201, await skills.SaveAsync(Actor, null, request, ct));
    [HttpPut("{id:guid}")] public Task<AdminSkillItem> Update(Guid id, SaveAdminSkill request, CancellationToken ct) => skills.SaveAsync(Actor, id, request, ct);

    [HttpPost("icons"), Consumes("multipart/form-data"), RequestSizeLimit(ImageFileReader.MaxBytes + 65536), RequestFormLimits(MultipartBodyLengthLimit = ImageFileReader.MaxBytes + 65536)]
    public async Task<ActionResult<ImageUploadResult>> UploadIcon(IFormFile file, [FromServices] ICloudImageStorage cloud, CancellationToken ct)
    {
        await skills.RequireAdmin(Actor, ct);
        var image = await ImageFileReader.Read(file, ct);
        var png = SkillIconImages.FitToCanvas(image.Bytes);
        return Ok(await cloud.Upload(new(png, $"uninet/skills/{Actor:N}/{Guid.NewGuid():N}", "png"), ct));
    }

    [HttpPost("icons/remove-background"), Consumes("multipart/form-data"), RequestSizeLimit(ImageFileReader.MaxBytes + 65536), RequestFormLimits(MultipartBodyLengthLimit = ImageFileReader.MaxBytes + 65536)]
    public async Task<IActionResult> RemoveIconBackground(IFormFile file, [FromServices] OnnxPortraitProcessor processor, CancellationToken ct)
    {
        await skills.RequireAdmin(Actor, ct);
        var image = await ImageFileReader.Read(file, ct);
        return File(SkillIconImages.FitToCanvas(await SkillIconImages.RemoveBackground(image.Bytes, processor, ct)), "image/png");
    }

    [HttpPost("icons/preview"), Consumes("multipart/form-data"), RequestSizeLimit(ImageFileReader.MaxBytes + 65536), RequestFormLimits(MultipartBodyLengthLimit = ImageFileReader.MaxBytes + 65536)]
    public async Task<IActionResult> PreviewIcon(IFormFile file, CancellationToken ct)
    {
        await skills.RequireAdmin(Actor, ct);
        var image = await ImageFileReader.Read(file, ct);
        return File(SkillIconImages.FitToCanvas(image.Bytes), "image/png");
    }
}
