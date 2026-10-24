using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.DTOs.Verification;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class PartnerVerificationService(UniNetDbContext db)
{
    public const int MaximumDocumentBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PartnerVerificationStatusResponse> GetMyStatusAsync(Guid accountId, CancellationToken ct)
    {
        var profile = await LoadPartnerProfileAsync(accountId, ct);
        var latest = await LatestPartnerVerification(profile.Id).AsNoTracking().FirstOrDefaultAsync(ct);
        return latest is null
            ? new(null, null, null, null)
            : new(latest.Id, latest.Status, latest.RejectReason, latest.SubmittedAt);
    }

    public async Task<PartnerVerificationStatusResponse> SubmitAsync(Guid accountId, SubmitPartnerVerificationRequest request, CancellationToken ct)
    {
        ValidateSubmission(request);
        return await CreateAsync(accountId, request.TaxCode, request.RepresentativeName, request.Documents, ct);
    }

    public async Task<PartnerVerificationStatusResponse> SubmitFormAsync(Guid accountId,
        SubmitPartnerVerificationUploadRequest request, PartnerVerificationFile? document, CancellationToken ct)
    {
        if (request.TaxCode?.Length > 50) throw new AuthException("INVALID_VERIFICATION_DATA", "Mã số thuế tối đa 50 ký tự.");
        if (request.RepresentativeName?.Length > 255) throw new AuthException("INVALID_VERIFICATION_DATA", "Tên người đại diện tối đa 255 ký tự.");
        if (!Enum.IsDefined(request.Type) || string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Trim().Length > 255)
            throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Loại và tên tài liệu không hợp lệ; tên tối đa 255 ký tự.");

        var hasUrl = !string.IsNullOrWhiteSpace(request.Url);
        if ((document is null && !hasUrl) || (document is not null && hasUrl))
            throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Chọn một trong hai: đường dẫn HTTPS hoặc tải tệp lên.");

        var item = new Dictionary<string, object?>
        {
            ["type"] = request.Type,
            ["fileName"] = request.FileName.Trim()
        };
        if (document is null)
        {
            if (!IsHttpsUrl(request.Url!.Trim())) throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Tài liệu phải có URL HTTPS hợp lệ.");
            item["url"] = request.Url.Trim();
        }
        else
        {
            var contentType = ValidateUploadedDocument(document);
            item["url"] = null;
            item["originalFileName"] = Path.GetFileName(document.FileName.Replace('\\', '/'));
            item["contentType"] = contentType;
            item["length"] = document.Bytes.Length;
            item["documentBase64"] = Convert.ToBase64String(document.Bytes);
        }
        return await CreateAsync(accountId, request.TaxCode, request.RepresentativeName, new[] { item }, ct);
    }

    public async Task<PartnerVerificationStatusResponse> SubmitDocumentsAsync(Guid accountId, string? taxCode,
        string? representativeName, IReadOnlyList<PartnerVerificationUploadDocumentRequest> documents,
        IReadOnlyList<PartnerVerificationFile> files, CancellationToken ct)
    {
        if (taxCode?.Length > 50) throw new AuthException("INVALID_VERIFICATION_DATA", "Mã số thuế tối đa 50 ký tự.");
        if (representativeName?.Length > 255) throw new AuthException("INVALID_VERIFICATION_DATA", "Tên người đại diện tối đa 255 ký tự.");
        if (documents is null || documents.Count is < 1 or > 10)
            throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Cần gửi từ 1 đến 10 tài liệu xác minh.");

        var usedFiles = new HashSet<int>();
        var storedDocuments = new List<Dictionary<string, object?>>(documents.Count);
        foreach (var document in documents)
        {
            if (!Enum.IsDefined(document.Type) || string.IsNullOrWhiteSpace(document.FileName) || document.FileName.Trim().Length > 255)
                throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Loại và tên tài liệu không hợp lệ; tên tối đa 255 ký tự.");
            var hasUrl = !string.IsNullOrWhiteSpace(document.Url);
            var hasFile = document.FileIndex.HasValue;
            if (hasUrl == hasFile) throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Mỗi tài liệu cần có đúng một URL hoặc tệp tải lên.");

            var item = new Dictionary<string, object?> { ["type"] = document.Type, ["fileName"] = document.FileName.Trim() };
            if (hasUrl)
            {
                var url = document.Url!.Trim();
                if (!IsHttpsUrl(url)) throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Tài liệu phải có URL HTTPS hợp lệ.");
                item["url"] = url;
            }
            else
            {
                var index = document.FileIndex!.Value;
                if (index < 0 || index >= files.Count || !usedFiles.Add(index))
                    throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Danh sách tệp không khớp với tài liệu đã nhập.");
                var file = files[index];
                var contentType = ValidateUploadedDocument(file);
                item["url"] = null;
                item["originalFileName"] = Path.GetFileName(file.FileName.Replace('\\', '/'));
                item["contentType"] = contentType;
                item["length"] = file.Bytes.Length;
                item["documentBase64"] = Convert.ToBase64String(file.Bytes);
            }
            storedDocuments.Add(item);
        }
        if (usedFiles.Count != files.Count) throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Danh sách tệp không khớp với tài liệu đã nhập.");
        return await CreateAsync(accountId, taxCode, representativeName, storedDocuments, ct);
    }

    private async Task<PartnerVerificationStatusResponse> CreateAsync(Guid accountId, string? taxCode,
        string? representativeName, object documents, CancellationToken ct)
    {
        var profile = await LoadPartnerProfileAsync(accountId, ct);
        var latest = await LatestPartnerVerification(profile.Id).FirstOrDefaultAsync(ct);
        if (latest?.Status == VerificationStatus.Pending)
            throw new AuthException("VERIFICATION_ALREADY_PENDING", "Yêu cầu xác minh đang chờ admin duyệt.", 409);
        if (latest?.Status == VerificationStatus.Approved && profile.IsVerified)
            throw new AuthException("PARTNER_ALREADY_VERIFIED", "Hồ sơ Partner đã được xác minh.", 409);

        var now = DateTimeOffset.UtcNow;
        var verification = new UserVerification
        {
            UserId = profile.Id,
            VerificationType = VerificationType.Partner,
            Status = VerificationStatus.Pending,
            VerificationData = JsonSerializer.Serialize(new
            {
                organizationName = profile.OrganizationName,
                partnerType = profile.PartnerType?.ToString(),
                taxCode = Clean(taxCode),
                representativeName = Clean(representativeName),
                industry = profile.Industry,
                website = profile.Website,
                contactEmail = profile.ContactEmail,
                phone = profile.Phone,
                address = profile.Address
            }, JsonOptions),
            DocumentUrls = JsonSerializer.Serialize(documents, JsonOptions),
            SubmittedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        profile.IsVerified = false;
        profile.UpdatedAt = now;
        db.UserVerifications.Add(verification);
        await db.SaveChangesAsync(ct);
        return new(verification.Id, verification.Status, null, verification.SubmittedAt);
    }

    public async Task<PartnerVerificationFileContent> GetDocumentAsync(Guid adminAccountId, Guid verificationId, int documentIndex, CancellationToken ct)
    {
        var isAdmin = await db.Accounts.AnyAsync(x => x.Id == adminAccountId && x.Role == AccountRole.Admin && x.Status == AccountStatus.Active, ct);
        if (!isAdmin) throw new AuthException("FORBIDDEN", "Chỉ quản trị viên đang hoạt động mới được xem tài liệu.", 403);
        if (documentIndex < 0) throw new AuthException("VERIFICATION_DOCUMENT_NOT_FOUND", "Không tìm thấy tài liệu.", 404);
        var row = await db.UserVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == verificationId && x.VerificationType == VerificationType.Partner, ct)
            ?? throw new AuthException("VERIFICATION_NOT_FOUND", "Không tìm thấy hồ sơ xác minh.", 404);
        try
        {
            using var json = JsonDocument.Parse(row.DocumentUrls ?? "[]");
            var item = json.RootElement.EnumerateArray().ElementAtOrDefault(documentIndex);
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("documentBase64", out var payload) || payload.ValueKind != JsonValueKind.String)
                throw new AuthException("VERIFICATION_DOCUMENT_NOT_FOUND", "Không tìm thấy tệp trong hồ sơ.", 404);
            var name = item.TryGetProperty("originalFileName", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : "document.pdf";
            var contentType = item.TryGetProperty("contentType", out var typeValue) && typeValue.ValueKind == JsonValueKind.String ? typeValue.GetString() : "application/pdf";
            return new(Path.GetFileName((name ?? "document.pdf").Replace('\\', '/')), contentType ?? "application/pdf", Convert.FromBase64String(payload.GetString()!));
        }
        catch (JsonException) { throw new AuthException("INVALID_VERIFICATION_DATA", "Không thể đọc tài liệu xác minh.", 500); }
    }

    public async Task<PartnerVerificationQueueResponse> ListPendingAsync(int page, int pageSize, CancellationToken ct)
    {
        ValidatePage(page, pageSize);
        var query = db.UserVerifications.AsNoTracking()
            .Where(x => x.VerificationType == VerificationType.Partner && x.Status == VerificationStatus.Pending);
        var total = await query.CountAsync(ct);
        var rows = await query.Include(x => x.User).ThenInclude(x => x.Account)
            .OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, rows.Select(ToQueueItem).ToList());
    }

    public async Task<PartnerVerificationDetailResponse> GetPendingAsync(Guid verificationId, CancellationToken ct)
    {
        var verification = await LoadPendingAsync(verificationId, ct);
        return ToDetail(verification);
    }

    public async Task<PartnerVerificationDetailResponse> ApproveAsync(Guid reviewerAccountId, Guid verificationId, CancellationToken ct)
    {
        await EnsureActiveAdminAsync(reviewerAccountId, ct);
        var verification = await LoadPendingAsync(verificationId, ct);
        var now = DateTimeOffset.UtcNow;
        verification.Status = VerificationStatus.Approved;
        verification.RejectReason = null;
        verification.ReviewedBy = reviewerAccountId;
        verification.ReviewedAt = now;
        verification.UpdatedAt = now;
        verification.User.IsVerified = true;
        verification.User.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToDetail(verification);
    }

    public async Task<PartnerVerificationDetailResponse> RejectAsync(Guid reviewerAccountId, Guid verificationId, string reason, CancellationToken ct)
    {
        await EnsureActiveAdminAsync(reviewerAccountId, ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new AuthException("INVALID_REJECT_REASON", "Lý do từ chối bắt buộc và tối đa 2000 ký tự.");
        var verification = await LoadPendingAsync(verificationId, ct);
        var now = DateTimeOffset.UtcNow;
        verification.Status = VerificationStatus.Rejected;
        verification.RejectReason = reason.Trim();
        verification.ReviewedBy = reviewerAccountId;
        verification.ReviewedAt = now;
        verification.UpdatedAt = now;
        verification.User.IsVerified = false;
        verification.User.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToDetail(verification);
    }

    private IQueryable<UserVerification> LatestPartnerVerification(Guid userId) => db.UserVerifications
        .Where(x => x.UserId == userId && x.VerificationType == VerificationType.Partner)
        .OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.CreatedAt);

    private async Task<UserProfile> LoadPartnerProfileAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new AuthException("ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại.", 404);
        if (account.Role != AccountRole.Partner || account.Status != AccountStatus.Active || account.Profile is null)
            throw new AuthException("FORBIDDEN", "Chỉ Partner đang hoạt động mới dùng được chức năng xác minh.", 403);
        return account.Profile;
    }

    private async Task EnsureActiveAdminAsync(Guid accountId, CancellationToken ct)
    {
        var isAdmin = await db.Accounts.AnyAsync(x => x.Id == accountId && x.Role == AccountRole.Admin && x.Status == AccountStatus.Active, ct);
        if (!isAdmin) throw new AuthException("FORBIDDEN", "Chỉ admin đang hoạt động mới được duyệt xác minh.", 403);
    }

    private async Task<UserVerification> LoadPendingAsync(Guid id, CancellationToken ct)
        => await db.UserVerifications.Include(x => x.User).ThenInclude(x => x.Account)
            .SingleOrDefaultAsync(x => x.Id == id && x.VerificationType == VerificationType.Partner && x.Status == VerificationStatus.Pending, ct)
            ?? throw new AuthException("VERIFICATION_NOT_FOUND", "Không tìm thấy yêu cầu đang chờ duyệt.", 404);

    private static void ValidateSubmission(SubmitPartnerVerificationRequest request)
    {
        if (request.TaxCode?.Length > 50) throw new AuthException("INVALID_VERIFICATION_DATA", "Mã số thuế tối đa 50 ký tự.");
        if (request.RepresentativeName?.Length > 255) throw new AuthException("INVALID_VERIFICATION_DATA", "Tên người đại diện tối đa 255 ký tự.");
        if (request.Documents is null || request.Documents.Count is < 1 or > 10)
            throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Cần gửi từ 1 đến 10 tài liệu xác minh.");
        if (request.Documents.Any(x => x is null || !Enum.IsDefined(x.Type) || string.IsNullOrWhiteSpace(x.FileName) || x.FileName.Trim().Length > 255 || string.IsNullOrWhiteSpace(x.Url) || !IsHttpsUrl(x.Url)))
            throw new AuthException("INVALID_VERIFICATION_DOCUMENTS", "Tài liệu phải có loại hợp lệ, tên tối đa 255 ký tự và URL HTTPS hợp lệ.");
    }

    private static string ValidateUploadedDocument(PartnerVerificationFile document)
    {
        var bytes = document.Bytes;
        if (bytes.Length is < 1 or > MaximumDocumentBytes) throw new AuthException("INVALID_VERIFICATION_DOCUMENT", "Tài liệu phải có dung lượng từ 1 byte đến 5 MB.");
        var extension = Path.GetExtension(document.FileName.Replace('\\', '/')).ToLowerInvariant();
        var expectedType = extension switch { ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".pdf" => "application/pdf", _ => null };
        var signatureValid = extension switch
        {
            ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            ".png" => bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4e && bytes[3] == 0x47 && bytes[4] == 0x0d && bytes[5] == 0x0a && bytes[6] == 0x1a && bytes[7] == 0x0a,
            ".pdf" => bytes.Length >= 5 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2d,
            _ => false
        };
        var typeMatches = expectedType is not null && (document.ContentType.Equals(expectedType, StringComparison.OrdinalIgnoreCase) || document.ContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase));
        if (!signatureValid || !typeMatches) throw new AuthException("INVALID_VERIFICATION_DOCUMENT", "Chỉ chấp nhận ảnh JPG, PNG hoặc tài liệu PDF hợp lệ, tối đa 5 MB.");
        return expectedType!;
    }

    private static bool IsHttpsUrl(string value)
        => value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1) throw new AuthException("INVALID_PAGE", "Page phải lớn hơn hoặc bằng 1.");
        if (pageSize is < 1 or > 100) throw new AuthException("INVALID_PAGE_SIZE", "Page size phải từ 1 đến 100.");
    }

    private static PartnerVerificationQueueItem ToQueueItem(UserVerification x)
        => new(x.Id, x.UserId, x.User.Account.Email, x.User.OrganizationName ?? x.User.FullName, x.User.PartnerType, x.Status, x.SubmittedAt);

    private static PartnerVerificationDetailResponse ToDetail(UserVerification x)
        => new(x.Id, x.UserId, x.User.Account.Email, x.User.OrganizationName ?? x.User.FullName, x.User.PartnerType,
            x.Status, x.VerificationData, WithoutPrivateDocumentBytes(x.DocumentUrls), x.RejectReason, x.SubmittedAt, x.ReviewedAt);

    private static string? WithoutPrivateDocumentBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        try
        {
            var node = JsonNode.Parse(value);
            if (node is JsonArray documents)
                foreach (var item in documents)
                    if (item is JsonObject document) document.Remove("documentBase64");
            return node?.ToJsonString(JsonOptions);
        }
        catch (JsonException) { return value; }
    }
}
