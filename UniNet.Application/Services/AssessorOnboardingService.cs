using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record CreateAssessorInvitation([Required, EmailAddress, MaxLength(255)] string Email, bool AutoApprove = false);
public sealed record AssessorInvitation(Guid Id, string Email, DateTimeOffset ExpiresAt, string ProfileLink);
public sealed record AssessorProfileLink([Required, StringLength(43, MinimumLength = 43)] string Token);
public sealed record SubmitAssessorOnboardingProfile(
    [Required, StringLength(43, MinimumLength = 43)] string Token,
    [Required, MaxLength(255)] string FullName,
    [Required, RegularExpression("^[0-9]{12}$")] string CitizenId,
    [Required, RegularExpression("^[0-9]{10}(-?[0-9]{3})?$")] string TaxCode,
    [Required, MaxLength(150)] string BankName,
    [Required, RegularExpression("^[A-Za-z0-9]{1,34}$")] string BankAccountNumber,
    [Required, MaxLength(255)] string BankAccountHolder);

public sealed class AssessorOnboardingService(UniNetDbContext db, IDataProtectionProvider protection, TimeProvider clock)
{
    public async Task<AssessorInvitation> Invite(Guid actor, CreateAssessorInvitation request, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var email = request.Email.Trim().ToLowerInvariant();
        var now = clock.GetUtcNow();
        var token = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new AssessorOnboarding
        {
            EmailNormalized = email,
            CreatedByAccountId = actor,
            AutoApprove = request.AutoApprove,
            Status = AssessorOnboardingStatus.Invited,
            CreatedAt = now,
            ProfileTokenHash = Hash(token),
            ProfileTokenExpiresAt = now.AddDays(7)
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await db.Accounts.AnyAsync(x => x.Email == email, ct))
            throw new AuthException("EMAIL_ALREADY_REGISTERED", "Email này đã được sử dụng bởi một tài khoản khác.", 409);

        await db.AssessorOnboardings
            .Where(x => x.EmailNormalized == email && x.Status == AssessorOnboardingStatus.Invited && x.ProfileTokenExpiresAt <= now)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, AssessorOnboardingStatus.Expired), ct);
        db.AssessorOnboardings.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UX_AssessorOnboardings_OpenEmail" })
        {
            throw new AuthException("ONBOARDING_ALREADY_OPEN", "Email này đã có hồ sơ onboarding đang mở.", 409);
        }

        await transaction.CommitAsync(ct);
        return new(row.Id, email, row.ProfileTokenExpiresAt, $"/assessor-onboarding/profile.html#token={token}");
    }

    public async Task Revoke(Guid actor, Guid id, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var now = clock.GetUtcNow();
        var updated = await db.AssessorOnboardings
            .Where(x => x.Id == id && (x.Status == AssessorOnboardingStatus.Invited ||
                x.Status == AssessorOnboardingStatus.ProfileSubmitted ||
                x.Status == AssessorOnboardingStatus.AwaitingSignature ||
                x.Status == AssessorOnboardingStatus.AwaitingApproval))
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.Status, AssessorOnboardingStatus.Revoked)
                .SetProperty(p => p.RevokedAt, now), ct);
        if (updated == 0)
            throw new AuthException("INVITATION_NOT_OPEN", "Hồ sơ không còn ở trạng thái có thể thu hồi.", 409);
    }

    public async Task<string> ReadForm(string token, CancellationToken ct)
        => (await FindOpenInvitation(token, ct)).EmailNormalized;

    public async Task<Guid> Submit(SubmitAssessorOnboardingProfile request, CancellationToken ct)
    {
        var row = await FindOpenInvitation(request.Token, ct);
        var now = clock.GetUtcNow();
        var sensitive = JsonSerializer.Serialize(new
        {
            request.CitizenId,
            request.TaxCode,
            BankName = request.BankName.Trim(),
            request.BankAccountNumber,
            BankAccountHolder = request.BankAccountHolder.Trim()
        });
        row.FullName = request.FullName.Trim();
        row.SensitiveProfileCiphertext = protection.CreateProtector("UniNet.AssessorOnboarding.Profile.v1")
            .Protect(sensitive);
        row.SubmittedAt = now;
        row.ProfileTokenUsedAt = now;
        row.Status = AssessorOnboardingStatus.ProfileSubmitted;
        row.Revision = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw InvalidInvitation();
        }
        return row.Id;
    }

    private async Task<AssessorOnboarding> FindOpenInvitation(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        var now = clock.GetUtcNow();
        return await db.AssessorOnboardings.SingleOrDefaultAsync(x => x.ProfileTokenHash == hash &&
            x.Status == AssessorOnboardingStatus.Invited && x.ProfileTokenExpiresAt > now, ct)
            ?? throw InvalidInvitation();
    }

    private async Task RequireAdmin(Guid actor, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(x => x.Id == actor && x.Role == AccountRole.Admin && x.Status == AccountStatus.Active, ct))
            throw new AuthException("FORBIDDEN", "Chỉ Admin đang hoạt động mới được quản lý lời mời.", 403);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static AuthException InvalidInvitation() =>
        new("INVITATION_UNAVAILABLE", "Lời mời không hợp lệ hoặc đã hết hiệu lực.", 410);
}
