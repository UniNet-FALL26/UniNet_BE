using System.Data;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class VerificatorAvailabilityService(UniNetDbContext db)
{
    public async Task<List<VerificatorAvailabilityResponse>> ListAsync(Guid accountId,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var userId = await RequireActorAsync(accountId, ct);
        if (to <= from || to - from > TimeSpan.FromDays(31))
            throw new ProjectVerificationException("INVALID_AVAILABILITY_RANGE", "Provide a date range of at most 31 days.");
        return await db.VerificatorAvailabilities.AsNoTracking()
            .Where(a => a.UserId == userId && a.StartAt < to && a.EndAt > from)
            .OrderBy(a => a.StartAt).ThenBy(a => a.Id)
            .Select(a => new VerificatorAvailabilityResponse(a.Id, a.StartAt, a.EndAt)).ToListAsync(ct);
    }

    public Task<VerificatorAvailabilityResponse> SaveAsync(Guid accountId, Guid? id,
        SaveVerificatorAvailabilityRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        var userId = await RequireActorAsync(accountId, ct);
        var now = DateTimeOffset.UtcNow;
        var start = request.StartAt.ToUniversalTime();
        var end = request.EndAt.ToUniversalTime();
        if (start <= now || end <= start || end - start > TimeSpan.FromDays(31) || end > now.AddDays(365))
            throw new ProjectVerificationException("INVALID_AVAILABILITY_INTERVAL",
                "Availability must be in the future, last at most 31 days and end within 365 days.");
        var item = id.HasValue ? await FindOwnedAsync(id.Value, userId, ct) : new VerificatorAvailability { UserId = userId, CreatedAt = now };
        if (await db.VerificatorAvailabilities.AnyAsync(a => a.UserId == userId && a.Id != item.Id &&
            a.StartAt < end && a.EndAt > start, ct))
            throw new ProjectVerificationException("AVAILABILITY_OVERLAP", "Availability overlaps an existing interval.", 409);
        if (id.HasValue) await RequireCommitmentsCoveredAsync(item, start, end, ct);
        item.StartAt = start;
        item.EndAt = end;
        item.UpdatedAt = now;
        if (!id.HasValue) db.VerificatorAvailabilities.Add(item);
        return new VerificatorAvailabilityResponse(item.Id, item.StartAt, item.EndAt);
    }, ct);

    public async Task DeleteAsync(Guid accountId, Guid id, CancellationToken ct = default) =>
        await MutateAsync(async () =>
        {
            var userId = await RequireActorAsync(accountId, ct);
            var item = await FindOwnedAsync(id, userId, ct);
            await RequireCommitmentsCoveredAsync(item, null, null, ct);
            db.VerificatorAvailabilities.Remove(item);
            return true;
        }, ct);

    private async Task<Guid> RequireActorAsync(Guid accountId, CancellationToken ct) =>
        await db.UserProfiles.Where(p => p.AccountId == accountId && p.Account.Role == AccountRole.Verificator &&
            p.Account.Status == AccountStatus.Active).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(ct)
        ?? throw new ProjectVerificationException("FORBIDDEN", "An active Verificator account is required.", 403);

    private async Task<VerificatorAvailability> FindOwnedAsync(Guid id, Guid userId, CancellationToken ct) =>
        await db.VerificatorAvailabilities.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
        ?? throw new ProjectVerificationException("AVAILABILITY_NOT_FOUND", "Availability interval not found.", 404);

    private async Task RequireCommitmentsCoveredAsync(VerificatorAvailability item,
        DateTimeOffset? newStart, DateTimeOffset? newEnd, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // Old bookings without a resource are protected conservatively until an admin assigns them.
        var commitments = await db.ProjectVerifications.Where(v => v.Status == ProjectVerificationStatus.Pending &&
            v.ScheduledStartAt < item.EndAt && v.ScheduledEndAt > item.StartAt && v.ScheduledEndAt > now &&
            ((v.ReservedVerificatorUserId == item.UserId || v.ReservedVerificatorUserId == null) &&
                (v.BookingStatus == VerificationBookingStatus.Confirmed || v.BookingStatus == VerificationBookingStatus.Legacy ||
                 (v.BookingStatus == VerificationBookingStatus.Held && v.ReservedUntil > now)) ||
             v.Assignments.Any(a => a.AssignedToUserId == item.UserId &&
                (a.Status == VerificationAssignmentStatus.Assigned || a.Status == VerificationAssignmentStatus.Overdue))))
            .Select(v => new { Start = v.ScheduledStartAt!.Value, End = v.ScheduledEndAt!.Value }).ToListAsync(ct);
        foreach (var commitment in commitments)
        {
            if (newStart <= commitment.Start && newEnd >= commitment.End) continue;
            if (await db.VerificatorAvailabilities.AnyAsync(a => a.UserId == item.UserId && a.Id != item.Id &&
                a.StartAt <= commitment.Start && a.EndAt >= commitment.End, ct)) continue;
            throw new ProjectVerificationException("AVAILABILITY_IN_USE",
                "This change would remove availability required by a held, confirmed or assigned meeting.", 409);
        }
    }

    private async Task<T> MutateAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (EvidenceModerationSource.IsWriteConflict(ex))
        {
            throw new ProjectVerificationException("AVAILABILITY_CONFLICT", "Availability changed concurrently. Reload and retry.", 409);
        }
    }
}
