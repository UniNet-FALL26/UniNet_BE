using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public sealed record FinanceRefundRequest(Guid RequestKey, Guid TransactionId, decimal Amount, string Reason);
public sealed record FinancePayment(Guid Id, Guid AccountId, string Email, string? FullName, string PackageName, decimal Amount, PaymentStatus Status, string? PaymentMethod, string? PaymentRef, DateTimeOffset CreatedAt, decimal ReservedRefund);
public sealed record FinanceRefund(Guid Id, Guid TransactionId, decimal Amount, PaymentStatus Status, RefundApproval Approval, string Reason, Guid? RequestedByUserId, DateTimeOffset? RequestedAt, Guid? ReviewedByAccountId, DateTimeOffset? ReviewedAt, string? ReviewReason, string? RefundRef, string? FailureReason);
public sealed record FinanceSummary(decimal PaymentsReceived, decimal RefundsPaid, decimal WithdrawalsPaid, decimal WalletIncome, decimal PendingWithdrawals, int RefundsAwaitingReview);
public sealed record FinanceRecipient(Guid AccountId, string Email, string FullName);

public sealed partial class FinanceService
{
    public async Task<IReadOnlyList<FinanceRecipient>> RecipientsAsync(Guid actor, string? search, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.WalletCredit, ct);
        if (string.IsNullOrWhiteSpace(search) || search.Trim().Length < 3) return [];
        var text = Text(search, 255).ToLowerInvariant();
        return await db.Accounts.AsNoTracking().Where(a => a.Status == AccountStatus.Active && (a.Role == AccountRole.Student || a.Role == AccountRole.Partner) &&
            (a.Email.ToLower().Contains(text) || a.Profile != null && a.Profile.FullName.ToLower().Contains(text)))
            .OrderBy(a => a.Email).Select(a => new FinanceRecipient(a.Id, a.Email, a.Profile == null ? "" : a.Profile.FullName)).Take(20).ToListAsync(ct);
    }
    public async Task<FinancePage<WalletEntry>> LedgerAsync(Guid actor, int page, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.FinanceView, ct);
        page = Math.Clamp(page, 1, 1000000); var query = db.Set<WalletEntry>().AsNoTracking();
        return new(await query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).Skip((page - 1) * 30).Take(30).ToListAsync(ct), page, 30, await query.CountAsync(ct));
    }
    private static FinanceRefund Map(RefundTransaction r) => new(r.Id, r.TransactionId, r.Amount, r.Status, r.Approval, r.Reason, r.RequestedByUserId, r.RequestedAt, r.ReviewedByAccountId, r.ReviewedAt, r.ReviewReason, r.RefundRef, r.FailureReason);
    public async Task<FinanceSummary> SummaryAsync(Guid actor, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.FinanceView, ct);
        return new(await Sum(db.Transactions.Where(t => t.Status == PaymentStatus.Success).Select(t => t.Amount), ct),
            await Sum(db.RefundTransactions.Where(t => t.Status == PaymentStatus.Success).Select(t => t.Amount), ct),
            await Sum(db.Set<WalletWithdrawal>().Where(t => t.State == WithdrawalState.Paid).Select(t => t.Amount), ct),
            await Sum(db.Set<WalletEntry>().Where(t => t.Amount > 0).Select(t => t.Amount), ct),
            await Sum(db.Set<WalletWithdrawal>().Where(t => t.State == WithdrawalState.Requested || t.State == WithdrawalState.Approved || t.State == WithdrawalState.Ordered).Select(t => t.Amount), ct),
            await db.RefundTransactions.CountAsync(t => t.Approval == RefundApproval.Pending, ct));
    }
    public async Task<FinancePage<FinancePayment>> PaymentsAsync(Guid actor, int page, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.FinanceView, ct);
        page = Math.Clamp(page, 1, 1000000);
        var rows = await db.Transactions.AsNoTracking().Include(t => t.User).ThenInclude(p => p.Account).Include(t => t.Package).Include(t => t.RefundTransactions)
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Skip((page - 1) * 30).Take(30).AsSplitQuery().ToListAsync(ct);
        return new(rows.Select(t => new FinancePayment(t.Id, t.User.AccountId, t.User.Account.Email, t.User.FullName, t.Package.Name, t.Amount, t.Status, t.PaymentMethod, t.PaymentRef, t.CreatedAt, RefundReservations.Reserved(t.RefundTransactions))).ToArray(), page, 30, await db.Transactions.CountAsync(ct));
    }
    public async Task<FinancePage<FinanceRefund>> RefundsAsync(Guid actor, int page, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.FinanceView, ct);
        page = Math.Clamp(page, 1, 1000000);
        var rows = await db.RefundTransactions.AsNoTracking().OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Skip((page - 1) * 30).Take(30).ToListAsync(ct);
        return new(rows.Select(Map).ToArray(), page, 30, await db.RefundTransactions.CountAsync(ct));
    }
    public async Task<FinanceRefund> CreateRefundAsync(Guid actor, FinanceRefundRequest request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.RefundCreate, ct);
        Amount(request.Amount); var reason = Text(request.Reason, 2000);
        if (request.RequestKey == Guid.Empty) throw Error("INVALID_REQUEST_KEY", "A request key is required.", 400);
        return await Mutate(request.TransactionId, async () => {
            var actorProfile = await db.UserProfiles.Where(p => p.AccountId == actor).Select(p => p.Id).SingleAsync(ct);
            var old = await db.RefundTransactions.SingleOrDefaultAsync(r => r.RequestKey == request.RequestKey, ct);
            if (old != null)
            {
                if (old.RequestedByUserId != actorProfile || old.TransactionId != request.TransactionId || old.Amount != request.Amount || old.Reason != reason) throw Error("IDEMPOTENCY_CONFLICT", "Refund request key has a different payload.");
                return Map(old);
            }
            var payment = await db.Transactions.Include(t => t.RefundTransactions).SingleOrDefaultAsync(t => t.Id == request.TransactionId, ct) ?? throw Error("NOT_FOUND", "Payment not found.", 404);
            if (payment.Status != PaymentStatus.Success || request.Amount > payment.Amount - RefundReservations.Reserved(payment.RefundTransactions)) throw Error("INVALID_REFUND_AMOUNT", "Refund exceeds the unreserved successful payment balance.");
            var now = DateTimeOffset.UtcNow;
            var row = new RefundTransaction { Id = Guid.NewGuid(), RequestKey = request.RequestKey, TransactionId = payment.Id, Amount = request.Amount, Reason = reason, PaymentProvider = payment.PaymentMethod ?? "Manual", Approval = RefundApproval.Pending,
                Status = PaymentStatus.Pending, RequestSource = RefundRequestSource.Admin, RequestedByUserId = actorProfile, RequestedAt = now, CreatedAt = now, UpdatedAt = now };
            db.Add(row); payment.UpdatedAt = now; return Map(row);
        }, ct);
    }
    public async Task<FinanceRefund> ReviewRefundAsync(Guid actor, Guid id, FinanceReviewRequest request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.RefundReview, ct); var reason = Text(request.Reason, 2000);
        var paymentId = await db.RefundTransactions.Where(r => r.Id == id).Select(r => (Guid?)r.TransactionId).SingleOrDefaultAsync(ct) ?? throw Error("NOT_FOUND", "Refund not found.", 404);
        return await Mutate(paymentId, async () => {
            var row = await db.RefundTransactions.Include(r => r.Transaction).SingleAsync(r => r.Id == id, ct);
            var creator = await db.UserProfiles.Where(p => p.Id == row.RequestedByUserId).Select(p => (Guid?)p.AccountId).SingleOrDefaultAsync(ct);
            if (creator == actor) throw Error("SECOND_REVIEWER_REQUIRED", "Another authorized staff member must review this refund.", 403);
            if (row.Approval != RefundApproval.Pending || row.Status != PaymentStatus.Pending || row.DispatchStartedAt != null) throw Error("INVALID_STATE", "Refund cannot be reviewed in its current state.");
            row.Approval = request.Approve ? RefundApproval.Approved : RefundApproval.Rejected;
            if (!request.Approve) row.Status = PaymentStatus.Failed;
            row.ReviewedByAccountId = actor; row.ReviewedAt = DateTimeOffset.UtcNow; row.ReviewReason = reason; row.UpdatedAt = row.ReviewedAt.Value; row.Transaction.UpdatedAt = row.UpdatedAt;
            return Map(row);
        }, ct);
    }
    public async Task<FinanceRefund> ConfirmRefundAsync(Guid actor, Guid id, PayoutConfirmation request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.PayoutConfirm, ct);
        var paying = Text(request.PayingAccount).ToUpperInvariant(); var reference = Text(request.BankReference).ToUpperInvariant();
        var paymentId = await db.RefundTransactions.Where(r => r.Id == id).Select(r => (Guid?)r.TransactionId).SingleOrDefaultAsync(ct) ?? throw Error("NOT_FOUND", "Refund not found.", 404);
        return await Mutate(paymentId, async () => {
            var row = await db.RefundTransactions.Include(r => r.Transaction).SingleAsync(r => r.Id == id, ct);
            if (row.Status == PaymentStatus.Success && row.ManualPayingAccount == paying && row.ManualBankReference == reference) return Map(row);
            if (row.Approval is not (RefundApproval.Approved or RefundApproval.NotRequired) || row.Status != PaymentStatus.Pending || row.DispatchStartedAt != null || row.Transaction.Status != PaymentStatus.Success)
                throw Error("INVALID_STATE", "Only an approved, undispatched manual refund may be confirmed.");
            // Manual-only provider: a real transfer must already have happened outside this application.
            if (await db.RefundTransactions.AnyAsync(r => r.ManualPayingAccount == paying && r.ManualBankReference == reference, ct) || await db.Set<WalletWithdrawal>().AnyAsync(w => w.PayingAccount == paying && w.BankReference == reference, ct))
                throw Error("DUPLICATE_TRANSFER", "Bank evidence already records a transfer.");
            row.ManualPayingAccount = paying; row.ManualBankReference = reference; row.RefundRef = reference; row.ConfirmedByAccountId = actor;
            row.Status = PaymentStatus.Success; row.CompletedAt = DateTimeOffset.UtcNow; row.UpdatedAt = row.CompletedAt; row.FailureReason = null; row.NextReconciliationAt = null; row.Transaction.UpdatedAt = row.UpdatedAt;
            return Map(row);
        }, ct);
    }
}
