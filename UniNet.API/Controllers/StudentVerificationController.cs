using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.DTOs.Verification;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student,Admin,Moderator,Verificator"), Route("api/student/verification"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentVerificationController(StudentVerificationService verifications) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet("me"), Authorize(Roles = "Student")]
    public Task<StudentVerificationStatusResponse> MyStatus(CancellationToken ct) => verifications.GetMyStatusAsync(AccountId, ct);

    [HttpPost, Authorize(Roles = "Student"), Consumes("multipart/form-data"), RequestSizeLimit(6 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<StudentVerificationStatusResponse> Submit([FromForm] StudentVerificationForm form, CancellationToken ct)
    {
        if (form.Document.Length is < 1 or > StudentVerificationService.MaximumDocumentBytes)
            throw new AuthException("INVALID_VERIFICATION_DOCUMENT", "Tài liệu phải có dung lượng từ 1 byte đến 5MB.");
        using var buffer = new MemoryStream();
        await form.Document.CopyToAsync(buffer, ct);
        return await verifications.SubmitAsync(AccountId, new(form.FullName, form.SchoolName, form.StudentCode),
            new(form.Document.FileName, form.Document.ContentType, buffer.ToArray()), ct);
    }

    [HttpGet("{verificationId:guid}/document"), Authorize(Roles = "Student,Admin,Moderator,Verificator")]
    public async Task<IActionResult> Document(Guid verificationId, CancellationToken ct)
    {
        var document = await verifications.GetDocumentAsync(AccountId, verificationId, ct);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(document.Bytes, document.ContentType, document.FileName);
    }
}

public sealed class StudentVerificationForm
{
    [Required, StringLength(255, MinimumLength = 2)] public string FullName { get; set; } = "";
    [Required, StringLength(255, MinimumLength = 2)] public string SchoolName { get; set; } = "";
    [Required, StringLength(50, MinimumLength = 3)] public string StudentCode { get; set; } = "";
    [Required] public IFormFile Document { get; set; } = null!;
}

[ApiController, Authorize(Roles = "Admin,Moderator,Verificator"), Route("api/admin/student-verifications"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminStudentVerificationsController(StudentVerificationService verifications, UniNetDbContext db) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    [HttpGet]
    public Task<AdminVerificationPage> List([FromQuery] AdminVerificationQuery query, CancellationToken ct) => new AdminVerificationQueries(db).ListAsync(AccountId, VerificationType.Student, query, ct);
    [HttpGet("stats")]
    public Task<AdminVerificationStats> Stats(CancellationToken ct) => new AdminVerificationQueries(db).StatsAsync(AccountId, VerificationType.Student, ct);
    [HttpGet("{verificationId:guid}")]
    public Task<AdminVerificationDetail> Detail(Guid verificationId, CancellationToken ct) => new AdminVerificationQueries(db).DetailAsync(AccountId, VerificationType.Student, verificationId, ct);
    [HttpPost("{verificationId:guid}/approve")]
    public Task<StudentVerificationStatusResponse> Approve(Guid verificationId, CancellationToken ct) => verifications.ReviewAsync(AccountId, verificationId, true, null, ct);
    [HttpPost("{verificationId:guid}/reject")]
    public Task<StudentVerificationStatusResponse> Reject(Guid verificationId, RejectStudentVerificationRequest request, CancellationToken ct)
        => verifications.ReviewAsync(AccountId, verificationId, false, request.Reason, ct);
}
