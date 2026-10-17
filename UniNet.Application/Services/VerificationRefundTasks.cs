using System.Data;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class VerificationRefundTasks(UniNetDbContext db, IRefundProvider provider, VerificationNotifications notifications)
{
    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.RefundTransactions.AsNoTracking().Where(r => r.Status == PaymentStatus.Pending &&
            r.Transaction.VerificationTransaction != null && (r.NextReconciliationAt == null || r.NextReconciliationAt <= now))
            .OrderBy(r => r.UpdatedAt).Select(r => r.Id).Take(50).ToListAsync(ct);
        foreach (var id in ids) await ProcessAsync(id, now, ct);
    }

    public async Task ProcessAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        ProviderRefundRequest request;
        bool lookup;
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var refund = await db.RefundTransactions.Include(r => r.Transaction).ThenInclude(p => p.VerificationTransaction)
                .SingleAsync(r => r.Id == id, ct);
            if (refund.Status != PaymentStatus.Pending || refund.NextReconciliationAt > now || refund.Transaction.VerificationTransaction == null) return;
            var verificationId = refund.Transaction.VerificationTransaction.VerificationId;
            if (refund.Transaction.Status != PaymentStatus.Success)
                throw new InvalidOperationException("A refund cannot be dispatched for an unsuccessful payment.");
            if (!provider.Supports(refund.PaymentProvider))
            {
                refund.FailureReason = "MANUAL_REFUND_REQUIRED";
                refund.NextReconciliationAt = now.AddHours(1); refund.UpdatedAt = now;
                await notifications.AdminsAsync(verificationId, $"refund-manual:{id}",
                    $"Refund {id} is reserved but requires manual execution: the payment provider has no configured refund adapter.", now, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return;
            }
            lookup = refund.DispatchStartedAt != null;
            refund.DispatchStartedAt ??= now;
            refund.ReconciliationAttempts++;
            refund.NextReconciliationAt = now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(refund.ReconciliationAttempts, 6))));
            refund.UpdatedAt = now;
            request = new(refund.Id.ToString("N"), refund.Transaction.PaymentRef, refund.Transaction.ProviderOrderCode, refund.Amount, refund.Reason);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        ProviderRefundResult? result = null;
        string? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            result = lookup ? await provider.LookupAsync(request, timeout.Token) : await provider.CreateAsync(request, timeout.Token);
            if (result.RequestKey != request.RequestKey || result.Amount != request.Amount || !Enum.IsDefined(result.Status) ||
                (result.Status == PaymentStatus.Success && string.IsNullOrWhiteSpace(result.Reference)) || result.Reference?.Length > 255)
            { result = null; error = "REFUND_PROVIDER_DATA_MISMATCH"; }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { error = "REFUND_RESULT_UNKNOWN"; }
        catch (HttpRequestException) { error = "REFUND_RESULT_UNKNOWN"; }
        // If the process stops after dispatch, the persisted DispatchStartedAt forces lookup on restart.
        db.ChangeTracker.Clear();
        await using var persistence = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await db.RefundTransactions.Include(r => r.Transaction).ThenInclude(p => p.VerificationTransaction).SingleAsync(r => r.Id == id, ct);
        if (current.Status != PaymentStatus.Pending) return;
        current.UpdatedAt = now;
        current.FailureReason = error;
        if (result != null)
        {
            current.Status = result.Status; current.RefundRef = result.Reference;
            if (result.Status == PaymentStatus.Success) current.CompletedAt = now;
            if (result.Status != PaymentStatus.Pending) current.NextReconciliationAt = null;
        }
        if (current.ReconciliationAttempts >= 5 || current.Status == PaymentStatus.Failed)
            await notifications.AdminsAsync(current.Transaction.VerificationTransaction!.VerificationId,
                $"refund-attention:{id}", $"Refund {id} needs attention; inspect its provider reconciliation state.", now, ct);
        await db.SaveChangesAsync(ct);
        await persistence.CommitAsync(ct);
    }
}
