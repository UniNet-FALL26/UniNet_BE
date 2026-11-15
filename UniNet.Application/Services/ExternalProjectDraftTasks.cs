using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ExternalProjectDraftTasks(UniNetDbContext db)
{
    public async Task RunAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await db.ExternalProjectDrafts.Where(d => d.Status != ExternalProjectDraftStatus.Confirmed &&
            d.Status != ExternalProjectDraftStatus.Expired && d.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, ExternalProjectDraftStatus.Expired)
                .SetProperty(d => d.Version, d => d.Version + 1).SetProperty(d => d.UpdatedAt, now), ct);
    }
}
