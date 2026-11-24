using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AssessorContractService(
    UniNetDbContext db, IAssessorContractFiles files,
    IOptions<AssessorContractOptions> options, TimeProvider clock, AssessorOnboardingEmailQueue emails)
{
    private readonly AssessorContractOptions settings = options.Value;

    public async Task<object> Inspect(string session, CancellationToken ct)
    {
        var row = await ByContractToken(session, ct);
        using var document = PdfDocument.Open(await files.Read(row.ContractStorageKey!, row.ContractSha256!, ct));
        var pages = document.GetPages().Select(ExtractContractPage).ToArray();
        await MarkViewed(row, ct);
        return new { row.FullName, row.TemplateVersion, row.ContractSha256, row.SignatureStatement,
            row.SignedAt, row.SignedName, row.ContractTokenExpiresAt, pages };
    }

    internal static string ExtractContractPage(UglyToad.PdfPig.Content.Page page)
    {
        var text = ContentOrderTextExtractor.GetText(page,
            new ContentOrderTextExtractor.Options { NegativeGapAsWhitespace = false });
        // PDF extraction can insert a gap before a combining Vietnamese tone mark.
        return Regex.Replace(text, @"(?<=\p{L}|\p{M})[ \t]+(?=\p{M})", "")
            .Normalize(NormalizationForm.FormC);
    }

    public async Task<byte[]> Pdf(string session, CancellationToken ct)
    {
        var row = await ByContractToken(session, ct);
        var bytes = await files.Export(row, ct);
        await MarkViewed(row, ct);
        return bytes;
    }

    private async Task MarkViewed(AssessorOnboarding row, CancellationToken ct)
    {
        if (row.ContractViewedAt != null || row.Status != AssessorOnboardingStatus.AwaitingSignature) return;
        row.ContractViewedAt = clock.GetUtcNow();
        await Save(row, ct);
    }

    public async Task<object> Sign(SignAssessorContract request, CancellationToken ct)
    {
        var row = await ByContractToken(request.Token, ct);
        var signedName = request.SignedName.Trim();
        if (row.Status != AssessorOnboardingStatus.AwaitingSignature) throw Unavailable();
        if (!request.Accepted || row.ContractViewedAt is null || !string.Equals(signedName, row.FullName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.ContractSha256, row.ContractSha256, StringComparison.OrdinalIgnoreCase))
            throw new AuthException("SIGNATURE_INVALID", "Vui lòng xác nhận đúng bản hợp đồng và nhập tên ký.", 400);
        var now = clock.GetUtcNow();
        row.SignedName = signedName;
        row.SignedAt = now;
        row.Status = AssessorOnboardingStatus.AwaitingApproval;
        row.EmailConfirmedAt = now;
        row.AutoApprove = false;
        row.AutoApproveAt = null;
        row.OtpHash = null;
        row.ReadSessionHash = null;
        row.ReadSessionExpiresAt = null;
        await Save(row, ct);
        return new { status = "AwaitingApproval", row.SignedName, row.SignedAt };
    }

    public async Task Resend(Guid actor, Guid id, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(x => x.Id == actor && x.Role == AccountRole.Admin && x.Status == AccountStatus.Active, ct))
            throw new AuthException("FORBIDDEN", "Chỉ Admin đang hoạt động mới được cấp lại link.", 403);
        var row = await db.AssessorOnboardings.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row?.Status != AssessorOnboardingStatus.AwaitingSignature)
            throw Unavailable();
        if (row.ContractTokenExpiresAt > clock.GetUtcNow().AddDays(3).AddMinutes(-1))
            throw new AuthException("RATE_LIMITED", "Vui lòng chờ trước khi cấp lại link.", 429);
        QueueContractLink(row);
        await Save(row, ct);
    }

    public void QueueContractLink(AssessorOnboarding row)
    {
        EnsureEnabled();
        var token = AssessorContractSecrets.Token();
        var now = clock.GetUtcNow();
        SetContractToken(row, token, now);
        var link = settings.PublicBaseUrl.TrimEnd('/') + "/assessor-onboarding/contract.html#token=" + token;
        Queue(row, "Contract", row.ContractTokenHash!, now.AddDays(3),
            new(row.EmailNormalized, "UniNet - Hợp đồng thẩm định viên",
                $"Vui lòng mở đường dẫn để đọc và xác nhận hợp đồng. Link có hiệu lực 3 ngày.\n\n{link}"));
    }

    private async Task<AssessorOnboarding> ByContractToken(string token, CancellationToken ct)
    {
        EnsureEnabled();
        var hash = AssessorContractSecrets.Hash(token);
        var row = await db.AssessorOnboardings.SingleOrDefaultAsync(x => x.ContractTokenHash == hash, ct);
        if (row is null || !CanRead(row) ||
            row.ContractTokenExpiresAt is null || row.ContractTokenExpiresAt <= clock.GetUtcNow())
            throw Unavailable();
        return row;
    }

    internal static bool CanRead(AssessorOnboarding row) => row.Status is
        AssessorOnboardingStatus.AwaitingSignature or AssessorOnboardingStatus.AwaitingApproval or
        AssessorOnboardingStatus.AwaitingPassword or AssessorOnboardingStatus.Activated;

    internal static void SetContractToken(AssessorOnboarding row, string token, DateTimeOffset now)
    {
        row.ContractTokenHash = AssessorContractSecrets.Hash(token);
        row.ContractTokenExpiresAt = now.AddDays(3);
        row.ReadSessionHash = null;
        row.ReadSessionExpiresAt = null;
        row.ContractViewedAt = null;
        row.OtpHash = null;
    }

    internal static async Task PrepareDocument(AssessorOnboarding row, ContractProfile profile,
        IAssessorContractFiles files, AssessorContractOptions settings, CancellationToken ct)
    {
        var document = await files.Generate(row, profile, ct);
        row.ContractStorageKey = document.Key;
        row.ContractSha256 = document.Sha256;
        row.TemplateVersion = settings.TemplateVersion;
        row.SignatureStatement = settings.SignatureStatement;
        row.GenerationErrorCode = null;
        row.GenerationRetryAt = null;
        row.Status = AssessorOnboardingStatus.AwaitingSignature;
    }

    private void Queue(AssessorOnboarding row, string kind, string credentialHash, DateTimeOffset expiry, AssessorEmailPayload payload)
    {
        emails.Add(row.Id, kind, credentialHash, expiry, payload);
    }

    private async Task Save(AssessorOnboarding row, CancellationToken ct)
    {
        row.Revision = Guid.NewGuid();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) {
            throw new AuthException("ONBOARDING_CHANGED", "Hồ sơ vừa được cập nhật. Vui lòng thử lại.", 409);
        }
    }

    private void EnsureEnabled()
    {
        if (!settings.Enabled) throw new AuthException("CONTRACTS_DISABLED", "Chức năng hợp đồng chưa được cấu hình.", 503);
    }

    private static AuthException Unavailable() => new("CONTRACT_UNAVAILABLE", "Hợp đồng hoặc phiên truy cập đã hết hiệu lực.", 410);
}
