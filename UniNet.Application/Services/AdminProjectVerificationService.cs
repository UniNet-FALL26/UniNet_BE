using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AdminProjectVerificationService(UniNetDbContext db, IConfiguration? configuration = null) : IAdminProjectVerificationService
{
    public Task<ProjectVerificationResponse> RecoverPaidBookingAsync(Guid id, Guid accountId,
        RecoverPaidBookingRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        await AuthorizeAsync(accountId, ct);
        ValidateReason(request.Reason);
        var verification = await db.ProjectVerifications.Include(v => v.Project)
            .SingleOrDefaultAsync(v => v.Id == id, ct)
            ?? throw Error("VERIFICATION_NOT_FOUND", "Verification not found.", 404);
        var now = DateTimeOffset.UtcNow;
        if (verification.Status != ProjectVerificationStatus.Pending || verification.FinalizedAt != null ||
            verification.MeetingStartAt != null || verification.MeetingEndAt != null ||
            verification.ScheduledStartAt <= now || verification.ScheduledStartAt == null ||
            verification.ScheduledEndAt <= verification.ScheduledStartAt || verification.ScheduledEndAt == null ||
            verification.ReservationId == null ||
            verification.BookingStatus is not (VerificationBookingStatus.Expired or VerificationBookingStatus.Held))
            throw Error("BOOKING_RECOVERY_NOT_ALLOWED", "Only an unfinalized paid hold with a future meeting can be recovered.", 409);
        if (await db.VerificationAssignments.AnyAsync(a => a.ProjectVerificationId == id, ct) ||
            await db.VerificationReports.AnyAsync(r => r.ProjectVerificationId == id, ct))
            throw Error("BOOKING_RECOVERY_NOT_ALLOWED", "A case with assignments or reports cannot be recovered.", 409);
        var payment = await db.Transactions.Include(t => t.VerificationTransaction).Include(t => t.RefundTransactions)
            .SingleOrDefaultAsync(t => t.Id == request.TransactionId && t.VerificationTransaction != null &&
                t.VerificationTransaction.VerificationId == id, ct)
            ?? throw Error("PAYMENT_NOT_FOUND", "Payment does not belong to this verification.", 404);
        if (payment.Status != PaymentStatus.Success || payment.PackageId != verification.PackageId ||
            payment.UserId != verification.RequestByUserId ||
            payment.VerificationTransaction!.ReservationId != verification.ReservationId ||
            payment.FailureReason is not (TransactionMapping.LatePaymentReview or TransactionMapping.BookingPaymentReview) ||
            payment.RefundTransactions.Any(r => r.Status is PaymentStatus.Pending or PaymentStatus.Success) ||
            await db.Transactions.AnyAsync(t => t.Id != payment.Id && t.VerificationTransaction != null &&
                t.VerificationTransaction.VerificationId == id &&
                (t.Status == PaymentStatus.Pending || t.Status == PaymentStatus.Success), ct))
            throw Error("PAYMENT_NOT_RECOVERABLE", "Recovery requires one successful matching payment under booking review and no pending or completed refund.", 409);
        var settings = configuration ?? new ConfigurationBuilder().Build();
        await new VerificationEligibility(db, settings).RequireCaseAsync(verification, ct);
        if (!await new ProjectVerificationService(db, settings).HasConfirmationCapacityAsync(verification, ct))
            throw Error("BOOKING_CAPACITY_UNAVAILABLE", "The original meeting no longer has available capacity.", 409);
        verification.BookingStatus = VerificationBookingStatus.Confirmed;
        verification.BookingConfirmedAt = now;
        verification.AssignmentEligibleAt = now;
        verification.UpdatedAt = now;
        payment.FailureReason = null;
        payment.UpdatedAt = now;
        var recipient = await db.UserProfiles.Where(p => p.Id == verification.Project.CreatorId)
            .Select(p => p.AccountId).SingleAsync(ct);
        await NotificationService.EnqueueAsync(db, [recipient],
            new("verification.booking.recovered", "Verification", "Lịch xác minh đã được xác nhận",
                $"Admin đã xác nhận lại lịch từ giao dịch {payment.Id}. Lý do: {request.Reason.Trim()}",
                "ProjectVerification", id, EventKey: $"paid-booking-recovery:{id}:{payment.Id}", SenderUserId: accountId), ct);
        return ProjectVerificationMapping.ToResponse(verification);
    }, ct);

    private async Task<Guid> AuthorizeAsync(Guid accountId, CancellationToken ct)
    {
        var actor = await db.UserProfiles.AsNoTracking().Include(p => p.Account)
            .SingleOrDefaultAsync(p => p.AccountId == accountId, ct);
        if (actor == null || actor.Account.Role != AccountRole.Admin || actor.Account.Status != AccountStatus.Active)
            throw Error("FORBIDDEN", "An active admin account is required.", 403);
        if (!await NotificationPermissionPolicy.HasAsync(db, accountId, NotificationPermissionPolicy.EvidenceReview, ct))
            throw Error("FORBIDDEN", "Bạn không có quyền quản lý xác minh dự án.", 403);
        return actor.Id;
    }

    public async Task<ProjectVerificationsResponse> ListAsync(Guid accountId, ProjectVerificationStatus? status = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, int page = 1, int pageSize = 10,
        bool pendingAssignment = false, bool overdue = false, CancellationToken ct = default)
    {
        await AuthorizeAsync(accountId, ct);
        if (status.HasValue && !Enum.IsDefined(status.Value)) throw Error("INVALID_STATUS", "Invalid verification status.");
        if (from.HasValue && to.HasValue && from > to) throw Error("INVALID_DATE_RANGE", "From must not exceed to.");
        from = from?.ToUniversalTime(); to = to?.ToUniversalTime();
        var query = db.ProjectVerifications.AsNoTracking().Include(v => v.Project).AsQueryable();
        if (status.HasValue) query = query.Where(v => v.Status == status);
        if (from.HasValue) query = query.Where(v => v.SubmittedAt >= from);
        if (to.HasValue) query = query.Where(v => v.SubmittedAt <= to);
        if (pendingAssignment) query = query.Where(v => v.Status == ProjectVerificationStatus.Pending &&
            v.FinalizedAt == null && v.BookingStatus == VerificationBookingStatus.Confirmed &&
            v.AssignmentEligibleAt != null && !v.Assignments.Any(a => a.Status != VerificationAssignmentStatus.Cancelled));
        if (overdue)
        {
            var now = DateTimeOffset.UtcNow;
            query = query.Where(v => v.Status == ProjectVerificationStatus.Pending && v.FinalizedAt == null &&
                ((v.ScheduledEndAt < now && v.MeetingEndAt == null) ||
                 v.Assignments.Any(a => a.CompletedAt == null && a.ReplacedAt == null &&
                     (a.Status == VerificationAssignmentStatus.Assigned || a.Status == VerificationAssignmentStatus.Overdue) &&
                     (a.Status == VerificationAssignmentStatus.Overdue || a.AssignmentDueAt < now || a.ReportDueAt < now))));
        }
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(v => v.SubmittedAt).ThenBy(v => v.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize), items.Select(ProjectVerificationMapping.ToResponse).ToList());
    }

    private async Task<ProjectVerification> LoadAsync(Guid id, CancellationToken ct) =>
        await db.ProjectVerifications.AsNoTracking().Include(v => v.Project).Include(v => v.EvidenceModeration).ThenInclude(m => m!.Attempts)
        .Include(v => v.Assignments).ThenInclude(a => a.Report)
        .Include(v => v.VerificationTransactions).ThenInclude(t => t.Transaction).ThenInclude(t => t.RefundTransactions)
        .AsSplitQuery().FirstOrDefaultAsync(v => v.Id == id, ct)
        ?? throw Error("VERIFICATION_NOT_FOUND", "Verification not found.", 404);

    public async Task<AdminVerificationDetailResponse> GetDetailAsync(Guid id, Guid accountId, CancellationToken ct = default)
    {
        await AuthorizeAsync(accountId, ct);
        var detail = ProjectVerificationMapping.ToDetailResponse(await LoadAsync(id, ct));
        return new(detail);
    }

    public async Task<List<VerificationReportResponse>> GetReportsAsync(Guid id, Guid accountId, CancellationToken ct = default)
    {
        await AuthorizeAsync(accountId, ct);
        var verification = await LoadAsync(id, ct);
        return verification.Assignments.Where(a => a.Report != null)
            .OrderBy(a => a.Report!.SubmittetAt).ThenBy(a => a.Id)
            .Select(a => ProjectVerificationMapping.ToReportResponse(a.Report!) with { Id = a.Report!.Id }).ToList();
    }

    public async Task<List<VerificationRefundResponse>> GetRefundsAsync(Guid id, Guid accountId, CancellationToken ct = default)
    {
        var result = await GetDetailAsync(id, accountId, ct);
        return result.Detail.Payments.SelectMany(p => p.Refunds).ToList();
    }

    public Task<VerificationRefundResponse> CreateRefundAsync(Guid id, Guid accountId, CreateVerificationRefundRequest request,
        CancellationToken ct = default) => MutateAsync(async () =>
    {
        var actor = await AuthorizeAsync(accountId, ct);
        ValidateReason(request.Reason);
        if (!await db.ProjectVerifications.AnyAsync(v => v.Id == id, ct)) throw Error("VERIFICATION_NOT_FOUND", "Verification not found.", 404);
        var payment = await db.Transactions.Include(t => t.RefundTransactions).SingleOrDefaultAsync(t =>
            t.Id == request.TransactionId && t.VerificationTransaction != null && t.VerificationTransaction.VerificationId == id, ct)
            ?? throw Error("PAYMENT_NOT_FOUND", "Payment does not belong to this verification.", 404);
        if (payment.Status != PaymentStatus.Success) throw Error("PAYMENT_NOT_SUCCESSFUL", "Only successful payments can be refunded.", 409);
        var reserved = payment.RefundTransactions.Where(r => r.Status == PaymentStatus.Pending || r.Status == PaymentStatus.Success).Sum(r => r.Amount);
        if (request.Amount <= 0 || decimal.Truncate(request.Amount) != request.Amount || request.Amount > payment.Amount - reserved)
            throw Error("INVALID_REFUND_AMOUNT", "Refund must be a positive whole VND amount within the unreserved payment balance.", 409);
        var now = DateTimeOffset.UtcNow;
        var refund = new RefundTransaction { Id = Guid.NewGuid(), TransactionId = payment.Id, Amount = request.Amount,
            Status = PaymentStatus.Pending, PaymentProvider = payment.PaymentMethod ?? "Manual", Reason = request.Reason.Trim(), RequestedByUserId = actor, RequestedAt = now, RequestSource = RefundRequestSource.Admin, CreatedAt = now, UpdatedAt = now };
        payment.UpdatedAt = now;
        db.RefundTransactions.Add(refund);
        return new VerificationRefundResponse(refund.Id, refund.Amount, refund.Status, refund.Reason, null, refund.RequestedByUserId, refund.RequestedAt, refund.RequestSource);
    }, ct);

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
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex is PostgresException { SqlState: "40001" or "40P01" or "23505" } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: "40001" or "40P01" or "23505" } })
        { throw Error("VERIFICATION_CONFLICT", "Data changed concurrently. Reload and try again.", 409); }
    }
    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000) throw Error("INVALID_REASON", "Reason must contain 1 to 2000 characters.");
    }
    private static ProjectVerificationException Error(string code, string message, int status = 400) => new(code, message, status);
}
