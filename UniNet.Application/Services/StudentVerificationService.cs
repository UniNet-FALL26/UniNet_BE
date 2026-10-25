using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.DTOs.Verification;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class StudentVerificationService(UniNetDbContext db)
{
    public const int MaximumDocumentBytes = 5 * 1024 * 1024;
    public const int ResubmitCooldownSeconds = 60;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    // Private document bytes live in the existing JSON column; never serialize this snapshot to API clients.
    private sealed record Snapshot(string FullName, string SchoolName, string StudentCode,
        string DocumentName, string DocumentContentType, string DocumentBase64);

    public async Task<StudentVerificationStatusResponse> GetMyStatusAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await StudentAsync(accountId, ct);
        var latest = await Latest(profile.Id).AsNoTracking().FirstOrDefaultAsync(ct);
        return ToStatus(latest, profile.IsVerified, currentAccountStatus: true);
    }

    public async Task<StudentVerificationStatusResponse> SubmitAsync(Guid accountId, SubmitStudentVerificationRequest request,
        StudentVerificationDocument document, CancellationToken ct)
    {
        Validate(request, document);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync(accountId, ct);
        var profile = await StudentAsync(accountId, ct);
        var latest = await Latest(profile.Id).FirstOrDefaultAsync(ct);
        if (profile.IsVerified)
            throw Error("STUDENT_ALREADY_VERIFIED", "Tài khoản sinh viên đã được xác minh.", 409);
        if (await db.UserVerifications.AnyAsync(x => x.UserId == profile.Id && x.VerificationType == VerificationType.Student && x.Status == VerificationStatus.Pending, ct))
            throw Error("VERIFICATION_ALREADY_PENDING", "Yêu cầu xác minh đang chờ xét duyệt.", 409);
        var now = DateTimeOffset.UtcNow;
        if (latest is not null && now - latest.SubmittedAt < TimeSpan.FromSeconds(ResubmitCooldownSeconds))
            throw Error("VERIFICATION_RATE_LIMITED", "Vui lòng chờ 60 giây giữa các lần gửi yêu cầu.", 429);
        var row = new UserVerification
        {
            UserId = profile.Id, VerificationType = VerificationType.Student, Status = VerificationStatus.Pending,
            VerificationData = JsonSerializer.Serialize(new Snapshot(request.FullName.Trim(), request.SchoolName.Trim(), request.StudentCode.Trim(),
                Path.GetFileName(document.FileName.Replace('\\', '/')), document.ContentType.ToLowerInvariant(), Convert.ToBase64String(document.Bytes)), JsonOptions),
            SubmittedAt = now, CreatedAt = now, UpdatedAt = now
        };
        db.UserVerifications.Add(row);
        await NotificationService.EnqueuePermissionAsync(db, NotificationPermissionPolicy.StudentVerificationReview,
            new("verification.student.submitted", "Verification", "Hồ sơ sinh viên chờ xác minh",
                "Có yêu cầu xác minh sinh viên mới đang chờ xét duyệt.", "UserVerification", row.Id,
                EventKey: $"student-verification:{row.Id}:submitted", Audience: "Permission",
                RequiredPermission: NotificationPermissionPolicy.StudentVerificationReview, SenderUserId: accountId), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToStatus(row, profile.IsVerified);
    }

    public async Task<StudentVerificationQueueResponse> ListAsync(Guid adminId, VerificationStatus? status, int page, int pageSize, CancellationToken ct)
    {
        await AdminAsync(adminId, ct);
        if (page < 1 || pageSize is < 1 or > 100 || (status.HasValue && !Enum.IsDefined(status.Value)))
            throw Error("INVALID_PAGE", "Bộ lọc hoặc phân trang không hợp lệ.");
        var query = db.UserVerifications.AsNoTracking().Where(x => x.VerificationType == VerificationType.Student);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        var count = await query.CountAsync(ct);
        var rows = await query.Include(x => x.User).ThenInclude(x => x.Account).OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, count, rows.Select(x => new StudentVerificationQueueItem(x.Id, x.UserId, x.User.Account.Email, ToStatus(x, x.User.IsVerified))).ToArray());
    }

    public async Task<StudentVerificationStatusResponse> GetDetailAsync(Guid adminId, Guid id, CancellationToken ct)
    {
        await AdminAsync(adminId, ct);
        var row = await FindAsync(id, ct);
        return ToStatus(row, row.User.IsVerified);
    }

    public async Task<StudentVerificationStatusResponse> ReviewAsync(Guid adminId, Guid id, bool approve, string? reason, CancellationToken ct)
    {
        await AdminAsync(adminId, ct);
        if (!approve && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000))
            throw Error("INVALID_REJECT_REASON", "Lý do từ chối bắt buộc và tối đa 2000 ký tự.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var ownerId = await db.UserVerifications.Where(x => x.Id == id && x.VerificationType == VerificationType.Student)
            .Select(x => (Guid?)x.User.AccountId).SingleOrDefaultAsync(ct) ?? throw NotFound();
        await LockAsync(ownerId, ct);
        var row = await FindAsync(id, ct);
        // Conditional database update makes a second review fail even if an older instance was tracked.
        var now = DateTimeOffset.UtcNow;
        var status = approve ? VerificationStatus.Approved : VerificationStatus.Rejected;
        var changed = await db.UserVerifications.Where(x => x.Id == id && x.Status == VerificationStatus.Pending && x.User.Account.Status == AccountStatus.Active)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.Status, status).SetProperty(x => x.ReviewedBy, (Guid?)adminId)
                .SetProperty(x => x.ReviewedAt, (DateTimeOffset?)now).SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.RejectReason, approve ? null : reason!.Trim()), ct);
        if (changed != 1) throw Error("VERIFICATION_ALREADY_REVIEWED", "Yêu cầu đã được xét duyệt.", 409);
        await db.Entry(row).ReloadAsync(ct);
        var snapshot = Read(row);
        if (approve)
        {
            // Publish exactly the identity the admin reviewed, even if the editable profile changed while pending.
            await db.UserProfiles.Where(x => x.Id == row.UserId).ExecuteUpdateAsync(set => set
                .SetProperty(x => x.IsVerified, true).SetProperty(x => x.FullName, snapshot.FullName)
                .SetProperty(x => x.UniversityName, snapshot.SchoolName).SetProperty(x => x.StudentCode, snapshot.StudentCode)
                .SetProperty(x => x.UpdatedAt, now), ct);
        }
        else
            await db.UserProfiles.Where(x => x.Id == row.UserId).ExecuteUpdateAsync(set => set
                .SetProperty(x => x.IsVerified, false).SetProperty(x => x.UpdatedAt, now), ct);
        await db.Entry(row.User).ReloadAsync(ct);
        var action = approve ? "approved" : "rejected";
        await NotificationService.EnqueueAsync(db, [ownerId],
            new($"verification.student.{action}", "Verification", approve ? "Xác minh tài khoản thành công" : "Yêu cầu xác minh bị từ chối",
                approve ? "Tài khoản sinh viên của bạn đã được xác minh." : $"Yêu cầu xác minh bị từ chối: {reason!.Trim()}",
                "UserVerification", row.Id, EventKey: $"student-verification:{row.Id}:{action}", SenderUserId: adminId), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return ToStatus(row, approve);
    }

    public async Task<StudentVerificationDocument> GetDocumentAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accountId && x.Status == AccountStatus.Active, ct);
        if (account is null) throw Error("FORBIDDEN", "Không có quyền xem tài liệu.", 403);
        var row = await FindAsync(id, ct);
        if (row.User.AccountId != accountId) await AdminAsync(accountId, ct);
        var data = Read(row);
        return new(data.DocumentName, data.DocumentContentType, Convert.FromBase64String(data.DocumentBase64));
    }

    private async Task LockAsync(Guid accountId, CancellationToken ct)
    {
        // Transaction advisory locks synchronize every backend instance on PostgreSQL, without schema changes.
        // SQLite's write transaction serializes test writes; no process-local semaphore is relied on in production.
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({accountId.ToString()}, 0))", ct);
    }

    private IQueryable<UserVerification> Latest(Guid userId) => db.UserVerifications.Where(x => x.UserId == userId && x.VerificationType == VerificationType.Student)
        .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
    private async Task<UserVerification> FindAsync(Guid id, CancellationToken ct) =>
        await db.UserVerifications.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id && x.VerificationType == VerificationType.Student, ct) ?? throw NotFound();
    private async Task<UserProfile> StudentAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Id == accountId, ct);
        if (account?.Role != AccountRole.Student || account.Status != AccountStatus.Active || account.Profile is null)
            throw Error("FORBIDDEN", "Chỉ tài khoản sinh viên đang hoạt động được sử dụng chức năng này.", 403);
        return account.Profile;
    }
    private async Task AdminAsync(Guid accountId, CancellationToken ct)
    {
        if (!await NotificationPermissionPolicy.HasAsync(db, accountId, NotificationPermissionPolicy.StudentVerificationReview, ct))
            throw Error("FORBIDDEN", "Bạn không có quyền xét duyệt xác minh sinh viên.", 403);
    }
    private static Snapshot Read(UserVerification row) => JsonSerializer.Deserialize<Snapshot>(row.VerificationData, JsonOptions)
        ?? throw Error("INVALID_VERIFICATION_DATA", "Không thể đọc dữ liệu xác minh.", 500);
    private static StudentVerificationStatusResponse ToStatus(UserVerification? row, bool verified, bool currentAccountStatus = false)
    {
        if (row is null) return new(null, null, verified, null, null, null, null, null, null, null);
        var data = Read(row);
        // Existing profile edits can invalidate an earlier approval; keep its history but restore the verification CTA.
        var status = currentAccountStatus && row.Status == VerificationStatus.Approved && !verified ? (VerificationStatus?)null : row.Status;
        return new(row.Id, status, verified, data.FullName, data.SchoolName, data.StudentCode,
            row.SubmittedAt, row.ReviewedAt, row.RejectReason, data.DocumentName);
    }
    private static void Validate(SubmitStudentVerificationRequest request, StudentVerificationDocument document)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length is < 2 or > 255 ||
            string.IsNullOrWhiteSpace(request.SchoolName) || request.SchoolName.Trim().Length is < 2 or > 255 ||
            string.IsNullOrWhiteSpace(request.StudentCode) || !Regex.IsMatch(request.StudentCode.Trim(), @"\A[\p{L}\p{N}._/\-]{3,50}\z"))
            throw Error("INVALID_VERIFICATION_DATA", "Nhập họ tên, trường (2–255 ký tự) và mã sinh viên hợp lệ (3–50 chữ, số, dấu chấm, gạch dưới, gạch chéo hoặc gạch ngang).");
        if (document.Bytes.Length is < 1 or > MaximumDocumentBytes)
            throw Error("INVALID_VERIFICATION_DOCUMENT", "Tài liệu phải có dung lượng từ 1 byte đến 5MB.");
        if (string.IsNullOrWhiteSpace(document.FileName) || document.FileName.Length > 255 || document.FileName.Any(char.IsControl))
            throw Error("INVALID_VERIFICATION_DOCUMENT", "Tên tài liệu không hợp lệ.");
        var bytes = document.Bytes.AsSpan();
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        var valid = (document.ContentType.ToLowerInvariant(), extension) switch
        {
            ("image/jpeg", ".jpg" or ".jpeg") => bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
            ("image/png", ".png") => bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ("application/pdf", ".pdf") => bytes.StartsWith("%PDF-"u8),
            _ => false
        };
        if (!valid) throw Error("INVALID_VERIFICATION_DOCUMENT", "Chỉ hỗ trợ tài liệu JPG, PNG hoặc PDF đúng định dạng.");
    }
    private static AuthException Error(string code, string message, int status = 400) => new(code, message, status);
    private static AuthException NotFound() => Error("VERIFICATION_NOT_FOUND", "Không tìm thấy yêu cầu xác minh.", 404);
}
