using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class VerificationReservationTasks(UniNetDbContext db)
{
    public async Task ExpireAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        // A delayed job can never expire a Confirmed booking. Keep timestamps/token for audit.
        await db.ProjectVerifications.Where(v => v.BookingStatus == VerificationBookingStatus.Held &&
                v.ReservedUntil <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.BookingStatus, VerificationBookingStatus.Expired)
                .SetProperty(v => v.UpdatedAt, now), ct);
    }
}
