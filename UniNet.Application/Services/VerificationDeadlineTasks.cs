using System.Data;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class VerificationDeadlineTasks(UniNetDbContext db, VerificationNotifications notifications, Microsoft.Extensions.Configuration.IConfiguration configuration)
{
    public const string AssignmentTimeout = "SYSTEM_TIMEOUT:ASSIGNMENT";
    public const string ReplacementTimeout = "SYSTEM_TIMEOUT:REPLACEMENT";

    public async Task RefundTimeoutsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var assignmentCutoff = now.AddHours(-24);
        var replacementCutoff = now.AddHours(-48);
        var ids = await db.ProjectVerifications.AsNoTracking().Where(v =>
            (v.Status == ProjectVerificationStatus.Pending && v.FinalizedAt == null &&
                ((!v.Assignments.Any(a => a.Status != VerificationAssignmentStatus.Cancelled) &&
                  v.VerificationTransactions.Any(p => p.Transaction.Status == PaymentStatus.Success &&
                      (p.Transaction.PaidAt ?? p.Transaction.CreatedAt) <= assignmentCutoff)) ||
                 v.Assignments.Any(old => old.ReplacedBy != null && old.ReplacedBy.AssignedAt <= replacementCutoff &&
                     (old.ReplacedBy.Status == VerificationAssignmentStatus.Assigned || old.ReplacedBy.Status == VerificationAssignmentStatus.Overdue) && old.ReplacedBy.Report == null))) ||
            (v.Status == ProjectVerificationStatus.Cancelled &&
                (v.CancellationReason == AssignmentTimeout || v.CancellationReason == ReplacementTimeout) &&
                v.VerificationTransactions.Any(p => p.Transaction.Status == PaymentStatus.Success &&
                    !p.Transaction.RefundTransactions.Any())))
            .OrderBy(v => v.UpdatedAt).Select(v => v.Id).Take(100).ToListAsync(ct);
        foreach (var id in ids) await RefundTimeoutAsync(id, now, ct);
    }

    public async Task RefundTimeoutAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var v = await db.ProjectVerifications.Include(v => v.Assignments).ThenInclude(a => a.Report)
            .Include(v => v.Assignments).ThenInclude(a => a.ReplacedBy)
            .Include(v => v.VerificationTransactions).ThenInclude(p => p.Transaction).ThenInclude(p => p.RefundTransactions)
            .AsSplitQuery().SingleAsync(v => v.Id == id, ct);
        var payments = v.VerificationTransactions.Select(p => p.Transaction).Where(p => p.Status == PaymentStatus.Success).ToList();
        var reason = v.CancellationReason;
        var alreadyTimedOut = v.Status == ProjectVerificationStatus.Cancelled && reason is AssignmentTimeout or ReplacementTimeout;
        if (!alreadyTimedOut)
        {
            if (v.Status != ProjectVerificationStatus.Pending || v.FinalizedAt != null || payments.Count == 0 ||
                v.Assignments.Any(a => a.Report != null)) return;
            if (!v.Assignments.Any(a => a.Status != VerificationAssignmentStatus.Cancelled) &&
                payments.Min(p => p.PaidAt ?? p.CreatedAt) <= now.AddHours(-24)) reason = AssignmentTimeout;
            else if (v.Assignments.Any(old => old.ReplacedBy != null &&
                (old.ReplacedBy.Status == VerificationAssignmentStatus.Assigned || old.ReplacedBy.Status == VerificationAssignmentStatus.Overdue) && old.ReplacedBy.Report == null &&
                old.ReplacedBy.AssignedAt <= now.AddHours(-48))) reason = ReplacementTimeout;
            else return;
            v.Status = ProjectVerificationStatus.Cancelled;
            v.CancellationReason = reason; v.CancelledAt = now; v.UpdatedAt = now;
            foreach (var a in v.Assignments.Where(a => a.Status is VerificationAssignmentStatus.Assigned or VerificationAssignmentStatus.Overdue))
            { a.Status = VerificationAssignmentStatus.Cancelled; a.UpdatedAt = now; }
        }
        foreach (var payment in payments)
        {
            // Failed automatic refunds also retain their reservation until an operator resolves them.
            // Never silently generate a new provider request after a definitive failure.
            var reserved = payment.RefundTransactions.Where(r => r.Status != PaymentStatus.Failed ||
                r.Reason == AssignmentTimeout || r.Reason == ReplacementTimeout).Sum(r => r.Amount);
            var balance = payment.Amount - reserved;
            if (balance <= 0) continue;
            db.RefundTransactions.Add(new() { Id = Guid.NewGuid(), TransactionId = payment.Id, Amount = balance,
                Status = PaymentStatus.Pending, PaymentProvider = payment.PaymentMethod ?? "Manual", Reason = reason!,
                CreatedAt = now, UpdatedAt = now });
            // Serialize with concurrent manual refunds and webhook updates on the same payment.
            payment.UpdatedAt = now;
        }
        await notifications.AdminsAsync(v.Id, $"timeout:{v.Id}", $"Verification cancelled; full refund reserved. Reason: {reason}.", now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task RemindAndEscalateAsync(DateTimeOffset now, CancellationToken ct)
    {
        var meetingCutoff = now.AddHours(-48);
        var reminderCutoff = now.AddHours(-24);
        var ids = await db.VerificationAssignments.AsNoTracking().Where(a => a.ProjectVerification.Status == ProjectVerificationStatus.Pending &&
            a.ProjectVerification.FinalizedAt == null && a.Report == null && a.ReplacedAt == null && a.CompletedAt == null &&
            (a.Status == VerificationAssignmentStatus.Assigned || a.Status == VerificationAssignmentStatus.Overdue) &&
            !db.VerificationAssignments.Any(old => old.ReplacedBy != null && old.ReplacedBy.Id == a.Id) &&
            ((a.ReminderSentAt == null && a.ProjectVerification.MeetingEndAt <= meetingCutoff) || a.ReminderSentAt <= reminderCutoff))
            .OrderBy(a => a.AssignedAt).Select(a => a.Id).Take(100).ToListAsync(ct);
        foreach (var id in ids) await RemindOrReplaceAsync(id, now, ct);
    }

    public async Task RemindOrReplaceAsync(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var a = await db.VerificationAssignments.Include(a => a.ProjectVerification).ThenInclude(v => v.Project)
            .Include(a => a.Report).Include(a => a.ReplacedBy)
            .SingleAsync(a => a.Id == id, ct);
        var v = a.ProjectVerification;
        if (v.Status != ProjectVerificationStatus.Pending || v.FinalizedAt != null || a.Report != null || a.ReplacedAt != null ||
            a.CompletedAt != null || a.Status is not (VerificationAssignmentStatus.Assigned or VerificationAssignmentStatus.Overdue) ||
            await db.VerificationAssignments.AnyAsync(old => old.ReplacedBy != null && old.ReplacedBy.Id == a.Id, ct)) return;
        if (v.MeetingEndAt == null || v.MeetingEndAt > now.AddHours(-48)) return;
        a.ReportDueAt = v.MeetingEndAt.Value.AddHours(48);
        if (a.ReminderSentAt == null)
        {
            a.Status = VerificationAssignmentStatus.Overdue; a.ReminderSentAt = now;
            await notifications.AddAsync(v.Id, a.AssignedToUserId, $"report-reminder:{a.Id}",
                "Your verification report is overdue. Please submit it.", now, ct);
        }
        else if (a.ReminderSentAt <= now.AddHours(-24))
        {
            a.EscalatedAt ??= now;
            await notifications.AdminsAsync(v.Id, $"report-escalation:{a.Id}",
                "Report is still missing 24 hours after reminder; replacement is required.", now, ct);
            var admin = await db.UserProfiles.Where(p => p.Account.Role == AccountRole.Admin && p.Account.Status == AccountStatus.Active)
                .OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            var replacement = await db.UserProfiles.Where(p => p.Account.Role == AccountRole.Verificator && p.Account.Status == AccountStatus.Active &&
                p.Id != v.RequestByUserId && p.Id != v.Project.CreatorId &&
                !db.ProjectMembers.Any(m => m.ProjectId == v.ProjectId && m.UserId == p.Id) &&
                !db.VerificationAssignments.Any(old => old.ProjectVerificationId == v.Id && old.AssignedToUserId == p.Id) &&
                !db.VerificationAssignments.Any(active => active.AssignedToUserId == p.Id &&
                    (active.Status == VerificationAssignmentStatus.Assigned || active.Status == VerificationAssignmentStatus.Overdue) &&
                    active.ProjectVerification.Status == ProjectVerificationStatus.Pending))
                .OrderBy(p => p.Id).FirstOrDefaultAsync(ct);
            var eligible = true;
            try { await new VerificationEligibility(db, configuration).RequireCaseAsync(v, ct); }
            catch (UniNet.Application.ProjectVerificationException) { eligible = false; }
            eligible = eligible && await db.VerificationTransactions.AnyAsync(t => t.VerificationId == v.Id &&
                t.Transaction.Status == PaymentStatus.Success && t.Transaction.PackageId == v.PackageId &&
                t.Transaction.UserId == v.RequestByUserId && t.Transaction.FailureReason != TransactionMapping.DuplicatePaymentReview &&
                !t.Transaction.RefundTransactions.Any(r => r.Status == PaymentStatus.Pending || r.Status == PaymentStatus.Success), ct);
            if (eligible && admin != null && replacement != null)
            {
                var next = new VerificationAssignment { Id = Guid.NewGuid(), ProjectVerificationId = v.Id, ProjectVerification = v,
                    AssignedToUserId = replacement.Id, AdminId = admin.Id, Status = VerificationAssignmentStatus.Assigned,
                    AssignedAt = now, AssignmentDueAt = now.AddHours(48), ReportDueAt = now.AddHours(48), CreatedAt = now, UpdatedAt = now };
                db.VerificationAssignments.Add(next);
                a.Status = VerificationAssignmentStatus.Cancelled; a.ReplacedAt = now; a.ReplacedBy = next;
                a.ReplacementReason = "SYSTEM: report missing after deadline and reminder";
                await notifications.AddAsync(v.Id, replacement.Id, $"replacement:{next.Id}",
                    "You have been assigned a replacement verification report, due in 48 hours.", now, ct);
            }
            else await notifications.AdminsAsync(v.Id, $"replacement-unavailable:{a.Id}",
                "Automatic replacement could not find an eligible, available verificator and active admin. Manual assignment is needed.", now, ct);
        }
        a.UpdatedAt = now; v.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
