using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record AssessorReviewResult(Guid Id, string Status, Guid? AccountId);
public sealed record RejectAssessorRequest([Required, MaxLength(2000)] string Reason);
public sealed record SetAssessorPassword(
    [Required, StringLength(43, MinimumLength = 43)] string Token,
    [Required, MinLength(8), MaxLength(1024)] string Password,
    [Required, MaxLength(1024)] string ConfirmPassword);

public sealed class AssessorActivationService(
    UniNetDbContext db, AssessorOnboardingEmailQueue emails, IOptions<AssessorContractOptions> options, TimeProvider clock)
{
    private readonly PasswordHasher<Account> hasher = new();

    public async Task<byte[]> ReviewPdf(Guid actor, Guid id, IAssessorContractFiles files, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var row = await db.AssessorOnboardings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row?.ContractStorageKey is null || row.ContractSha256 is null)
            throw new AuthException("CONTRACT_UNAVAILABLE", "Hồ sơ chưa có hợp đồng.", 404);
        return await files.Read(row.ContractStorageKey, row.ContractSha256, ct);
    }

    public async Task<object> Review(Guid actor, Guid id, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var row = await db.AssessorOnboardings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new AuthException("ONBOARDING_NOT_FOUND", "Không tìm thấy hồ sơ.", 404);
        return new {
            row.Id, row.EmailNormalized, row.FullName, Status = row.Status.ToString(), row.AccountId,
            row.AutoApprove, row.AutoApproveAt, row.SignedName, row.SignedAt, row.EmailConfirmedAt,
            row.ContractViewedAt, row.TemplateVersion, row.ContractSha256,
            row.ReviewedByAccountId, row.ReviewedAt, row.ReviewReason, row.ActivatedAt
        };
    }

    public async Task<AssessorReviewResult> Approve(Guid actor, Guid id, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        return await ApproveCore(id, actor, false, ct);
    }

    public async Task ApproveNext(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        var now = clock.GetUtcNow();
        var id = await db.AssessorOnboardings.AsNoTracking().Where(x =>
            x.Status == AssessorOnboardingStatus.AwaitingApproval && x.AutoApprove &&
            x.AutoApproveAt != null && x.AutoApproveAt <= now && x.SignedAt != null &&
            x.EmailConfirmedAt != null && x.ContractSha256 != null)
            .OrderBy(x => x.AutoApproveAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (id.HasValue) await ApproveCore(id.Value, null, true, ct);
    }

    private async Task<AssessorReviewResult> ApproveCore(Guid id, Guid? actor, bool automatic, CancellationToken ct)
    {
        EnsureEnabled();
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await db.AssessorOnboardings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new AuthException("ONBOARDING_NOT_FOUND", "Không tìm thấy hồ sơ.", 404);
        if (row.Status is AssessorOnboardingStatus.AwaitingPassword or AssessorOnboardingStatus.Activated)
            return Result(row);
        if (row.Status != AssessorOnboardingStatus.AwaitingApproval || row.SignedAt is null ||
            row.EmailConfirmedAt is null || string.IsNullOrWhiteSpace(row.ContractSha256) ||
            string.IsNullOrWhiteSpace(row.FullName) || string.IsNullOrWhiteSpace(row.SignedName))
            throw Changed();
        if (automatic && (!row.AutoApprove || row.AutoApproveAt is null || row.AutoApproveAt > now))
            return Result(row);

        // Claim this revision first. The row lock remains held until account creation and email commit.
        var claimedRevision = Guid.NewGuid();
        var claimed = await db.AssessorOnboardings.Where(x => x.Id == id && x.Revision == row.Revision &&
            x.Status == AssessorOnboardingStatus.AwaitingApproval)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Revision, claimedRevision), ct);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(ct);
            return await ApprovedOrChanged(id, ct);
        }

        row.ReviewedByAccountId = actor;
        row.ReviewedAt = now;
        row.ReviewReason = null;
        if (await db.Accounts.AnyAsync(x => x.Email == row.EmailNormalized, ct))
        {
            await RecordConflict(row, ct);
            await transaction.CommitAsync(ct);
            return Result(row);
        }

        var account = new Account
        {
            Email = row.EmailNormalized, Role = AccountRole.Assessor, Status = AccountStatus.Pending,
            EmailVerified = true, CreatedAt = now, UpdatedAt = now,
            Profile = new UserProfile
            {
                FullName = row.FullName, Nickname = row.FullName[..Math.Min(row.FullName.Length, 100)],
                CreatedAt = now, UpdatedAt = now
            }
        };
        db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Accounts_Email" })
        {
            // A concurrent registration won the email after the check. Undo all partial provisioning.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var changed = await db.AssessorOnboardings.Where(x => x.Id == id &&
                x.Status == AssessorOnboardingStatus.AwaitingApproval && x.Revision == row.Revision)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssessorOnboardingStatus.ApprovalConflict)
                    .SetProperty(p => p.ReviewReason, "EMAIL_ALREADY_REGISTERED")
                    .SetProperty(p => p.AutoApproveAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.ReviewedAt, now).SetProperty(p => p.ReviewedByAccountId, actor)
                    .SetProperty(p => p.Revision, claimedRevision), ct);
            if (changed == 0) return await ApprovedOrChanged(id, ct);
            row.Status = AssessorOnboardingStatus.ApprovalConflict;
            return Result(row);
        }

        row.AccountId = account.Id;
        row.Status = AssessorOnboardingStatus.AwaitingPassword;
        QueuePassword(row);
        await db.AssessorOnboardings.Where(x => x.Id == id && x.Revision == claimedRevision)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.AccountId, account.Id)
                .SetProperty(p => p.Status, AssessorOnboardingStatus.AwaitingPassword)
                .SetProperty(p => p.ReviewedByAccountId, actor).SetProperty(p => p.ReviewedAt, now)
                .SetProperty(p => p.ReviewReason, (string?)null)
                .SetProperty(p => p.AutoApproveAt, (DateTimeOffset?)null)
                .SetProperty(p => p.SetPasswordTokenHash, row.SetPasswordTokenHash)
                .SetProperty(p => p.SetPasswordTokenExpiresAt, row.SetPasswordTokenExpiresAt)
                .SetProperty(p => p.SetPasswordTokenUsedAt, (DateTimeOffset?)null), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result(row);
    }

    public async Task<AssessorReviewResult> Reject(Guid actor, Guid id, string reason, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        EnsureEnabled();
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new AuthException("INVALID_REASON", "Vui lòng nhập lý do từ chối, tối đa 2000 ký tự.", 400);
        reason = reason.Trim();
        var row = await db.AssessorOnboardings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new AuthException("ONBOARDING_NOT_FOUND", "Không tìm thấy hồ sơ.", 404);
        if (row.Status == AssessorOnboardingStatus.Rejected && row.ReviewReason == reason) return Result(row);
        if (row.Status is not (AssessorOnboardingStatus.AwaitingApproval or AssessorOnboardingStatus.ApprovalConflict))
            throw Changed();
        var now = clock.GetUtcNow();
        var revision = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.AssessorOnboardings.Where(x => x.Id == id && x.Revision == row.Revision && x.Status == row.Status)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssessorOnboardingStatus.Rejected)
                .SetProperty(p => p.ReviewReason, reason).SetProperty(p => p.ReviewedByAccountId, actor)
                .SetProperty(p => p.ReviewedAt, now).SetProperty(p => p.AutoApproveAt, (DateTimeOffset?)null)
                .SetProperty(p => p.Revision, revision), ct);
        if (changed == 0) throw Changed();
        emails.Add(id, "Rejected", null, now.AddDays(7),
            new(row.EmailNormalized, "UniNet - Kết quả xét duyệt hồ sơ", $"Hồ sơ của bạn đã được xem xét và từ chối.\nLý do: {reason}"));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        row.Status = AssessorOnboardingStatus.Rejected;
        return Result(row);
    }

    public async Task<object> InspectPassword(string token, CancellationToken ct)
    {
        EnsureEnabled();
        var row = await PasswordRow(token, ct);
        return new { row.FullName, row.EmailNormalized, expiresAt = row.SetPasswordTokenExpiresAt };
    }

    public async Task SetPassword(SetAssessorPassword request, CancellationToken ct)
    {
        EnsureEnabled();
        AuthService.ValidatePassword(request.Password, request.ConfirmPassword);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var row = await PasswordRow(request.Token, ct);
        var account = await PendingAccount(row, ct);
        var passwordHash = hasher.HashPassword(account, request.Password);
        var now = clock.GetUtcNow();
        var revision = Guid.NewGuid();
        var changed = await db.AssessorOnboardings.Where(x => x.Id == row.Id && x.Revision == row.Revision &&
            x.Status == AssessorOnboardingStatus.AwaitingPassword && x.SetPasswordTokenUsedAt == null &&
            x.SetPasswordTokenExpiresAt > now)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssessorOnboardingStatus.Activated)
                .SetProperty(p => p.ActivatedAt, now).SetProperty(p => p.SetPasswordTokenUsedAt, now)
                .SetProperty(p => p.Revision, revision), ct);
        if (changed == 0) throw PasswordUnavailable();
        var activated = await db.Accounts.Where(x => x.Id == account.Id && x.Status == AccountStatus.Pending &&
            x.Role == AccountRole.Assessor && x.PasswordHash == null && x.Email == row.EmailNormalized &&
            x.SessionVersion == account.SessionVersion)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.PasswordHash, passwordHash)
                .SetProperty(p => p.Status, AccountStatus.Active).SetProperty(p => p.EmailVerified, true)
                .SetProperty(p => p.UpdatedAt, now).SetProperty(p => p.SessionVersion, p => p.SessionVersion + 1), ct);
        if (activated == 0) throw PasswordUnavailable();
        await transaction.CommitAsync(ct);
    }

    public async Task ResendPassword(string token, CancellationToken ct)
    {
        EnsureEnabled();
        var hash = AssessorContractSecrets.Hash(token);
        var row = await db.AssessorOnboardings.AsNoTracking().SingleOrDefaultAsync(x =>
            x.SetPasswordTokenHash == hash && x.Status == AssessorOnboardingStatus.AwaitingPassword, ct);
        if (row != null) await ResendPasswordCore(row, ct);
    }

    public async Task ResendPasswordByAdmin(Guid actor, Guid id, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        EnsureEnabled();
        var row = await db.AssessorOnboardings.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.Status == AssessorOnboardingStatus.AwaitingPassword, ct) ?? throw Changed();
        await ResendPasswordCore(row, ct);
    }

    private async Task ResendPasswordCore(AssessorOnboarding row, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (row.SetPasswordTokenExpiresAt > now.AddHours(48).AddSeconds(-60) ||
            await db.AssessorOnboardingEmails.CountAsync(x => x.OnboardingId == row.Id && x.Kind == "SetPassword" &&
                x.CreatedAt > now.AddHours(-1), ct) >= 3)
            throw new AuthException("RATE_LIMITED", "Vui lòng chờ trước khi gửi lại link. Tối đa 3 lần mỗi giờ.", 429);
        await PendingAccount(row, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var revision = Guid.NewGuid();
        QueuePassword(row);
        var changed = await db.AssessorOnboardings.Where(x => x.Id == row.Id && x.Revision == row.Revision &&
            x.Status == AssessorOnboardingStatus.AwaitingPassword)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.SetPasswordTokenHash, row.SetPasswordTokenHash)
                .SetProperty(p => p.SetPasswordTokenExpiresAt, row.SetPasswordTokenExpiresAt)
                .SetProperty(p => p.Revision, revision), ct);
        if (changed == 0) throw Changed();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private void QueuePassword(AssessorOnboarding row)
    {
        var now = clock.GetUtcNow();
        var token = AssessorContractSecrets.Token();
        row.SetPasswordTokenHash = AssessorContractSecrets.Hash(token);
        row.SetPasswordTokenExpiresAt = now.AddHours(48);
        var link = options.Value.PublicBaseUrl.TrimEnd('/') + "/assessor-onboarding/activate.html#token=" + token;
        emails.Add(row.Id, "SetPassword", row.SetPasswordTokenHash, row.SetPasswordTokenExpiresAt.Value,
            new(row.EmailNormalized, "UniNet - Hồ sơ đã được duyệt",
                $"Hồ sơ của bạn đã được duyệt. Vui lòng đặt mật khẩu để kích hoạt tài khoản. Link có hiệu lực 48 giờ.\n\n{link}"));
    }

    private async Task<AssessorOnboarding> PasswordRow(string token, CancellationToken ct)
    {
        var hash = AssessorContractSecrets.Hash(token);
        var now = clock.GetUtcNow();
        var row = await db.AssessorOnboardings.AsNoTracking().SingleOrDefaultAsync(x =>
            x.SetPasswordTokenHash == hash && x.Status == AssessorOnboardingStatus.AwaitingPassword &&
            x.SetPasswordTokenUsedAt == null && x.SetPasswordTokenExpiresAt > now, ct) ?? throw PasswordUnavailable();
        await PendingAccount(row, ct);
        return row;
    }

    private async Task<Account> PendingAccount(AssessorOnboarding row, CancellationToken ct)
        => await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.AccountId &&
            x.Role == AccountRole.Assessor && x.Status == AccountStatus.Pending && x.PasswordHash == null &&
            x.Email == row.EmailNormalized, ct) ?? throw PasswordUnavailable();

    private async Task RecordConflict(AssessorOnboarding row, CancellationToken ct)
    {
        row.Status = AssessorOnboardingStatus.ApprovalConflict;
        await db.AssessorOnboardings.Where(x => x.Id == row.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssessorOnboardingStatus.ApprovalConflict)
                .SetProperty(p => p.AutoApproveAt, (DateTimeOffset?)null)
                .SetProperty(p => p.ReviewReason, "EMAIL_ALREADY_REGISTERED")
                .SetProperty(p => p.ReviewedAt, row.ReviewedAt).SetProperty(p => p.ReviewedByAccountId, row.ReviewedByAccountId), ct);
    }

    private async Task<AssessorReviewResult> ApprovedOrChanged(Guid id, CancellationToken ct)
    {
        var current = await db.AssessorOnboardings.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (current.Status is AssessorOnboardingStatus.AwaitingPassword or AssessorOnboardingStatus.Activated or AssessorOnboardingStatus.ApprovalConflict)
            return Result(current);
        throw Changed();
    }

    private async Task RequireAdmin(Guid actor, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(x => x.Id == actor && x.Role == AccountRole.Admin && x.Status == AccountStatus.Active, ct))
            throw new AuthException("FORBIDDEN", "Chỉ Admin đang hoạt động mới được xét duyệt hồ sơ.", 403);
    }

    private void EnsureEnabled()
    {
        if (!options.Value.Enabled) throw new AuthException("CONTRACTS_DISABLED", "Chức năng onboarding chưa được cấu hình.", 503);
    }

    private static AssessorReviewResult Result(AssessorOnboarding row) => new(row.Id, row.Status.ToString(), row.AccountId);
    private static AuthException Changed() => new("ONBOARDING_CHANGED", "Hồ sơ không ở trạng thái có thể xử lý hoặc vừa được cập nhật.", 409);
    private static AuthException PasswordUnavailable() => new("ACTIVATION_UNAVAILABLE", "Link kích hoạt không hợp lệ, đã dùng hoặc hết hạn.", 410);
}
