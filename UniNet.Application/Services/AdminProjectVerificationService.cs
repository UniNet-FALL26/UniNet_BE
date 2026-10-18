using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AdminProjectVerificationService(UniNetDbContext db) : IAdminProjectVerificationService
{
    private async Task<Guid> AuthorizeAsync(Guid accountId, CancellationToken ct)
    {
        var actor = await db.UserProfiles.AsNoTracking().Include(p => p.Account)
            .SingleOrDefaultAsync(p => p.AccountId == accountId, ct);
        if (actor == null || actor.Account.Role != AccountRole.Admin || actor.Account.Status != AccountStatus.Active)
            throw Error("FORBIDDEN", "An active admin account is required.", 403);
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
        .Include(v => v.Assignments).ThenInclude(a => a.Report).ThenInclude(r => r!.Versions)
        .Include(v => v.VerificationTransactions).ThenInclude(t => t.Transaction).ThenInclude(t => t.RefundTransactions)
        .AsSplitQuery().FirstOrDefaultAsync(v => v.Id == id, ct)
        ?? throw Error("VERIFICATION_NOT_FOUND", "Verification not found.", 404);

    public async Task<AdminVerificationDetailResponse> GetDetailAsync(Guid id, Guid accountId, CancellationToken ct = default)
    {
        await AuthorizeAsync(accountId, ct);
        var detail = ProjectVerificationMapping.ToDetailResponse(await LoadAsync(id, ct));
        var actions = await db.Set<VerificationAdminAction>().AsNoTracking().Where(a => a.VerificationId == id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync(ct);
        return new(detail, actions.Select(MapAction).ToList());
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
            Status = PaymentStatus.Pending, PaymentProvider = payment.PaymentMethod ?? "Manual", Reason = request.Reason.Trim(), CreatedAt = now, UpdatedAt = now };
        payment.UpdatedAt = now;
        db.RefundTransactions.Add(refund);
        db.Set<VerificationAdminAction>().Add(new() { Id = Guid.NewGuid(), VerificationId = id, ActorUserId = actor,
            Decision = "RefundRequested", Reason = refund.Reason, RefundId = refund.Id, CreatedAt = now });
        return new VerificationRefundResponse(refund.Id, refund.Amount, refund.Status, refund.Reason, null);
    }, ct);

    public Task<VerificationAdminActionResponse> ReviewAsync(Guid id, Guid accountId, ReviewVerificationRequest request,
        CancellationToken ct = default) => MutateAsync(async () =>
    {
        var actor = await AuthorizeAsync(accountId, ct);
        ValidateReason(request.Reason);
        if (!Enum.IsDefined(request.Decision)) throw Error("INVALID_REVIEW_DECISION", "Invalid review decision.");
        var verification = await LoadAsync(id, ct);
        if (!verification.Assignments.Any(a => a.Report != null)) throw Error("REPORT_REQUIRED", "A report is required for review.", 409);
        var action = new VerificationAdminAction { Id = Guid.NewGuid(), VerificationId = id, ActorUserId = actor,
            Decision = request.Decision.ToString(), Reason = request.Reason.Trim(), CreatedAt = DateTimeOffset.UtcNow };
        db.Set<VerificationAdminAction>().Add(action);
        return MapAction(action);
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
    private static VerificationAdminActionResponse MapAction(VerificationAdminAction a) => new(a.Id, a.ActorUserId, a.Decision, a.Reason, a.RefundId, a.CreatedAt);
    private static ProjectVerificationException Error(string code, string message, int status = 400) => new(code, message, status);
}
