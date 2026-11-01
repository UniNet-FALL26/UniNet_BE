using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record WalletCreditRequest(Guid AccountId, decimal Amount, string SourceReference, string Reason);
public sealed record WithdrawalRequest(Guid RequestKey, decimal Amount, string BankName, string BankAccountNumber, string BankAccountHolder);
public sealed record FinanceReviewRequest(bool Approve, string Reason);
public sealed record PayoutConfirmation(string PayingAccount, string BankReference);
public sealed record WalletBalance(decimal Balance, decimal Reserved, decimal Available);
public sealed record WalletPage(WalletBalance Balance, IReadOnlyList<WalletEntry> Entries, IReadOnlyList<WalletWithdrawal> Withdrawals);
public sealed record FinancePage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed partial class FinanceService(UniNetDbContext db)
{
    private static AuthException Error(string code, string message, int status = 409) => new(code, message, status);
    private static string Text(string? value, int max = 150)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw Error("INVALID_INPUT", "Required field is missing or too long.", 400);
        return value.Trim();
    }
    private static void Amount(decimal value)
    {
        if (value <= 0 || value > 999999999999999999m || decimal.Truncate(value) != value)
            throw Error("INVALID_AMOUNT", "Amount must be a positive whole VND value.", 400);
    }
    private async Task Owner(Guid owner, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == owner && a.Status == AccountStatus.Active && (a.Role == AccountRole.Student || a.Role == AccountRole.Partner), ct))
            throw Error("INVALID_WALLET", "Wallet requires an active Student or Partner account.", 403);
    }
    private async Task<T> Mutate<T>(Guid resource, Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({resource.ToString()}, 0))", ct);
            var result = await action(); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
        }
        catch (Exception ex) when (Conflict(ex))
        { db.ChangeTracker.Clear(); throw Error("FINANCE_CONFLICT", "Data changed concurrently. Reload and retry."); }
    }
    private static bool Conflict(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException or PostgresException { SqlState: "40001" or "40P01" or "23505" }) return true;
        return false;
    }
    private async Task<decimal> Sum(IQueryable<decimal> query, CancellationToken ct) => db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
        ? (await query.ToListAsync(ct)).Sum() : await query.SumAsync(ct);
    private async Task<WalletBalance> Balance(Guid owner, CancellationToken ct)
    {
        var balance = await Sum(db.Set<WalletEntry>().Where(e => e.AccountId == owner).Select(e => e.Amount), ct);
        var reserved = await Sum(db.Set<WalletWithdrawal>().Where(w => w.AccountId == owner && (w.State == WithdrawalState.Requested || w.State == WithdrawalState.Approved || w.State == WithdrawalState.Ordered)).Select(w => w.Amount), ct);
        return new(balance, reserved, balance - reserved);
    }
    public async Task<WalletBalance> WalletAsync(Guid owner, CancellationToken ct) { await Owner(owner, ct); return await Balance(owner, ct); }
    public async Task<WalletPage> HistoryAsync(Guid owner, CancellationToken ct)
    {
        await Owner(owner, ct);
        return new(await Balance(owner, ct), await db.Set<WalletEntry>().AsNoTracking().Where(e => e.AccountId == owner).OrderByDescending(e => e.CreatedAt).Take(100).ToListAsync(ct),
            await db.Set<WalletWithdrawal>().AsNoTracking().Where(e => e.AccountId == owner).OrderByDescending(e => e.CreatedAt).Take(100).ToListAsync(ct));
    }
    public async Task<WalletEntry> CreditAsync(Guid actor, WalletCreditRequest request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.WalletCredit, ct);
        Amount(request.Amount); var source = Text(request.SourceReference).ToUpperInvariant(); var reason = Text(request.Reason, 2000);
        if (source.StartsWith("WITHDRAWAL:", StringComparison.Ordinal)) throw Error("RESERVED_REFERENCE", "This source reference namespace is reserved for withdrawal debits.", 400);
        return await Mutate(request.AccountId, async () => {
            await Owner(request.AccountId, ct);
            var old = await db.Set<WalletEntry>().SingleOrDefaultAsync(e => e.AccountId == request.AccountId && e.SourceReference == source, ct);
            if (old != null)
            {
                if (old.Amount != request.Amount || old.Reason != reason || old.WithdrawalId != null) throw Error("IDEMPOTENCY_CONFLICT", "Income reference already has a different payload.");
                return old;
            }
            var entry = new WalletEntry { AccountId = request.AccountId, Amount = request.Amount, SourceReference = source, Reason = reason, ActorAccountId = actor };
            db.Add(entry); return entry;
        }, ct);
    }
    public async Task<WalletWithdrawal> RequestAsync(Guid owner, WithdrawalRequest request, CancellationToken ct)
    {
        await Owner(owner, ct); Amount(request.Amount);
        if (request.RequestKey == Guid.Empty) throw Error("INVALID_REQUEST_KEY", "A durable request key is required.", 400);
        var bank = Text(request.BankName); var number = Text(request.BankAccountNumber); var holder = Text(request.BankAccountHolder);
        return await Mutate(owner, async () => {
            var old = await db.Set<WalletWithdrawal>().SingleOrDefaultAsync(w => w.AccountId == owner && w.RequestKey == request.RequestKey, ct);
            if (old != null)
            {
                if (old.Amount != request.Amount || old.BankName != bank || old.BankAccountNumber != number || old.BankAccountHolder != holder) throw Error("IDEMPOTENCY_CONFLICT", "Request key already has a different payload.");
                return old;
            }
            if ((await Balance(owner, ct)).Available < request.Amount) throw Error("INSUFFICIENT_BALANCE", "Available income balance is insufficient.");
            var row = new WalletWithdrawal { AccountId = owner, RequestKey = request.RequestKey, Amount = request.Amount, BankName = bank, BankAccountNumber = number, BankAccountHolder = holder };
            db.Add(row); return row;
        }, ct);
    }
    private async Task<WalletWithdrawal> Withdrawal(Guid id, CancellationToken ct) => await db.Set<WalletWithdrawal>().SingleOrDefaultAsync(w => w.Id == id, ct) ?? throw Error("NOT_FOUND", "Withdrawal not found.", 404);
    private async Task<WalletWithdrawal> Change(Guid id, Func<WalletWithdrawal, Task> change, CancellationToken ct)
    {
        var owner = await db.Set<WalletWithdrawal>().Where(w => w.Id == id).Select(w => (Guid?)w.AccountId).SingleOrDefaultAsync(ct) ?? throw Error("NOT_FOUND", "Withdrawal not found.", 404);
        return await Mutate(owner, async () => { var row = await Withdrawal(id, ct); await change(row); row.Version = Guid.NewGuid(); return row; }, ct);
    }
    public async Task<WalletWithdrawal> CancelAsync(Guid owner, Guid id, CancellationToken ct)
    {
        await Owner(owner, ct);
        return await Change(id, row => {
            if (row.AccountId != owner) throw Error("NOT_FOUND", "Withdrawal not found.", 404);
            if (row.State == WithdrawalState.Cancelled) return Task.CompletedTask;
            if (row.State != WithdrawalState.Requested) throw Error("INVALID_STATE", "Only a requested withdrawal can be cancelled.");
            row.State = WithdrawalState.Cancelled; return Task.CompletedTask;
        }, ct);
    }
    public async Task<WalletWithdrawal> ReviewAsync(Guid actor, Guid id, FinanceReviewRequest request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.WithdrawalReview, ct); var reason = Text(request.Reason, 2000);
        return await Change(id, row => {
            if (request.Approve ? row.State != WithdrawalState.Requested : row.State is not (WithdrawalState.Requested or WithdrawalState.Approved)) throw Error("INVALID_STATE", "Withdrawal is no longer reviewable.");
            row.State = request.Approve ? WithdrawalState.Approved : WithdrawalState.Rejected;
            if (request.Approve) { row.ReviewedBy = actor; row.ReviewedAt = DateTimeOffset.UtcNow; row.ReviewReason = reason; }
            else { row.RejectedBy = actor; row.RejectedAt = DateTimeOffset.UtcNow; row.RejectionReason = reason; }
            return Task.CompletedTask;
        }, ct);
    }
    public async Task<WalletWithdrawal> OrderAsync(Guid actor, Guid id, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.PayoutCreate, ct);
        return await Change(id, row => {
            if (row.State == WithdrawalState.Ordered) return Task.CompletedTask;
            if (row.State != WithdrawalState.Approved) throw Error("INVALID_STATE", "Approval is required before creating the payout order.");
            row.State = WithdrawalState.Ordered; row.OrderedBy = actor; row.OrderedAt = DateTimeOffset.UtcNow; return Task.CompletedTask;
        }, ct);
    }
    public async Task<WalletWithdrawal> ConfirmAsync(Guid actor, Guid id, PayoutConfirmation request, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.PayoutConfirm, ct);
        var paying = Text(request.PayingAccount).ToUpperInvariant(); var reference = Text(request.BankReference).ToUpperInvariant();
        return await Change(id, async row => {
            if (row.State == WithdrawalState.Paid && row.PayingAccount == paying && row.BankReference == reference) return;
            if (row.State != WithdrawalState.Ordered) throw Error("INVALID_STATE", "Only an ordered withdrawal may be confirmed; evidence must match on retry.");
            if (await db.Set<WalletWithdrawal>().AnyAsync(w => w.PayingAccount == paying && w.BankReference == reference && w.Id != id, ct) || await db.RefundTransactions.AnyAsync(r => r.ManualPayingAccount == paying && r.ManualBankReference == reference, ct)) throw Error("DUPLICATE_TRANSFER", "Bank transaction already confirms another payout.");
            row.State = WithdrawalState.Paid; row.PayingAccount = paying; row.BankReference = reference; row.ConfirmedBy = actor; row.ConfirmedAt = DateTimeOffset.UtcNow;
            db.Add(new WalletEntry { AccountId = row.AccountId, Amount = -row.Amount, SourceReference = "withdrawal:" + row.Id, Reason = "Manual bank payout", WithdrawalId = row.Id, ActorAccountId = actor });
        }, ct);
    }
    public async Task<FinancePage<WalletWithdrawal>> WithdrawalsAsync(Guid actor, int page, CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, actor, NotificationPermissionPolicy.FinanceView, ct);
        page = Math.Clamp(page, 1, 1000000); var query = db.Set<WalletWithdrawal>().AsNoTracking();
        return new(await query.OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id).Skip((page - 1) * 30).Take(30).ToListAsync(ct), page, 30, await query.CountAsync(ct));
    }
}
