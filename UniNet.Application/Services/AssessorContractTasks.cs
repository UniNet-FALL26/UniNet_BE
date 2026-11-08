using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AssessorContractTasks(
    UniNetDbContext db, IDataProtectionProvider protection, AssessorContractSecrets secrets,
    IAssessorContractFiles files, IAssessorOnboardingMailSender mail, AssessorContractService contracts,
    IOptions<AssessorContractOptions> options, TimeProvider clock)
{
    public async Task GenerateNext(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        var now = clock.GetUtcNow();
        var row = await db.AssessorOnboardings.Where(x => x.Status == AssessorOnboardingStatus.ProfileSubmitted &&
                (x.GenerationRetryAt == null || x.GenerationRetryAt <= now))
            .OrderBy(x => x.SubmittedAt).FirstOrDefaultAsync(ct);
        if (row is null) return;
        var revision = row.Revision;
        try
        {
            var json = protection.CreateProtector("UniNet.AssessorOnboarding.Profile.v1")
                .Unprotect(row.SensitiveProfileCiphertext!);
            var profile = JsonSerializer.Deserialize<ContractProfile>(json)
                ?? throw new InvalidOperationException("Invalid contract profile.");
            var document = await files.Generate(row, profile, ct);
            row.ContractStorageKey = document.Key;
            row.ContractSha256 = document.Sha256;
            row.TemplateVersion = options.Value.TemplateVersion;
            row.SignatureStatement = options.Value.SignatureStatement;
            row.GenerationErrorCode = null;
            row.GenerationRetryAt = null;
            row.Status = AssessorOnboardingStatus.AwaitingSignature;
            row.Revision = Guid.NewGuid();
            contracts.QueueContractLink(row);
            // State and email are committed atomically. A losing generator leaves only a private, content-addressed file.
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            db.ChangeTracker.Clear();
            var nextRevision = Guid.NewGuid();
            await db.AssessorOnboardings.Where(x => x.Id == row.Id &&
                x.Status == AssessorOnboardingStatus.ProfileSubmitted && x.Revision == revision)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.GenerationAttempts, p => p.GenerationAttempts + 1)
                    .SetProperty(p => p.GenerationErrorCode, "PDF_GENERATION_FAILED")
                    .SetProperty(p => p.GenerationRetryAt, now.AddMinutes(5))
                    .SetProperty(p => p.Revision, nextRevision), ct);
        }
    }

    public async Task SendNext(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        var now = clock.GetUtcNow();
        var id = await db.AssessorOnboardingEmails
            .Where(x => x.SentAt == null && x.CancelledAt == null && x.PayloadCiphertext != null &&
                x.NextAttemptAt <= now && (x.LeaseUntil == null || x.LeaseUntil <= now))
            .OrderBy(x => x.NextAttemptAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (id is null) return;
        var lease = Guid.NewGuid();
        var claimed = await db.AssessorOnboardingEmails.Where(x => x.Id == id &&
            x.SentAt == null && x.CancelledAt == null && (x.LeaseUntil == null || x.LeaseUntil <= now))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.LeaseId, lease)
                .SetProperty(p => p.LeaseUntil, now.AddMinutes(2)), ct);
        if (claimed == 0) return;
        var email = await db.AssessorOnboardingEmails.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var row = await db.AssessorOnboardings.AsNoTracking().SingleAsync(x => x.Id == email.OnboardingId, ct);
        var pending = db.AssessorOnboardingEmails.Where(x => x.Id == id && x.LeaseId == lease);
        if (!CanSend(row, email, now))
        {
            await pending.ExecuteUpdateAsync(x => x.SetProperty(p => p.CancelledAt, now)
                .SetProperty(p => p.PayloadCiphertext, (string?)null)
                .SetProperty(p => p.LeaseUntil, (DateTimeOffset?)null).SetProperty(p => p.LeaseId, (Guid?)null), ct);
            return;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<AssessorEmailPayload>(secrets.Unprotect(email.PayloadCiphertext!))
                ?? throw new InvalidOperationException("Invalid email payload.");
            await mail.Send(payload, email.DeduplicationKey, ct);
            await pending.ExecuteUpdateAsync(x => x.SetProperty(p => p.SentAt, clock.GetUtcNow())
                .SetProperty(p => p.PayloadCiphertext, (string?)null)
                .SetProperty(p => p.LastErrorCode, (string?)null)
                .SetProperty(p => p.LeaseUntil, (DateTimeOffset?)null).SetProperty(p => p.LeaseId, (Guid?)null), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            var attempts = email.Attempts + 1;
            await pending.ExecuteUpdateAsync(x => x.SetProperty(p => p.Attempts, attempts)
                .SetProperty(p => p.LastErrorCode, "EMAIL_SEND_FAILED")
                .SetProperty(p => p.NextAttemptAt, clock.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempts, 8)))))
                .SetProperty(p => p.LeaseUntil, (DateTimeOffset?)null).SetProperty(p => p.LeaseId, (Guid?)null)
                .SetProperty(p => p.CancelledAt, attempts >= 10 ? clock.GetUtcNow() : (DateTimeOffset?)null)
                .SetProperty(p => p.PayloadCiphertext, attempts >= 10 ? null : email.PayloadCiphertext), ct);
        }
    }

    private static bool CanSend(AssessorOnboarding row, AssessorOnboardingEmail email, DateTimeOffset now)
    {
        if (email.ExpiresAt <= now || row.Status != AssessorOnboardingStatus.AwaitingSignature) return false;
        return email.Kind switch
        {
            "Contract" => row.ContractTokenHash == email.CredentialHash,
            "ReadOtp" => row.OtpPurpose == "Read" && row.OtpHash != null &&
                AssessorContractSecrets.Hash(row.OtpHash) == email.CredentialHash && row.OtpExpiresAt > now,
            "SignOtp" => row.OtpPurpose == "Sign" && row.OtpHash != null &&
                AssessorContractSecrets.Hash(row.OtpHash) == email.CredentialHash && row.OtpExpiresAt > now,
            _ => false
        };
    }
}
