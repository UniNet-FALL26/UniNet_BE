using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class TransactionService(UniNetDbContext db, IPaymentProvider provider,
    IConfiguration configuration, ILogger<TransactionService> logger) : ITransactionService
{
    public async Task<TransactionResponse> CreateAsync(Guid verificationId, Guid accountId, CancellationToken ct = default)
    {
        var options = PaymentOptions.Load(configuration);
        var attempt = await AtomicAsync(async () =>
        {
            var verification = await GetOwnedVerificationAsync(verificationId, accountId, ct);
            Require(verification.Status == ProjectVerificationStatus.Pending && !verification.FinalizedAt.HasValue,
                "INVALID_VERIFICATION_STATE", "Only pending, unfinalized verifications can be paid.", 409);
            Require(verification.Project.Status == ProjectStatus.Completed, "PROJECT_NOT_COMPLETED",
                "Only completed projects are eligible for verification payment.", 409);
            await new VerificationEligibility(db, configuration).RequireCaseAsync(verification, ct);
            var attempts = await db.Transactions.AsNoTracking()
                .Where(t => t.VerificationTransaction != null && t.VerificationTransaction.VerificationId == verificationId)
                .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(ct);
            Require(!attempts.Any(t => t.Status == PaymentStatus.Success), "VERIFICATION_ALREADY_PAID",
                "This verification already has a successful payment.", 409);
            var reservationNow = DateTimeOffset.UtcNow;
            Require(verification.BookingStatus == VerificationBookingStatus.Held && verification.ReservationId.HasValue &&
                verification.ReservedUntil > reservationNow && verification.ScheduledStartAt > reservationNow,
                "RESERVATION_REQUIRED", "Hold an available slot before checkout; expired reservations cannot be paid.", 409);
            var pending = attempts.FirstOrDefault(t => t.Status == PaymentStatus.Pending);
            if (pending != null)
            {
                var association = await db.VerificationTransactions.SingleAsync(t => t.TransactionId == pending.Id, ct);
                Require(association.ReservationId == verification.ReservationId && pending.ExpiredAt > reservationNow &&
                    pending.ExpiredAt == verification.ReservedUntil, "PAYMENT_RECONCILIATION_REQUIRED",
                    "Resolve the existing checkout before requesting another payment.", 409);
                return (Transaction: pending, Created: false);
            }

            options.RequireCheckoutConfiguration(requireLinkDuration: false);
            provider.EnsureConfigured();
            var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == verification.PackageId, ct);
            Require(package != null && package.IsActive && package.Type == PackageType.Verification &&
                package.Role == TargetRole.Student, "INVALID_VERIFICATION_PACKAGE",
                "An active Student verification package is required.", 409);
            Require(package.Price > 0 && package.Price == decimal.Truncate(package.Price) && package.Price <= int.MaxValue,
                "INVALID_PAYMENT_AMOUNT", "The package price must be a positive whole VND amount supported by PayOS.", 409);
            var now = DateTimeOffset.UtcNow;
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(), UserId = verification.RequestByUserId, PackageId = package.Id,
                Amount = package.Price, Status = PaymentStatus.Pending, PaymentMethod = provider.Name,
                // 52-bit random codes remain exact in both .NET Int64 and JavaScript numbers.
                ProviderOrderCode = ((BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) &
                    ((1UL << 52) - 1)) + 1).ToString(CultureInfo.InvariantCulture),
                ExpiredAt = verification.ReservedUntil, CreatedAt = now, UpdatedAt = now,
                VerificationTransaction = new()
                {
                    VerificationId = verificationId, ReservationId = verification.ReservationId, CreatedAt = now, UpdatedAt = now
                }
            };
            verification.UpdatedAt = now;
            db.Transactions.Add(transaction);
            return (Transaction: transaction, Created: true);
        }, ct);

        if (!attempt.Created) return TransactionMapping.ToResponse(attempt.Transaction, verificationId);
        var transaction = attempt.Transaction;
        ProviderPaymentResult result;
        try
        {
            // Local pending row and association are committed before any external request.
            result = await provider.CreatePaymentAsync(new(transaction.ProviderOrderCode!, (long)transaction.Amount,
                "UniNet verification", options.ReturnUrl!, options.CancelUrl!, transaction.ExpiredAt!.Value), ct);
        }
        catch (PaymentProviderException ex)
        {
            logger.LogWarning("Payment creation {TransactionId} / {OrderCode}: {Code}",
                transaction.Id, transaction.ProviderOrderCode, ex.Code);
            return await RecordProviderProblemAsync(transaction.Id, ex, creating: true);
        }
        // A disconnected frontend must not prevent saving a provider result already received.
        using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await ApplyAsync(transaction.Id, result, persistence.Token);
    }

    public async Task<TransactionsResponse> GetHistoryAsync(Guid verificationId, Guid accountId,
        int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        await GetOwnedVerificationAsync(verificationId, accountId, ct);
        Require(page >= 1 && pageSize is >= 1 and <= 100 && (long)(page - 1) * pageSize <= int.MaxValue,
            "INVALID_PAGINATION", "Page must be positive and pageSize must be between 1 and 100.");
        var query = db.Transactions.AsNoTracking().Where(t => t.VerificationTransaction != null &&
            t.VerificationTransaction.VerificationId == verificationId);
        var total = await query.CountAsync(ct);
        var transactions = await query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize),
            transactions.Select(t => TransactionMapping.ToResponse(t, verificationId)).ToList());
    }

    public async Task<TransactionResponse> GetAsync(Guid verificationId, Guid transactionId, Guid accountId,
        CancellationToken ct = default)
    {
        await GetOwnedVerificationAsync(verificationId, accountId, ct);
        var transaction = await db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transactionId &&
            t.VerificationTransaction != null && t.VerificationTransaction.VerificationId == verificationId, ct)
            ?? throw NotFound();
        return TransactionMapping.ToResponse(transaction, verificationId);
    }

    public async Task<TransactionResponse> ReconcileAsync(Guid transactionId, Guid accountId, CancellationToken ct = default)
    {
        var transaction = await db.Transactions.AsNoTracking().Include(t => t.VerificationTransaction)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct) ?? throw NotFound();
        Require(transaction.VerificationTransaction != null, "TRANSACTION_NOT_FOUND", "Verification payment not found.", 404);
        await GetOwnedVerificationAsync(transaction.VerificationTransaction.VerificationId, accountId, ct);
        Require(PaymentOptions.Load(configuration).AllowUserReconciliation, "RECONCILIATION_DISABLED",
            "User-triggered payment reconciliation is disabled.", 403);
        Require(transaction.PaymentMethod == provider.Name && !string.IsNullOrWhiteSpace(transaction.ProviderOrderCode),
            "UNSUPPORTED_PAYMENT_PROVIDER", "This payment provider is not supported for reconciliation.", 409);
        if (transaction.Status == PaymentStatus.Success)
            return TransactionMapping.ToResponse(transaction, transaction.VerificationTransaction.VerificationId);
        ProviderPaymentResult result;
        try { result = await provider.GetPaymentStatusAsync(transaction.ProviderOrderCode, ct); }
        catch (PaymentProviderException ex)
        {
            logger.LogWarning("Payment reconciliation {TransactionId} / {OrderCode}: {Code}",
                transaction.Id, transaction.ProviderOrderCode, ex.Code);
            return await RecordProviderProblemAsync(transactionId, ex, creating: false);
        }
        using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await ApplyAsync(transactionId, result, persistence.Token);
    }

    public async Task<PaymentWebhookResponse> HandlePayOSWebhookAsync(JsonElement payload, CancellationToken ct = default)
    {
        Require(provider.Name == "PayOS", "UNSUPPORTED_PAYMENT_PROVIDER", "PayOS webhook provider is unavailable.", 503);
        var result = provider.VerifyWebhook(payload);
        var transactionId = await db.Transactions.AsNoTracking()
            .Where(t => t.PaymentMethod == provider.Name && t.ProviderOrderCode == result.OrderCode &&
                t.VerificationTransaction != null).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct);
        // PayOS sends signed test data when registering a webhook. Unmatched or other-use-case
        // events are acknowledged without changing the shared advertisement ledger.
        if (!transactionId.HasValue) return new(true, false);
        await ApplyAsync(transactionId.Value, result, ct);
        return new(true, result.Status == PaymentStatus.Success);
    }

    // Only the backend worker calls this; no user-triggered refund or reconciliation authorization is bypassed by a route.
    public async Task ReconcilePendingInBackgroundAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-5);
        var pending = await db.Transactions.AsNoTracking().Where(t => t.Status == PaymentStatus.Pending &&
            t.VerificationTransaction != null && t.PaymentMethod == provider.Name && t.ProviderOrderCode != null &&
            t.UpdatedAt <= cutoff).OrderBy(t => t.UpdatedAt).Select(t => new { t.Id, t.ProviderOrderCode }).Take(50).ToListAsync(ct);
        foreach (var payment in pending)
        {
            db.ChangeTracker.Clear();
            try
            {
                var result = await provider.GetPaymentStatusAsync(payment.ProviderOrderCode!, ct);
                await ApplyAsync(payment.Id, result, ct);
            }
            catch (PaymentProviderException ex) { await RecordProviderProblemAsync(payment.Id, ex, creating: false); }
            catch (TransactionException ex) when (ex.Code != "PAYMENT_CONFLICT")
            {
                logger.LogError("Payment reconciliation needs review: {TransactionId} / {Code}", payment.Id, ex.Code);
                db.ChangeTracker.Clear();
                await db.Transactions.Where(t => t.Id == payment.Id && t.Status == PaymentStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedAt, now)
                        .SetProperty(t => t.FailureReason, ex.Code), ct);
            }
        }
    }

    private Task<TransactionResponse> ApplyAsync(Guid transactionId, ProviderPaymentResult result, CancellationToken ct) =>
        AtomicAsync(async () =>
        {
            var transaction = await LoadForUpdateAsync(transactionId, ct);
            var verification = transaction.VerificationTransaction!.Verification;
            Require(transaction.PaymentMethod == provider.Name && transaction.ProviderOrderCode == result.OrderCode &&
                transaction.Amount == result.AmountVnd && result.Currency == "VND" &&
                !string.IsNullOrWhiteSpace(result.PaymentLinkId) && result.PaymentLinkId.Length <= 255 &&
                (transaction.ProviderPaymentLinkId == null || transaction.ProviderPaymentLinkId == result.PaymentLinkId),
                "PAYMENT_DATA_MISMATCH", "Provider order, amount, currency or payment-link ID does not match this transaction.", 409);
            Require(transaction.PackageId == verification.PackageId && transaction.UserId == verification.RequestByUserId,
                "PAYMENT_ASSOCIATION_MISMATCH", "Payment does not belong to this verification's requester and package.", 409);
            if (transaction.Status == PaymentStatus.Success)
                return TransactionMapping.ToResponse(transaction, verification.Id);
            Require(result.Status != PaymentStatus.Success || !result.AmountPaidVnd.HasValue ||
                result.AmountPaidVnd >= transaction.Amount, "PAYMENT_DATA_MISMATCH",
                "PayOS has not confirmed the full payment amount.", 409);
            var now = DateTimeOffset.UtcNow;
            transaction.ProviderPaymentLinkId = result.PaymentLinkId;
            if (result.CheckoutUrl != null)
            {
                Require(result.CheckoutUrl.Length <= 2048 && PaymentOptions.IsWebUrl(result.CheckoutUrl),
                    "PAYMENT_DATA_MISMATCH", "Provider checkout URL is invalid.", 409);
                transaction.PaymentUrl = result.CheckoutUrl;
            }
            if (result.Status == PaymentStatus.Success)
            {
                var alreadyPaid = await db.Transactions.AnyAsync(t => t.Id != transaction.Id &&
                    t.VerificationTransaction != null && t.VerificationTransaction.VerificationId == verification.Id &&
                    t.Status == PaymentStatus.Success, ct);
                // Preserve every real charge, but only the first successful charge is accepted.
                transaction.Status = PaymentStatus.Success;
                transaction.PaidAt = result.PaidAt?.ToUniversalTime() ?? now;
                transaction.PaymentRef = result.Reference?.Length <= 255 ? result.Reference : null;
                transaction.FailureReason = alreadyPaid ? TransactionMapping.DuplicatePaymentReview : null;
                if (!alreadyPaid)
                {
                    var validHold = verification.Status == ProjectVerificationStatus.Pending && verification.FinalizedAt == null &&
                        verification.BookingStatus == VerificationBookingStatus.Held && verification.ReservedUntil > now &&
                        verification.ScheduledStartAt > now && transaction.ExpiredAt > now &&
                        transaction.VerificationTransaction.ReservationId != null &&
                        transaction.VerificationTransaction.ReservationId == verification.ReservationId;
                    if (!validHold) transaction.FailureReason = TransactionMapping.LatePaymentReview;
                    else
                    {
                        try
                        {
                            await new VerificationEligibility(db, configuration).RequireCaseAsync(verification, ct);
                            if (!await new ProjectVerificationService(db, configuration).HasConfirmationCapacityAsync(verification, ct))
                                transaction.FailureReason = TransactionMapping.BookingPaymentReview;
                        }
                        catch (ProjectVerificationException) { transaction.FailureReason = TransactionMapping.BookingPaymentReview; }
                        if (transaction.FailureReason == null)
                        {
                            verification.BookingStatus = VerificationBookingStatus.Confirmed;
                            verification.BookingConfirmedAt = now;
                            verification.AssignmentEligibleAt = now;
                        }
                    }
                }
                if (alreadyPaid) logger.LogError("Duplicate verification charge requires review: {TransactionId} / {VerificationId}",
                    transaction.Id, verification.Id);
            }
            else if (result.Status == PaymentStatus.Failed)
            {
                // A partially paid cancelled link requires operational review, not another checkout.
                transaction.Status = result.AmountPaidVnd == 0 ? PaymentStatus.Failed : PaymentStatus.Pending;
                transaction.FailureReason = result.AmountPaidVnd > 0 ? "PARTIAL_PAYMENT_REVIEW_REQUIRED" :
                    result.AmountPaidVnd == 0 ? "PROVIDER_PAYMENT_CLOSED" : "CLOSED_PAYMENT_RECONCILIATION_REQUIRED";
            }
            else if (transaction.Status == PaymentStatus.Pending)
                transaction.FailureReason = result.AmountPaidVnd > 0 ? "PARTIAL_PAYMENT_PENDING" : null;
            transaction.UpdatedAt = now;
            transaction.VerificationTransaction.UpdatedAt = now;
            // Touching the parent serializes simultaneous success events for different attempts.
            // Payment never finalizes verification or assigns a verificator.
            verification.UpdatedAt = now;
            return TransactionMapping.ToResponse(transaction, verification.Id);
        }, ct);

    private async Task<TransactionResponse> RecordProviderProblemAsync(Guid transactionId,
        PaymentProviderException problem, bool creating)
    {
        using var persistence = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await AtomicAsync(async () =>
        {
            var transaction = await LoadForUpdateAsync(transactionId, persistence.Token);
            if (transaction.Status != PaymentStatus.Success)
            {
                if (creating && problem.DefinitivelyRejected) transaction.Status = PaymentStatus.Failed;
                transaction.FailureReason = problem.Code;
                transaction.UpdatedAt = DateTimeOffset.UtcNow;
            }
            return TransactionMapping.ToResponse(transaction, transaction.VerificationTransaction!.VerificationId);
        }, persistence.Token);
    }

    private async Task<Transaction> LoadForUpdateAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.Transactions.Include(t => t.VerificationTransaction).ThenInclude(v => v!.Verification)
            .FirstOrDefaultAsync(t => t.Id == transactionId && t.VerificationTransaction != null, ct) ?? throw NotFound();
        // A provider call occurs between local transactions; discard tracked pre-call state.
        await db.Entry(transaction).ReloadAsync(ct);
        await db.Entry(transaction.VerificationTransaction!).ReloadAsync(ct);
        await db.Entry(transaction.VerificationTransaction!.Verification).ReloadAsync(ct);
        return transaction;
    }

    private async Task<ProjectVerification> GetOwnedVerificationAsync(Guid verificationId, Guid accountId, CancellationToken ct)
    {
        var profile = await db.UserProfiles.AsNoTracking().Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new TransactionException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(profile.Account.Status == AccountStatus.Active && profile.Account.Role == AccountRole.Student,
            "FORBIDDEN", "Only active Students can access verification payments.", 403);
        var verification = await db.ProjectVerifications.Include(v => v.Project)
            .FirstOrDefaultAsync(v => v.Id == verificationId, ct)
            ?? throw new TransactionException("VERIFICATION_NOT_FOUND", "Verification request not found.", 404);
        Require(verification.Project.CreatorId == profile.Id && verification.RequestByUserId == profile.Id,
            "FORBIDDEN", "Only the requesting project leader can access verification payments.", 403);
        return verification;
    }

    private async Task<T> AtomicAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            // PostgreSQL serializable predicate locks protect the cross-table one-pending/one-accepted
            // rules across API instances. Provider HTTP calls never run inside a DB transaction.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } })
        {
            throw new TransactionException("PAYMENT_CONFLICT", "Payment data changed concurrently. Reload and retry the same operation.", 409);
        }
    }

    private static TransactionException NotFound() => new("TRANSACTION_NOT_FOUND", "Verification payment not found.", 404);
    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new TransactionException(code, message, status);
    }
}
