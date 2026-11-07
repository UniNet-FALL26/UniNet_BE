using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AssessorContractService(
    UniNetDbContext db, AssessorContractSecrets secrets, IAssessorContractFiles files,
    IOptions<AssessorContractOptions> options, TimeProvider clock)
{
    private readonly AssessorContractOptions settings = options.Value;

    public async Task RequestReadOtp(string token, CancellationToken ct)
    {
        var row = await ByContractToken(token, ct);
        await IssueOtp(row, "Read", ct);
    }

    public async Task<string> VerifyReadOtp(string token, string code, CancellationToken ct)
    {
        var row = await ByContractToken(token, ct);
        await CheckOtp(row, "Read", code, ct);
        var session = AssessorContractSecrets.Token();
        row.EmailConfirmedAt = clock.GetUtcNow();
        row.ReadSessionHash = AssessorContractSecrets.Hash(session);
        row.ReadSessionExpiresAt = clock.GetUtcNow().AddMinutes(30);
        row.ContractViewedAt = null;
        row.OtpHash = null;
        await Save(row, ct);
        return session;
    }

    public async Task<object> Inspect(string session, CancellationToken ct)
    {
        var row = await BySession(session, ct);
        return new { row.FullName, row.TemplateVersion, row.ContractSha256, row.SignatureStatement, row.ReadSessionExpiresAt };
    }

    public async Task<byte[]> Pdf(string session, CancellationToken ct)
    {
        var row = await BySession(session, ct);
        var bytes = await files.Read(row.ContractStorageKey!, row.ContractSha256!, ct);
        row.ContractViewedAt = clock.GetUtcNow();
        await Save(row, ct);
        return bytes;
    }

    public async Task RequestSignOtp(string session, CancellationToken ct)
    {
        var row = await BySession(session, ct);
        if (row.ContractViewedAt is null)
            throw new AuthException("CONTRACT_NOT_VIEWED", "Vui lòng mở hợp đồng trước khi yêu cầu OTP ký.", 400);
        await IssueOtp(row, "Sign", ct);
    }

    public async Task Sign(SignAssessorContract request, CancellationToken ct)
    {
        var row = await BySession(request.Token, ct);
        if (!request.Accepted || row.ContractViewedAt is null || string.IsNullOrWhiteSpace(request.SignedName) ||
            !string.Equals(request.ContractSha256, row.ContractSha256, StringComparison.OrdinalIgnoreCase))
            throw new AuthException("SIGNATURE_INVALID", "Vui lòng xác nhận đúng bản hợp đồng và nhập tên ký.", 400);
        await CheckOtp(row, "Sign", request.Code, ct);
        var now = clock.GetUtcNow();
        row.SignedName = request.SignedName.Trim();
        row.SignedAt = now;
        row.Status = AssessorOnboardingStatus.AwaitingApproval;
        row.AutoApproveAt = row.AutoApprove ? now.AddMinutes(15) : null;
        row.OtpHash = null;
        row.ReadSessionHash = null;
        row.ReadSessionExpiresAt = null;
        await Save(row, ct);
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
        row.ContractTokenHash = AssessorContractSecrets.Hash(token);
        row.ContractTokenExpiresAt = now.AddDays(3);
        row.ReadSessionHash = null;
        row.ReadSessionExpiresAt = null;
        row.ContractViewedAt = null;
        row.OtpHash = null;
        var link = settings.PublicBaseUrl.TrimEnd('/') + "/assessor-onboarding/contract.html#token=" + token;
        Queue(row, "Contract", row.ContractTokenHash, row.ContractTokenExpiresAt.Value,
            new(row.EmailNormalized, "UniNet - Hợp đồng thẩm định viên",
                $"Vui lòng mở đường dẫn để xác thực email, đọc và xác nhận hợp đồng. Link có hiệu lực 3 ngày.\n\n{link}"));
    }

    private async Task IssueOtp(AssessorOnboarding row, string purpose, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (row.OtpLastSentAt > now.AddSeconds(-60))
            throw new AuthException("RATE_LIMITED", "Vui lòng chờ 60 giây trước khi gửi lại OTP.", 429);
        var since = now.AddHours(-1);
        if (await db.AssessorOnboardingEmails.CountAsync(x => x.OnboardingId == row.Id &&
            (x.Kind == "ReadOtp" || x.Kind == "SignOtp") && x.CreatedAt > since, ct) >= 3)
            throw new AuthException("RATE_LIMITED", "Bạn đã đạt giới hạn 3 mã OTP mỗi giờ.", 429);
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        row.OtpPurpose = purpose;
        row.OtpExpiresAt = now.AddMinutes(10);
        row.OtpLastSentAt = now;
        row.OtpFailedAttempts = 0;
        row.OtpHash = secrets.CreateOtp(row, code);
        var action = purpose == "Read" ? "mở hợp đồng" : "xác nhận ký hợp đồng";
        Queue(row, purpose + "Otp", AssessorContractSecrets.Hash(row.OtpHash), row.OtpExpiresAt.Value,
            new(row.EmailNormalized, $"UniNet - OTP {action}",
                $"Mã OTP {action} của bạn là {code}. Mã có hiệu lực 10 phút. Không chia sẻ mã với người khác."));
        await Save(row, ct);
    }

    private async Task CheckOtp(AssessorOnboarding row, string purpose, string code, CancellationToken ct)
    {
        if (row.OtpHash is null || row.OtpPurpose != purpose || row.OtpExpiresAt is null || row.OtpExpiresAt <= clock.GetUtcNow() ||
            row.OtpFailedAttempts >= 5)
            throw new AuthException("OTP_UNAVAILABLE", "OTP đã hết hiệu lực. Vui lòng yêu cầu mã mới.", 400);
        if (!secrets.VerifyOtp(row, code))
        {
            row.OtpFailedAttempts++;
            if (row.OtpFailedAttempts >= 5) row.OtpHash = null;
            await Save(row, ct);
            throw new AuthException("OTP_INVALID", "Mã OTP không đúng.", 400);
        }
    }

    private async Task<AssessorOnboarding> ByContractToken(string token, CancellationToken ct)
    {
        EnsureEnabled();
        var hash = AssessorContractSecrets.Hash(token);
        var row = await db.AssessorOnboardings.SingleOrDefaultAsync(x => x.ContractTokenHash == hash, ct);
        if (row?.Status != AssessorOnboardingStatus.AwaitingSignature ||
            row.ContractTokenExpiresAt is null || row.ContractTokenExpiresAt <= clock.GetUtcNow())
            throw Unavailable();
        return row;
    }

    private async Task<AssessorOnboarding> BySession(string token, CancellationToken ct)
    {
        EnsureEnabled();
        var hash = AssessorContractSecrets.Hash(token);
        var row = await db.AssessorOnboardings.SingleOrDefaultAsync(x => x.ReadSessionHash == hash, ct);
        if (row?.Status != AssessorOnboardingStatus.AwaitingSignature ||
            row.ReadSessionExpiresAt is null || row.ReadSessionExpiresAt <= clock.GetUtcNow() ||
            row.ContractTokenExpiresAt is null || row.ContractTokenExpiresAt <= clock.GetUtcNow())
            throw Unavailable();
        return row;
    }

    private void Queue(AssessorOnboarding row, string kind, string credentialHash, DateTimeOffset expiry, AssessorEmailPayload payload)
    {
        var now = clock.GetUtcNow();
        db.AssessorOnboardingEmails.Add(new()
        {
            OnboardingId = row.Id, Kind = kind, CreatedAt = now, ExpiresAt = expiry, NextAttemptAt = now,
            DeduplicationKey = $"{row.Id:N}:{kind}:{Guid.NewGuid():N}",
            CredentialHash = credentialHash, PayloadCiphertext = secrets.Protect(JsonSerializer.Serialize(payload))
        });
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
