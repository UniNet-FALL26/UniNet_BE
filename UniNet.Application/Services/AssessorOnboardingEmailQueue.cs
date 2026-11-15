using System.Text.Json;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AssessorOnboardingEmailQueue(UniNetDbContext db, AssessorContractSecrets secrets, TimeProvider clock)
{
    public void Add(Guid onboardingId, string kind, string? credentialHash, DateTimeOffset expiry, AssessorEmailPayload payload)
    {
        var now = clock.GetUtcNow();
        db.AssessorOnboardingEmails.Add(new()
        {
            OnboardingId = onboardingId, Kind = kind, CreatedAt = now, ExpiresAt = expiry, NextAttemptAt = now,
            DeduplicationKey = $"{onboardingId:N}:{kind}:{Guid.NewGuid():N}",
            CredentialHash = credentialHash, PayloadCiphertext = secrets.Protect(JsonSerializer.Serialize(payload))
        });
    }
}
