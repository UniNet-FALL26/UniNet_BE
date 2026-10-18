using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Application.Interfaces;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class AssignmentService(UniNetDbContext db, Microsoft.Extensions.Configuration.IConfiguration configuration) : IAssignmentService
{
    public async Task<AssignmentDetailResponse> GetDetailAsync(Guid assignmentId, Guid accountId,
        CancellationToken ct = default)
    {
        var user = await GetActorAsync(accountId, null, ct);
        var assignment = await AssignmentQuery().AsNoTracking()
            .Include(a => a.ProjectVerification).ThenInclude(v => v.Project)
            .Include(a => a.ProjectVerification).ThenInclude(v => v.EvidenceModeration).ThenInclude(m => m!.Attempts)
            .Include(a => a.Report).ThenInclude(r => r!.Versions).AsSplitQuery()
            .FirstOrDefaultAsync(a => a.Id == assignmentId &&
                (user.Account.Role == AccountRole.Admin || a.AssignedToUserId == user.Id), ct)
            ?? throw NotFound();
        return AssignmentMapping.ToDetailResponse(assignment);
    }

    public async Task<AssignmentsResponse> GetMyAssignmentsAsync(Guid accountId,
        VerificationAssignmentStatus? status = null, int page = 1, int pageSize = 10,
        CancellationToken ct = default)
    {
        var user = await GetActorAsync(accountId, AccountRole.Verificator, ct);
        return await ListAsync(AssignmentQuery().AsNoTracking().Where(a => a.AssignedToUserId == user.Id),
            status, page, pageSize, ct);
    }

    public async Task<AssignmentsResponse> GetAllAssignmentsAsync(Guid accountId,
        VerificationAssignmentStatus? status = null, int page = 1, int pageSize = 10,
        CancellationToken ct = default)
    {
        await GetActorAsync(accountId, AccountRole.Admin, ct);
        return await ListAsync(AssignmentQuery().AsNoTracking(), status, page, pageSize, ct);
    }

    private static async Task<AssignmentsResponse> ListAsync(IQueryable<VerificationAssignment> query,
        VerificationAssignmentStatus? status, int page, int pageSize, CancellationToken ct)
    {
        Require(!status.HasValue || Enum.IsDefined(status.Value), "INVALID_ASSIGNMENT_STATUS", "Invalid assignment status.");
        if (status.HasValue) query = query.Where(a => a.Status == status.Value);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.AssignedAt).ThenBy(a => a.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        return new(page, pageSize, total, (int)(((long)total + pageSize - 1) / pageSize),
            items.Select(AssignmentMapping.ToResponse).ToList());
    }

    public Task<AssignmentResponse> CreateAsync(Guid accountId, CreateAssignmentRequest request,
        CancellationToken ct = default) => MutateAsync(async () =>
    {
        var admin = await GetActorAsync(accountId, AccountRole.Admin, ct);
        var verification = await GetSchedulableVerificationAsync(request.ProjectVerificationId, ct);
        Require(!await db.VerificationAssignments.AnyAsync(a => a.ProjectVerificationId == verification.Id &&
            (a.Status == VerificationAssignmentStatus.Assigned || a.Status == VerificationAssignmentStatus.Overdue ||
             a.Status == VerificationAssignmentStatus.Completed), ct),
            "ASSIGNMENT_EXISTS", "This verification already has an active or completed assignment.", 409);
        var user = await GetAvailableVerificatorAsync(request.AssignedToUserId, verification, null, ct);
        return AssignmentMapping.ToResponse(AddAssignment(verification, user, admin, DateTimeOffset.UtcNow));
    }, ct);

    public Task<AssignmentResponse> ReplaceVerificatorAsync(Guid assignmentId, Guid accountId,
        ReplaceVerificatorRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        var admin = await GetActorAsync(accountId, AccountRole.Admin, ct);
        var old = await AssignmentQuery().Include(a => a.Report).FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw NotFound();
        RequireActive(old);
        Require(old.Report == null && old.ReplacedAt == null && old.ReplacedBy == null,
            "ASSIGNMENT_LOCKED", "An assignment with a report or replacement cannot be replaced.", 409);
        Require(!string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Trim().Length <= 2000,
            "INVALID_REPLACEMENT_REASON", "Replacement reason must contain 1 to 2000 characters.");
        Require(request.AssignedToUserId != old.AssignedToUserId, "SAME_VERIFICATOR", "Select a different verificator.");
        var verification = await GetSchedulableVerificationAsync(old.ProjectVerificationId, ct);
        var user = await GetAvailableVerificatorAsync(request.AssignedToUserId, verification, old.Id, ct);
        var now = DateTimeOffset.UtcNow;
        var replacement = AddAssignment(verification, user, admin, now);
        old.Status = VerificationAssignmentStatus.Cancelled;
        old.ReplacedAt = now;
        old.ReplacementReason = request.Reason.Trim();
        old.ReplacedBy = replacement;
        old.UpdatedAt = now;
        return AssignmentMapping.ToResponse(replacement);
    }, ct);

    public Task<AssignmentResponse> UpdateAsync(Guid assignmentId, Guid accountId,
        UpdateAssignmentRequest request, CancellationToken ct = default) => MutateAsync(async () =>
    {
        await GetActorAsync(accountId, AccountRole.Admin, ct);
        var assignment = await AssignmentQuery().Include(a => a.Report)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, ct)
            ?? throw NotFound();
        RequireActive(assignment);
        var verification = assignment.ProjectVerification;
        Require(verification.Status == ProjectVerificationStatus.Pending && verification.FinalizedAt == null,
            "VERIFICATION_LOCKED", "Only pending verifications can be updated.", 409);
        var now = DateTimeOffset.UtcNow;
        switch (request.Action)
        {
            case AssignmentAction.StartMeeting:
                Require(verification.MeetingStartAt == null && verification.MeetingEndAt == null &&
                    verification.ScheduledStartAt.HasValue && verification.ScheduledStartAt <= now,
                    "INVALID_MEETING_STATE", "The meeting must be scheduled and not already started.", 409);
                verification.MeetingStartAt = now;
                break;
            case AssignmentAction.EndMeeting:
                Require(verification.MeetingStartAt.HasValue && verification.MeetingEndAt == null,
                    "INVALID_MEETING_STATE", "Start the meeting before ending it.", 409);
                Require(now >= verification.MeetingStartAt.Value, "CLOCK_CONFLICT", "Server time precedes meeting start.", 409);
                verification.MeetingEndAt = now;
                assignment.ReportDueAt = now.AddHours(48);
                break;
            case AssignmentAction.Complete:
                Require(verification.MeetingEndAt.HasValue && assignment.Report != null,
                    "ASSIGNMENT_INCOMPLETE", "End the meeting and submit a report before completing the assignment.", 409);
                assignment.Status = VerificationAssignmentStatus.Completed;
                assignment.CompletedAt = now;
                break;
            default:
                throw new AssignmentException("INVALID_ASSIGNMENT_ACTION", "Invalid assignment action.");
        }
        assignment.UpdatedAt = now;
        verification.UpdatedAt = now;
        return AssignmentMapping.ToResponse(assignment);
    }, ct);

    public async Task<List<SuitableVerificatorResponse>> GetSuitableVerificatorsAsync(Guid projectVerificationId,
        Guid accountId, CancellationToken ct = default)
    {
        await GetActorAsync(accountId, AccountRole.Admin, ct);
        var verification = await GetSchedulableVerificationAsync(projectVerificationId, ct);
        return await AvailableVerificators(verification, null).OrderBy(p => p.FullName).ThenBy(p => p.Id)
            .Select(p => new SuitableVerificatorResponse(p.Id, p.FullName)).ToListAsync(ct);
    }

    private IQueryable<VerificationAssignment> AssignmentQuery() => db.VerificationAssignments
        .Include(a => a.ProjectVerification).Include(a => a.ReplacedBy);

    private async Task<UserProfile> GetActorAsync(Guid accountId, AccountRole? role, CancellationToken ct)
    {
        var user = await db.UserProfiles.Include(p => p.Account).FirstOrDefaultAsync(p => p.AccountId == accountId, ct)
            ?? throw new AssignmentException("PROFILE_NOT_FOUND", "User profile not found.", 404);
        Require(user.Account.Status == AccountStatus.Active &&
            (role.HasValue ? user.Account.Role == role.Value :
                user.Account.Role is AccountRole.Admin or AccountRole.Verificator),
            "FORBIDDEN", "An active account with the required assignment role is required.", 403);
        return user;
    }

    private async Task<ProjectVerification> GetSchedulableVerificationAsync(Guid id, CancellationToken ct)
    {
        var verification = await db.ProjectVerifications.FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new AssignmentException("VERIFICATION_NOT_FOUND", "Verification not found.", 404);
        Require(verification.Status == ProjectVerificationStatus.Pending && verification.FinalizedAt == null &&
            verification.MeetingStartAt == null && verification.MeetingEndAt == null,
            "VERIFICATION_LOCKED", "Only pending verifications before the meeting starts can be assigned.", 409);
        Require(verification.ScheduledStartAt.HasValue && verification.ScheduledEndAt.HasValue &&
            verification.ScheduledStartAt < verification.ScheduledEndAt && verification.ScheduledStartAt > DateTimeOffset.UtcNow,
            "INVALID_VERIFICATION_SCHEDULE", "A valid future meeting schedule is required.", 409);
        await new VerificationEligibility(db, configuration).RequireCaseAsync(verification, ct);
        Require(await db.VerificationTransactions.AnyAsync(v => v.VerificationId == id &&
            v.Transaction.Status == PaymentStatus.Success && v.Transaction.PackageId == verification.PackageId &&
            v.Transaction.UserId == verification.RequestByUserId &&
            v.Transaction.FailureReason != TransactionMapping.DuplicatePaymentReview &&
            !v.Transaction.RefundTransactions.Any(r => r.Status == PaymentStatus.Pending || r.Status == PaymentStatus.Success), ct),
            "PAYMENT_REQUIRED", "A valid payment is required before assignment.", 409);
        return verification;
    }

    private IQueryable<UserProfile> AvailableVerificators(ProjectVerification v, Guid? excludeAssignmentId) =>
        db.UserProfiles.Where(p => p.Account.Role == AccountRole.Verificator && p.Account.Status == AccountStatus.Active &&
            p.Id != v.RequestByUserId && !db.ProjectMembers.Any(m => m.ProjectId == v.ProjectId && m.UserId == p.Id) &&
            !db.VerificationAssignments.Any(a => a.AssignedToUserId == p.Id && a.Id != excludeAssignmentId &&
                (a.Status == VerificationAssignmentStatus.Assigned || a.Status == VerificationAssignmentStatus.Overdue) &&
                a.ProjectVerification.Status == ProjectVerificationStatus.Pending &&
                (!a.ProjectVerification.ScheduledStartAt.HasValue || !a.ProjectVerification.ScheduledEndAt.HasValue ||
                 (a.ProjectVerification.ScheduledStartAt < v.ScheduledEndAt &&
                  a.ProjectVerification.ScheduledEndAt > v.ScheduledStartAt) ||
                 (a.ProjectVerification.MeetingStartAt.HasValue && !a.ProjectVerification.MeetingEndAt.HasValue))));

    private async Task<UserProfile> GetAvailableVerificatorAsync(Guid id, ProjectVerification v,
        Guid? excludeAssignmentId, CancellationToken ct) =>
        await AvailableVerificators(v, excludeAssignmentId).FirstOrDefaultAsync(p => p.Id == id, ct)
        ?? throw new AssignmentException("VERIFICATOR_UNAVAILABLE", "Verificator is inactive, ineligible or has a conflicting schedule.", 409);

    private VerificationAssignment AddAssignment(ProjectVerification v, UserProfile user, UserProfile admin, DateTimeOffset now)
    {
        var assignment = new VerificationAssignment
        {
            Id = Guid.NewGuid(), ProjectVerificationId = v.Id, ProjectVerification = v,
            AssignedToUserId = user.Id, AssignedToUser = user, AdminId = admin.Id, AssignedByUser = admin,
            Status = VerificationAssignmentStatus.Assigned, AssignedAt = now,
            AssignmentDueAt = v.ScheduledEndAt, CreatedAt = now, UpdatedAt = now
        };
        db.VerificationAssignments.Add(assignment);
        v.UpdatedAt = now;
        return assignment;
    }

    private static void RequireActive(VerificationAssignment assignment) => Require(
        assignment.Status is VerificationAssignmentStatus.Assigned or VerificationAssignmentStatus.Overdue,
        "ASSIGNMENT_LOCKED", "Only active assignments can be changed.", 409);

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
            ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected } })
        {
            throw new AssignmentException("ASSIGNMENT_CONFLICT", "Assignment data changed concurrently. Reload and try again.", 409);
        }
    }

    private static AssignmentException NotFound() => new("ASSIGNMENT_NOT_FOUND", "Assignment not found.", 404);

    private static void Require([DoesNotReturnIf(false)] bool condition, string code, string message, int status = 400)
    {
        if (!condition) throw new AssignmentException(code, message, status);
    }
}
