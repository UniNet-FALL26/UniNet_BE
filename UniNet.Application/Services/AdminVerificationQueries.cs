using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.DTOs.Verification;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record AdminVerificationQuery(VerificationStatus? Status = null, int Page = 1, int PageSize = 20, string? Search = null, string Sort = "submittedAt", bool Desc = true);
public sealed record AdminVerificationStats(int Total, int Pending, int Approved, int Rejected, int Cancelled);
public sealed record AdminVerificationItem(Guid Id, Guid UserId, Guid AccountId, string Email, string FullName, string? AvatarUrl,
    string? Phone, string? OrganizationName, PartnerType? PartnerType, string? SchoolName, string? StudentCode, VerificationStatus Status,
    DateTimeOffset SubmittedAt, DateTimeOffset? ReviewedAt, Guid? ReviewedBy, string? ReviewerName, StudentVerificationStatusResponse? Verification);
public sealed record AdminVerificationPage(int Page, int PageSize, int TotalItems, IReadOnlyList<AdminVerificationItem> Items);
public sealed record AdminVerificationDocument(int Index, string FileName, string? ContentType, bool Uploaded);
public sealed record AdminVerificationDetail(AdminVerificationItem Application, AdminPublicProfile Profile,
    IReadOnlyDictionary<string, string?> SubmittedData, IReadOnlyList<AdminVerificationDocument> Documents,
    IReadOnlyList<AdminVerificationHistory> History, string? RejectReason, bool IsVerified)
{
    // Preserve the existing student detail contract while adding administrative metadata.
    public Guid Id => Application.Id;
    public VerificationStatus Status => Application.Status;
    public string FullName => Application.FullName;
    public string? SchoolName => Application.SchoolName;
    public string? StudentCode => Application.StudentCode;
    public DateTimeOffset SubmittedAt => Application.SubmittedAt;
    public DateTimeOffset? ReviewedAt => Application.ReviewedAt;
    public string? DocumentName => Documents.FirstOrDefault()?.FileName;
}

public sealed class AdminVerificationQueries(UniNetDbContext db)
{
    public static string Permission(VerificationType type) => type == VerificationType.Student ? NotificationPermissionPolicy.StudentVerificationReview : NotificationPermissionPolicy.PartnerVerificationReview;
    public async Task<AdminVerificationPage> ListAsync(Guid actor, VerificationType type, AdminVerificationQuery request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, Permission(type), ct);
        AdminUsersService.ValidatePage(request.Page, request.PageSize);
        if (request.Status.HasValue && !Enum.IsDefined(request.Status.Value) || request.Search?.Length > 255) throw AdminUsersService.Invalid();
        var query = db.UserVerifications.AsNoTracking().Where(v => v.VerificationType == type);
        if (request.Status.HasValue) query = query.Where(v => v.Status == request.Status);
        if (!string.IsNullOrWhiteSpace(request.Search)) {
            var text = request.Search.Trim().ToLowerInvariant();
            // Search only named snapshot fields; never search or return document payloads.
            var snapshots = db.Database.IsNpgsql()
                ? db.UserVerifications.FromSqlInterpolated($"SELECT * FROM \"UserVerifications\" WHERE strpos(lower(concat_ws(' ', \"VerificationData\"->>'fullName', \"VerificationData\"->>'schoolName', \"VerificationData\"->>'studentCode', \"VerificationData\"->>'organizationName', \"VerificationData\"->>'representativeName')), {text}) > 0")
                : db.UserVerifications.FromSqlInterpolated($"SELECT * FROM \"UserVerifications\" WHERE instr(lower(coalesce(json_extract(\"VerificationData\", '$.fullName'),'') || ' ' || coalesce(json_extract(\"VerificationData\", '$.schoolName'),'') || ' ' || coalesce(json_extract(\"VerificationData\", '$.studentCode'),'') || ' ' || coalesce(json_extract(\"VerificationData\", '$.organizationName'),'') || ' ' || coalesce(json_extract(\"VerificationData\", '$.representativeName'),'')), {text}) > 0");
            query = query.Where(v => v.User.FullName.ToLower().Contains(text) || v.User.Account.Email.ToLower().Contains(text) || snapshots.Any(s => s.Id == v.Id));
        }
        var total = await query.CountAsync(ct);
        var ordered = request.Sort switch {
            "submittedAt" => request.Desc ? query.OrderByDescending(v => v.SubmittedAt) : query.OrderBy(v => v.SubmittedAt),
            "name" => request.Desc ? query.OrderByDescending(v => v.User.FullName) : query.OrderBy(v => v.User.FullName),
            _ => throw AdminUsersService.Invalid()
        };
        var rows = await WithPeople(ordered.ThenBy(v => v.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)).ToListAsync(ct);
        return new(request.Page, request.PageSize, total, rows.Select(Item).ToArray());
    }
    public async Task<AdminVerificationStats> StatsAsync(Guid actor, VerificationType type, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, Permission(type), ct);
        var groups = await db.UserVerifications.Where(v => v.VerificationType == type).GroupBy(v => v.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(VerificationStatus status) => groups.FirstOrDefault(g => g.Status == status)?.Count ?? 0;
        return new(groups.Sum(g => g.Count), Count(VerificationStatus.Pending), Count(VerificationStatus.Approved), Count(VerificationStatus.Rejected), Count(VerificationStatus.Cancelled));
    }
    public async Task<AdminVerificationDetail> DetailAsync(Guid actor, VerificationType type, Guid id, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, Permission(type), ct);
        var row = await WithPeople(db.UserVerifications.AsNoTracking()).SingleOrDefaultAsync(v => v.Id == id && v.VerificationType == type, ct) ?? throw Missing();
        var data = Fields(row.VerificationData);
        var documents = new List<AdminVerificationDocument>();
        if (type == VerificationType.Student) documents.Add(new(0, data.GetValueOrDefault("documentName") ?? "Tài liệu sinh viên", data.GetValueOrDefault("documentContentType"), true));
        else if (!string.IsNullOrWhiteSpace(row.DocumentUrls)) {
            using var json = JsonDocument.Parse(row.DocumentUrls);
            if (json.RootElement.ValueKind == JsonValueKind.Array) {
                var index = 0;
                foreach (var document in json.RootElement.EnumerateArray()) {
                    string? Value(string key) => document.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    documents.Add(new(index++, Value("originalFileName") ?? Value("fileName") ?? "Tài liệu đối tác", Value("contentType"), document.TryGetProperty("documentBase64", out _)));
                }
            }
        }
        var history = await db.UserVerifications.AsNoTracking().Where(v => v.UserId == row.UserId && v.VerificationType == type).OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.Id)
            .Select(v => new AdminVerificationHistory(v.Id, v.VerificationType, v.Status, v.SubmittedAt, v.ReviewedAt, v.RejectReason, v.ReviewedBy, v.Reviewer == null || v.Reviewer.Profile == null ? null : v.Reviewer.Profile.FullName)).ToListAsync(ct);
        var p = row.User;
        return new(Item(row), new(p.CoverUrl, p.Bio, p.Phone, p.UniversityName, p.Major, p.StudentCode, p.OrganizationName, p.PartnerType, p.Industry, p.Website, p.ContactEmail, p.Address), data, documents, history, row.RejectReason, p.IsVerified);
    }
    public async Task<string> ExternalDocumentAsync(Guid actor, Guid id, int index, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, Permission(VerificationType.Partner), ct);
        var value = await db.UserVerifications.AsNoTracking().Where(v => v.Id == id && v.VerificationType == VerificationType.Partner).Select(v => v.DocumentUrls).SingleOrDefaultAsync(ct);
        if (value == null || index < 0) throw Missing();
        using var json = JsonDocument.Parse(value);
        var documents = json.RootElement.EnumerateArray().ToArray();
        if (index >= documents.Length || !documents[index].TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw Missing();
        return uri.AbsoluteUri;
    }
    private static IQueryable<UserVerification> WithPeople(IQueryable<UserVerification> query) => query.Include(v => v.User).ThenInclude(u => u.Account).Include(v => v.Reviewer).ThenInclude(a => a!.Profile);
    private static Dictionary<string, string?> Fields(string json)
    {
        using var data = JsonDocument.Parse(json); var result = new Dictionary<string, string?>();
        foreach (var key in new[] { "fullName", "schoolName", "studentCode", "documentName", "documentContentType", "organizationName", "partnerType", "taxCode", "representativeName", "industry", "website", "contactEmail", "phone", "address" })
            if (data.RootElement.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Null) result[key] = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        return result;
    }
    private static AdminVerificationItem Item(UserVerification v) {
        var data = Fields(v.VerificationData); var p = v.User;
        return new(v.Id, v.UserId, p.AccountId, p.Account.Email, data.GetValueOrDefault("fullName") ?? p.FullName, p.AvatarUrl, data.GetValueOrDefault("phone") ?? p.Phone,
            data.GetValueOrDefault("organizationName") ?? p.OrganizationName, p.PartnerType, data.GetValueOrDefault("schoolName"), data.GetValueOrDefault("studentCode"), v.Status, v.SubmittedAt, v.ReviewedAt,
            v.ReviewedBy, v.Reviewer?.Profile?.FullName, v.VerificationType == VerificationType.Student ? new(v.Id, v.Status, p.IsVerified, data.GetValueOrDefault("fullName"), data.GetValueOrDefault("schoolName"), data.GetValueOrDefault("studentCode"), v.SubmittedAt, v.ReviewedAt, v.RejectReason, data.GetValueOrDefault("documentName")) : null);
    }
    private static AuthException Missing() => new("VERIFICATION_NOT_FOUND", "Không tìm thấy hồ sơ hoặc tài liệu xác minh.", 404);
}
