using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    private sealed class ModerationProbe : IAIModerationService
    {
        public int Calls;
        public Task<AiModerationResult> AnalyzeProjectAsync(ProjectModerationInput input, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new AiModerationResult("safe", "safe", false, false, false, 0.95m, "Safe", [])); }
    }

    [Theory]
    [InlineData("TRANSIENT_ERROR", 0, 1)]
    [InlineData("CONFIG_ERROR", 0, 0)]
    [InlineData("TRANSIENT_ERROR", 5, 0)]
    public async Task ModerationBackgroundRetryOnlyRetriesTemporaryErrorsWithinLimit(string flag, int retries, int expectedCalls)
    {
        await using var f = await Fixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        f.Project.Status = ProjectStatus.Pending;
        f.Configuration["ProjectModeration:AutoApproveConfidence"] = "0.9";
        f.Db.ProjectModerations.Add(new() { Id = Guid.NewGuid(), ProjectId = f.Project.Id, Status = ProjectModerationStatus.Error,
            ContentHash = "old", AttemptNumber = 1, RetryCount = retries, UpdatedAt = now.AddMinutes(-11),
            ResultJson = "{\"flags\":[\"" + flag + "\"]}" });
        await f.Db.SaveChangesAsync();
        var probe = new ModerationProbe();
        var service = new ProjectModerationService(probe, f.Db, f.Configuration);
        await service.RetryTransientErrorsAsync(now, default);
        await service.RetryTransientErrorsAsync(now, default);
        Assert.Equal(expectedCalls, probe.Calls);
        Assert.Equal(1 + expectedCalls, await f.Db.ProjectModerations.CountAsync());
    }

    [Fact]
    public async Task ProjectStatusOnlyCreatorCanCompleteActiveProject()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new ProjectService(f.Db, null!, f.Configuration);
        f.Project.Status = ProjectStatus.Active;
        var joinedAt = DateTimeOffset.UtcNow.AddDays(-2);
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = f.Project.Id,
            UserId = f.Other.Profile!.Id,
            Role = "Developer",
            Status = ProjectMemberStatus.Left,
            JoinedAt = joinedAt,
            LeftAt = joinedAt.AddDays(1),
            CreatedAt = joinedAt,
            UpdatedAt = joinedAt.AddDays(1)
        });
        var kickedAccount = new Account
        {
            Email = "kicked@example.com",
            Role = AccountRole.Student,
            Status = AccountStatus.Active,
            Profile = new UserProfile
            {
                FullName = "Kicked Member",
                Nickname = "kicked",
                IsVerified = true,
                CreatedAt = joinedAt,
                UpdatedAt = joinedAt
            }
        };
        kickedAccount.Profile.AccountId = kickedAccount.Id;
        f.Db.Accounts.Add(kickedAccount);
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = f.Project.Id,
            UserId = kickedAccount.Profile.Id,
            Role = "Designer",
            Status = ProjectMemberStatus.Kicked,
            JoinedAt = joinedAt,
            LeftAt = joinedAt.AddDays(1),
            CreatedAt = joinedAt,
            UpdatedAt = joinedAt.AddDays(1)
        });
        f.Db.ProjectMembers.Add(new ProjectMember
        {
            Id = Guid.NewGuid(),
            ProjectId = f.Project.Id,
            UserId = f.Project.CreatorId,
            Role = "Creator",
            Status = ProjectMemberStatus.Active,
            JoinedAt = joinedAt,
            CreatedAt = joinedAt,
            UpdatedAt = joinedAt
        });
        await f.Db.SaveChangesAsync();
        var forbidden = await Assert.ThrowsAsync<ProjectException>(() => service.UpdateStatusAsync(f.Other.Id, new(f.Project.Id, ProjectStatus.Completed), default));
        Assert.Equal(403, forbidden.Status);
        foreach (var target in new[] { ProjectStatus.Active, ProjectStatus.Pending, ProjectStatus.Rejected, (ProjectStatus)100 })
            await Assert.ThrowsAsync<ProjectException>(() => service.UpdateStatusAsync(f.Owner.Id, new(f.Project.Id, target), default));
        var result = await service.UpdateStatusAsync(f.Owner.Id, new(f.Project.Id, ProjectStatus.Completed), default);
        Assert.Equal(ProjectStatus.Completed, result.Status);
        Assert.NotNull(result.CompletedAt);
        await Assert.ThrowsAsync<ProjectException>(() => service.UpdateStatusAsync(f.Owner.Id, new(f.Project.Id, ProjectStatus.Completed), default));
        f.Db.ChangeTracker.Clear();
        var completedProject = await f.Db.Projects.SingleAsync();
        Assert.Equal(ProjectStatus.Completed, completedProject.Status);
        Assert.Equal(result.CompletedAt, completedProject.CompletedAt);
        var cycle = await f.Db.ContributionCycles.Include(c => c.Participants).SingleAsync();
        Assert.Equal(result.CompletedAt, cycle.OpenedAt);
        Assert.Equal(f.Owner.Profile!.Id, cycle.OpenedById);
        Assert.Equal(2, cycle.Participants.Count);
        Assert.Single(cycle.Participants, p => p.IsLeader && p.UserId == f.Project.CreatorId);
        Assert.Single(cycle.Participants, p => p.UserId == f.Other.Profile!.Id && p.LeftAt != null);
        Assert.DoesNotContain(cycle.Participants, p => p.UserId == kickedAccount.Profile.Id);
    }

    [Fact]
    public async Task AssignmentTimeoutRefundsActualPaidAmountOnceAndPreservesSuccessfulCharge()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var now = DateTimeOffset.UtcNow;
        var payment = await f.Db.Transactions.SingleAsync();
        payment.Amount = 51000; payment.PaidAt = now.AddHours(-24);
        (await f.Db.ProjectVerifications.SingleAsync()).AssignmentEligibleAt = now.AddHours(-24);
        await f.Db.SaveChangesAsync();
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.RefundTimeoutsAsync(now, default);
        await tasks.RefundTimeoutsAsync(now.AddMinutes(1), default);
        var refund = await f.Db.RefundTransactions.SingleAsync();
        Assert.Equal(51000, refund.Amount);
        Assert.Equal(PaymentStatus.Pending, refund.Status);
        Assert.Equal(PaymentStatus.Success, (await f.Db.Transactions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(ProjectVerificationStatus.Cancelled, (await f.Db.ProjectVerifications.AsNoTracking().SingleAsync()).Status);
        // A missed webhook can reveal another real charge after the timeout. It must also be refunded once.
        await f.PayAsync(id, f.Owner);
        await tasks.RefundTimeoutsAsync(now.AddMinutes(2), default);
        Assert.Equal(2, await f.Db.RefundTransactions.CountAsync());
    }

    [Fact]
    public async Task TimeoutDoesNotRefundUnpaidOrRecentlyPaidVerification()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.RefundTimeoutAsync(id, DateTimeOffset.UtcNow, default);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
        await f.PayAsync(id, f.Owner);
        await tasks.RefundTimeoutAsync(id, DateTimeOffset.UtcNow, default);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public async Task TimeoutReservesOnlyUnrefundedBalance()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var payment = await f.Db.Transactions.SingleAsync();
        payment.PaidAt = DateTimeOffset.UtcNow.AddDays(-2);
        (await f.Db.ProjectVerifications.SingleAsync()).AssignmentEligibleAt = payment.PaidAt;
        f.Db.RefundTransactions.Add(new() { Id = Guid.NewGuid(), TransactionId = payment.Id, Amount = 10000,
            PaymentProvider = "Manual", Status = PaymentStatus.Success, Reason = "Previous partial refund" });
        await f.Db.SaveChangesAsync();
        await new VerificationDeadlineTasks(f.Db, f.Configuration).RefundTimeoutAsync(id, DateTimeOffset.UtcNow, default);
        Assert.Equal(payment.Amount, (await f.Db.RefundTransactions.ToListAsync()).Sum(r => r.Amount));
    }

    private static async Task<VerificationAssignment> PrepareOverdueAsync(Fixture f, Guid id, DateTimeOffset meetingEnd)
    {
        f.Other.Role = AccountRole.Admin;
        var staff = await f.Db.UserProfiles.SingleAsync(p => p.Account.Role == AccountRole.Verificator);
        var v = await f.Db.ProjectVerifications.SingleAsync(v => v.Id == id);
        v.MeetingStartAt = meetingEnd.AddHours(-1); v.MeetingEndAt = meetingEnd;
        var a = new VerificationAssignment { Id = Guid.NewGuid(), ProjectVerificationId = id, AssignedToUserId = staff.Id,
            AdminId = f.Other.Profile!.Id, Status = VerificationAssignmentStatus.Assigned, AssignedAt = meetingEnd.AddHours(-2) };
        f.Db.VerificationAssignments.Add(a);
        await f.Db.SaveChangesAsync();
        return a;
    }

    [Fact]
    public async Task ReportOverdueEscalationReplacementAndRefundFollowTheirOwnDeadlines()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var now = DateTimeOffset.UtcNow;
        var original = await PrepareOverdueAsync(f, id, now.AddHours(-48));
        f.Db.Accounts.Add(Fixture.Account("Replacement", AccountRole.Verificator));
        await f.Db.SaveChangesAsync();
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.MarkOverdueAndEscalateAsync(now.AddTicks(-1), default);
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        await tasks.MarkOverdueAndEscalateAsync(now, default);
        await tasks.MarkOverdueAndEscalateAsync(now, default);
        Assert.Equal(VerificationAssignmentStatus.Overdue, (await f.Db.VerificationAssignments.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        await tasks.MarkOverdueAndEscalateAsync(now.AddHours(24), default);
        await tasks.MarkOverdueAndEscalateAsync(now.AddHours(24), default);
        var assignments = await f.Db.VerificationAssignments.AsNoTracking().ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Equal(VerificationAssignmentStatus.Cancelled, assignments.Single(a => a.Id == original.Id).Status);
        await tasks.RefundTimeoutsAsync(now.AddHours(71), default);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
        await tasks.RefundTimeoutsAsync(now.AddHours(72), default);
        Assert.Single(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public async Task MissingReplacementPersistsEscalationWithoutNotificationsOrIneligibleAssignment()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var now = DateTimeOffset.UtcNow;
        var a = await PrepareOverdueAsync(f, id, now.AddHours(-48));
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.MarkOverdueOrReplaceAsync(a.Id, now, default);
        await tasks.MarkOverdueOrReplaceAsync(a.Id, now.AddHours(24), default);
        await tasks.MarkOverdueOrReplaceAsync(a.Id, now.AddHours(25), default);
        Assert.Single(await f.Db.VerificationAssignments.ToListAsync());
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.NotNull((await f.Db.VerificationAssignments.SingleAsync()).EscalatedAt);
    }

    [Fact]
    public async Task ReportSubmissionSuppressesDeadlineAndTimeoutActions()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var now = DateTimeOffset.UtcNow;
        var a = await PrepareOverdueAsync(f, id, now.AddDays(-5));
        f.Db.VerificationReports.Add(new() { Id = Guid.NewGuid(), AssignmentId = a.Id, ProjectVerificationId = id, Summary = "Completed report" });
        await f.Db.SaveChangesAsync();
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.MarkOverdueAndEscalateAsync(now, default);
        await tasks.RefundTimeoutsAsync(now, default);
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    private sealed class RefundProbe : IRefundProvider
    {
        public int CreateCalls, LookupCalls;
        public string? RequestKey;
        public bool Enabled = true;
        public bool Supports(string provider) => Enabled;
        public Task<ProviderRefundResult> CreateAsync(ProviderRefundRequest request, CancellationToken ct)
        { CreateCalls++; RequestKey = request.RequestKey; throw new OperationCanceledException(); }
        public Task<ProviderRefundResult> LookupAsync(ProviderRefundRequest request, CancellationToken ct)
        { LookupCalls++; Assert.Equal(RequestKey, request.RequestKey); return Task.FromResult(new ProviderRefundResult(request.RequestKey, request.Amount, PaymentStatus.Success, "refund-confirmed")); }
    }

    [Fact]
    public async Task RefundTimeoutIsReconciledBySameKeyWithoutSendingAnotherRefund()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var now = DateTimeOffset.UtcNow;
        (await f.Db.Transactions.SingleAsync()).PaidAt = now.AddDays(-2);
        (await f.Db.ProjectVerifications.SingleAsync()).AssignmentEligibleAt = now.AddDays(-2);
        await f.Db.SaveChangesAsync();
        await new VerificationDeadlineTasks(f.Db, f.Configuration).RefundTimeoutAsync(id, now, default);
        var probe = new RefundProbe();
        var tasks = new VerificationRefundTasks(f.Db, probe);
        await tasks.RunAsync(now, default);
        Assert.Equal(PaymentStatus.Pending, (await f.Db.RefundTransactions.AsNoTracking().SingleAsync()).Status);
        await tasks.RunAsync(now.AddMinutes(10), default);
        await tasks.RunAsync(now.AddMinutes(20), default);
        Assert.Equal(1, probe.CreateCalls); Assert.Equal(1, probe.LookupCalls);
        Assert.Equal(PaymentStatus.Success, (await f.Db.RefundTransactions.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(PaymentStatus.Success, (await f.Db.Transactions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task UnsupportedRefundProviderLeavesMoneyReservedWithoutNotifications()
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var now = DateTimeOffset.UtcNow;
        (await f.Db.Transactions.SingleAsync()).PaidAt = now.AddDays(-2);
        (await f.Db.ProjectVerifications.SingleAsync()).AssignmentEligibleAt = now.AddDays(-2);
        await f.Db.SaveChangesAsync();
        await new VerificationDeadlineTasks(f.Db, f.Configuration).RefundTimeoutAsync(id, now, default);
        var tasks = new VerificationRefundTasks(f.Db, new ManualRefundProvider());
        await tasks.RunAsync(now, default); await tasks.RunAsync(now.AddHours(2), default);
        Assert.Equal(PaymentStatus.Pending, (await f.Db.RefundTransactions.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await f.Db.Set<VerificationNotification>().ToListAsync());
        Assert.Equal("MANUAL_REFUND_REQUIRED", (await f.Db.RefundTransactions.SingleAsync()).FailureReason);
    }

}
