using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task AdminVerificationRequiresActiveAdminAndValidFilters()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new AdminProjectVerificationService(f.Db);
        await Error("FORBIDDEN", () => service.ListAsync(f.Owner.Id));
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        await Error("INVALID_DATE_RANGE", () => service.ListAsync(f.Other.Id,
            from: DateTimeOffset.UtcNow, to: DateTimeOffset.UtcNow.AddDays(-1)));
        f.Other.Status = AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Error("FORBIDDEN", () => service.GetDetailAsync(Guid.NewGuid(), f.Other.Id));
    }

    [Fact]
    public async Task AdminListsPendingAndOverdueAndReadsEvidence()
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync(true)).Verification.Id;
        var service = new AdminProjectVerificationService(f.Db);
        Assert.Equal(id, Assert.Single((await service.ListAsync(f.Other.Id, pendingAssignment: true)).Verifications).Id);
        Assert.Empty((await service.ListAsync(f.Other.Id, overdue: true)).Verifications);
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.ScheduledEndAt = DateTimeOffset.UtcNow.AddHours(-1);
        await f.Db.SaveChangesAsync();
        Assert.Equal(id, Assert.Single((await service.ListAsync(f.Other.Id, overdue: true)).Verifications).Id);
        Assert.Single((await service.GetDetailAsync(id, f.Other.Id)).Detail.EvidenceSubmissions);
        await Error("REPORT_REQUIRED", () => service.ReviewAsync(id, f.Other.Id, new(VerificationReviewDecision.Uphold, "Reviewed")));
    }

    [Fact]
    public async Task AdminRefundReservesBalanceAndRecordsActorWithoutChangingVerification()
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var payment = await f.Db.Transactions.SingleAsync();
        var service = new AdminProjectVerificationService(f.Db);
        await Error("PAYMENT_NOT_FOUND", () => service.CreateRefundAsync(id, f.Other.Id, new(Guid.NewGuid(), 1, "Authorized")));
        var refund = await service.CreateRefundAsync(id, f.Other.Id, new(payment.Id, payment.Amount, "Authorized service refund"));
        Assert.Equal(PaymentStatus.Pending, refund.Status);
        Assert.Null(refund.CompletedAt);
        await Error("INVALID_REFUND_AMOUNT", () => service.CreateRefundAsync(id, f.Other.Id, new(payment.Id, 1, "Duplicate")));
        var detail = await service.GetDetailAsync(id, f.Other.Id);
        Assert.Equal(f.Other.Profile!.Id, Assert.Single(detail.Actions).ActorUserId);
        Assert.Equal(ProjectVerificationStatus.Pending, detail.Detail.Verification.Status);
        Assert.Single(await service.GetRefundsAsync(id, f.Other.Id));
    }

    [Fact]
    public async Task AdminReviewRecordsDecisionWithoutEditingReportOrRefunding()
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync(true)).Verification.Id;
        var staff = await f.Db.UserProfiles.SingleAsync(p => p.Account.Role == AccountRole.Verificator);
        var now = DateTimeOffset.UtcNow;
        f.Db.VerificationAssignments.Add(new()
        {
            Id = Guid.NewGuid(), ProjectVerificationId = id, AssignedToUserId = staff.Id,
            AdminId = f.Other.Profile!.Id, Status = VerificationAssignmentStatus.Completed,
            AssignedAt = now, CreatedAt = now, UpdatedAt = now,
            Report = new() { Id = Guid.NewGuid(), Summary = "Failed assessment", Outcome = ProjectVerificationStatus.Failed,
                SubmittetAt = now, CreatedAt = now, UpdatedAt = now }
        });
        await f.Db.SaveChangesAsync();
        var service = new AdminProjectVerificationService(f.Db);
        var result = await service.ReviewAsync(id, f.Other.Id, new(VerificationReviewDecision.ReassessmentRequired, "Evidence was overlooked"));
        Assert.Equal("ReassessmentRequired", result.Decision);
        Assert.Equal(f.Other.Profile.Id, result.ActorUserId);
        Assert.Equal("Failed assessment", Assert.Single(await service.GetReportsAsync(id, f.Other.Id)).Summary);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public async Task CreationRecordsPendingWithoutFinalizerOrChangingProjectOrCreatingPayment()
    {
        await using var f = await Fixture.CreateAsync();
        var result = await f.CreateVerificationAsync();
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.ProjectVerifications.SingleAsync();
        Assert.Equal(ProjectVerificationStatus.Pending, result.Verification.Status);
        Assert.Null(stored.FinalizedByUserId);
        Assert.Equal(f.Package.Id, stored.PackageId);
        Assert.Equal(ProjectStatus.Completed, (await f.Db.Projects.SingleAsync()).Status);
        Assert.Empty(await f.Db.Transactions.ToListAsync());
        Assert.Empty(result.EvidenceSubmissions);
        Assert.False(result.AiPreCheck.HasResult);
    }

    [Fact]
    public async Task NonLeaderAndIncompleteProjectCannotCreate()
    {
        await using var f = await Fixture.CreateAsync();
        await Error("FORBIDDEN", () => f.Service.CreateAsync(f.Project.Id, f.Other.Id, new(f.Package.Id)));
        f.Project.Status = ProjectStatus.Active;
        await f.Db.SaveChangesAsync();
        await Error("PROJECT_NOT_COMPLETED", () => f.CreateVerificationAsync());
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
    }

    [Fact]
    public async Task InactiveAccountsCannotAccessService()
    {
        await using var f = await Fixture.CreateAsync();
        f.Owner.Status = AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Error("ACCOUNT_INACTIVE", () => f.CreateVerificationAsync());
    }

    [Theory]
    [InlineData(false, PackageType.Verification, TargetRole.Student, "INVALID_VERIFICATION_PACKAGE")]
    [InlineData(true, PackageType.Advertisement, TargetRole.Student, "INVALID_VERIFICATION_PACKAGE")]
    [InlineData(true, PackageType.Verification, TargetRole.Company, "INVALID_PACKAGE_ROLE")]
    public async Task InvalidPackageCannotBeSelected(bool active, PackageType type, TargetRole role, string code)
    {
        await using var f = await Fixture.CreateAsync();
        f.Package.IsActive = active;
        f.Package.Type = type;
        f.Package.Role = role;
        await f.Db.SaveChangesAsync();
        await Error(code, () => f.CreateVerificationAsync());
    }

    [Fact]
    public async Task DuplicatePendingRequestIsRejectedIncludingDatabaseConstraint()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.CreateVerificationAsync();
        await Error("VERIFICATION_EXISTS", () => f.CreateVerificationAsync());
        f.Db.ProjectVerifications.Add(new()
        {
            Id = Guid.NewGuid(), ProjectId = f.Project.Id, RequestByUserId = f.Owner.Profile!.Id,
            PackageId = f.Package.Id, Status = ProjectVerificationStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
        Assert.NotEqual(Guid.Empty, first.Verification.Id);
    }

    [Fact]
    public async Task ConfiguredInitialEvidenceAndTypesAreRequired()
    {
        await using var f = await Fixture.CreateAsync();
        f.Configuration["ProjectVerification:RequireEvidenceOnCreate"] = "true";
        f.Configuration["ProjectVerification:RequiredEvidenceTypes:0"] = "source";
        await Error("EVIDENCE_REQUIRED", () => f.CreateVerificationAsync());
        await Error("EVIDENCE_REQUIRED", () => f.Service.CreateAsync(f.Project.Id, f.Owner.Id,
            new(f.Package.Id, [new("demo", "Demo", "https://example.com/demo")])));
        var result = await f.CreateVerificationAsync(true);
        Assert.Single(result.EvidenceSubmissions);
    }

    [Fact]
    public async Task EvidenceResubmissionPreservesHistoryAndMarksAiResultStale()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var initial = await f.Service.SubmitEvidenceAsync(id, f.Owner.Id, new(Fixture.Evidence()));
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.AiSummary = "Need an additional demo.";
        verification.AiScore = 55;
        verification.AiReviewedAt = DateTimeOffset.UtcNow;
        verification.AiEvidenceVersion = 1;
        verification.AiAdditionalEvidenceJson = JsonSerializer.Serialize(new[] { "demo" });
        await f.Db.SaveChangesAsync();
        Assert.True((await f.Service.GetAiPreCheckAsync(id, f.Owner.Id)).IsCurrent);
        var revision = await f.Service.ResubmitEvidenceAsync(id, f.Owner.Id,
            new([new("source", "Updated", "https://example.com/new")]));
        f.Db.ChangeTracker.Clear();
        var detail = await f.Service.GetDetailAsync(id, f.Owner.Id);
        Assert.Equal(1, initial.Version);
        Assert.Equal(2, revision.Version);
        Assert.Equal(2, detail.EvidenceSubmissions.Count);
        Assert.Equal("https://example.com/source", detail.EvidenceSubmissions[0].Evidence[0].Url);
        Assert.Equal("https://example.com/new", detail.EvidenceSubmissions[1].Evidence[0].Url);
        Assert.False(detail.AiPreCheck.IsCurrent);
        Assert.Equal("Need an additional demo.", detail.AiPreCheck.Summary);
        Assert.Equal(new[] { "demo" }, detail.AiPreCheck.AdditionalEvidenceRequired);
        Assert.Equal(ProjectVerificationStatus.Pending, detail.Verification.Status);
        await Error("EVIDENCE_ALREADY_SUBMITTED", () => f.Service.SubmitEvidenceAsync(id, f.Owner.Id, new(Fixture.Evidence())));
    }

    [Theory]
    [InlineData("http://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/file")]
    [InlineData("https://user:password@example.com/file")]
    public async Task InvalidEvidenceUrlsAreRejectedWithoutSaving(string url)
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await Error("INVALID_EVIDENCE_URL", () => f.Service.SubmitEvidenceAsync(id, f.Owner.Id,
            new([new("source", "Source", url)])));
        Assert.Empty(await f.Db.ProjectVerificationEvidence.ToListAsync());
    }

    [Fact]
    public async Task EvidenceMethodSequenceAndDuplicateUrlsAreValidated()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await Error("EVIDENCE_NOT_SUBMITTED", () => f.Service.ResubmitEvidenceAsync(id, f.Owner.Id, new(Fixture.Evidence())));
        await Error("DUPLICATE_EVIDENCE", () => f.Service.SubmitEvidenceAsync(id, f.Owner.Id,
            new([Fixture.Evidence()[0], Fixture.Evidence()[0]])));
        await Error("INVALID_EVIDENCE", () => f.Service.SubmitEvidenceAsync(id, f.Owner.Id, new([])));
    }

    [Fact]
    public async Task AllReadsAndMutationsRejectAnotherLeader()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        await Error("FORBIDDEN", () => f.Service.GetDetailAsync(id, f.Other.Id));
        await Error("FORBIDDEN", () => f.Service.GetAiPreCheckAsync(id, f.Other.Id));
        await Error("FORBIDDEN", () => f.Service.GetFinalResultAsync(id, f.Other.Id));
        await Error("FORBIDDEN", () => f.Service.SubmitEvidenceAsync(id, f.Other.Id, new(Fixture.Evidence())));
        await Error("FORBIDDEN", () => f.Service.ResubmitEvidenceAsync(id, f.Other.Id, new(Fixture.Evidence())));
        await Error("FORBIDDEN", () => f.Service.GetAvailableMeetingSlotsAsync(id, f.Other.Id, slot.StartAt, slot.EndAt));
        await Error("FORBIDDEN", () => f.Service.ScheduleMeetingAsync(id, f.Other.Id, slot));
        await Error("FORBIDDEN", () => f.Service.CancelAsync(id, f.Other.Id, new("Cancel")));
        await Error("FORBIDDEN", () => f.Service.GetMyVerificationsAsync(f.Other.Id, f.Project.Id));
    }

    [Fact]
    public async Task ListingFiltersByOwnershipStatusAndPagination()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.CreateVerificationAsync();
        await f.Service.CancelAsync(first.Verification.Id, f.Owner.Id, new("Retry"));
        await f.CreateVerificationAsync();
        await f.CreateOtherVerificationAsync();
        f.Db.ChangeTracker.Clear();
        var page = await f.Service.GetMyVerificationsAsync(f.Owner.Id, page: 1, pageSize: 1);
        Assert.Equal(2, page.TotalItems);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Verifications);
        var pending = await f.Service.GetMyVerificationsAsync(f.Owner.Id, status: ProjectVerificationStatus.Pending);
        Assert.Single(pending.Verifications);
        Assert.Equal(f.Project.Id, pending.Verifications[0].ProjectId);
        var own = await f.Service.GetMyVerificationsAsync(f.Other.Id);
        Assert.Single(own.Verifications);
        await Error("INVALID_VERIFICATION_STATUS", () => f.Service.GetMyVerificationsAsync(f.Owner.Id, status: (ProjectVerificationStatus)99));
    }

    [Fact]
    public async Task SchedulingRequiresEvidenceAndActualSuccessfulPayment()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await Error("EVIDENCE_REQUIRED", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, Fixture.Slot()));
        await f.Service.SubmitEvidenceAsync(id, f.Owner.Id, new(Fixture.Evidence()));
        await Error("PAYMENT_REQUIRED", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, Fixture.Slot()));
        await f.PayAsync(id, f.Owner, PaymentStatus.Pending);
        await Error("PAYMENT_REQUIRED", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, Fixture.Slot()));
        (await f.Db.Transactions.SingleAsync()).Status = PaymentStatus.Success;
        await f.Db.SaveChangesAsync();
        await f.Service.ScheduleMeetingAsync(id, f.Owner.Id, Fixture.Slot());
    }

    [Fact]
    public async Task MissingCalendarPolicyDoesNotInventAvailability()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync(true)).Verification.Id;
        await f.PayAsync(id, f.Owner);
        f.Configuration["ProjectVerification:TimeZoneId"] = null;
        await Error("MEETING_POLICY_NOT_CONFIGURED", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, Fixture.Slot()));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Success)]
    public async Task RefundingOrRefundedChargeCannotAuthorizeBooking(PaymentStatus status)
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync(true)).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var transaction = await f.Db.Transactions.SingleAsync();
        f.Db.RefundTransactions.Add(new()
        {
            Id = Guid.NewGuid(), TransactionId = transaction.Id, Amount = transaction.Amount,
            Status = status, PaymentProvider = "Provider", Reason = "Process failure",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow
        });
        await f.Db.SaveChangesAsync();
        await Error("PAYMENT_REQUIRED", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, Fixture.Slot()));
        var payment = Assert.Single((await f.Service.GetDetailAsync(id, f.Owner.Id)).Payments);
        Assert.Equal(PaymentStatus.Success, payment.Status);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(status, refund.Status);
        Assert.Equal(status == PaymentStatus.Success, refund.CompletedAt.HasValue);
    }

    [Fact]
    public async Task SlotsRespectRealCapacityRecheckBookingAndPermitUpdatingOwnSlot()
    {
        await using var f = await Fixture.CreateAsync();
        var first = (await f.CreateVerificationAsync(true)).Verification.Id;
        var second = (await f.CreateOtherVerificationAsync(true)).Verification.Id;
        await f.PayAsync(first, f.Owner);
        await f.PayAsync(second, f.Other);
        var slot = Fixture.Slot();
        var offered = await f.Service.GetAvailableMeetingSlotsAsync(second, f.Other.Id, slot.StartAt, slot.EndAt);
        Assert.Single(offered.Slots);
        // The second request must recheck even if the frontend obtained a free slot earlier.
        await f.Service.ScheduleMeetingAsync(first, f.Owner.Id, slot);
        await Error("MEETING_SLOT_UNAVAILABLE", () => f.Service.ScheduleMeetingAsync(second, f.Other.Id, slot));
        var updated = await f.Service.ScheduleMeetingAsync(first, f.Owner.Id, slot);
        Assert.Equal(slot.StartAt, updated.Verification.ScheduledStartAt);
        var full = await f.Service.GetAvailableMeetingSlotsAsync(second, f.Other.Id, slot.StartAt, slot.EndAt);
        Assert.Empty(full.Slots);
        await f.Service.CancelAsync(first, f.Owner.Id, new("Cannot attend"));
        await f.Service.ScheduleMeetingAsync(second, f.Other.Id, slot);
    }

    [Fact]
    public async Task CapacityDoesNotExceedActiveVerificatorsAndSameLeaderCannotDoubleBook()
    {
        await using var f = await Fixture.CreateAsync();
        f.Configuration["ProjectVerification:Capacity"] = "5";
        var first = (await f.CreateVerificationAsync(true)).Verification.Id;
        await f.PayAsync(first, f.Owner);
        var staff = await f.Db.Accounts.SingleAsync(a => a.Role == AccountRole.Verificator);
        staff.Status = AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Error("MEETING_SLOT_UNAVAILABLE", () => f.Service.ScheduleMeetingAsync(first, f.Owner.Id, Fixture.Slot()));
        staff.Status = AccountStatus.Active;
        f.Db.Accounts.Add(Fixture.Account("Staff2", AccountRole.Verificator));
        var secondProject = Fixture.ProjectFor(f.Owner);
        f.Db.Projects.Add(secondProject);
        await f.Db.SaveChangesAsync();
        var second = (await f.Service.CreateAsync(secondProject.Id, f.Owner.Id, new(f.Package.Id, Fixture.Evidence()))).Verification.Id;
        await f.PayAsync(second, f.Owner);
        await f.Service.ScheduleMeetingAsync(first, f.Owner.Id, Fixture.Slot());
        await Error("MEETING_SLOT_UNAVAILABLE", () => f.Service.ScheduleMeetingAsync(second, f.Owner.Id, Fixture.Slot()));
    }

    [Fact]
    public async Task SlotsValidateNoticeHoursDurationAndNormalizeUtc()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync(true)).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var slot = Fixture.Slot();
        await Error("INVALID_MEETING_SLOT", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id,
            new(slot.StartAt.AddMinutes(15), slot.EndAt.AddMinutes(15))));
        await Error("INVALID_MEETING_SLOT", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id,
            new(slot.StartAt, slot.EndAt.AddMinutes(1))));
        f.Configuration["ProjectVerification:MinimumNoticeMinutes"] = "10000";
        await Error("INVALID_MEETING_SLOT", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot));
        f.Configuration["ProjectVerification:MinimumNoticeMinutes"] = "60";
        var result = await f.Service.ScheduleMeetingAsync(id, f.Owner.Id,
            new(slot.StartAt.ToOffset(TimeSpan.FromHours(7)), slot.EndAt.ToOffset(TimeSpan.FromHours(7))));
        Assert.Equal(TimeSpan.Zero, result.Verification.ScheduledStartAt!.Value.Offset);
        Assert.Equal(slot.StartAt, result.Verification.ScheduledStartAt);
        f.Db.ChangeTracker.Clear();
        Assert.Equal(TimeSpan.Zero, (await f.Db.ProjectVerifications.SingleAsync()).ScheduledStartAt!.Value.Offset);
        await Error("INVALID_SLOT_RANGE", () => f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.StartAt.AddDays(32)));
    }

    [Theory]
    [InlineData(ProjectVerificationStatus.Verified)]
    [InlineData(ProjectVerificationStatus.Failed)]
    [InlineData(ProjectVerificationStatus.Cancelled)]
    public async Task TerminalRequestsCannotBeChanged(ProjectVerificationStatus status)
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync(true)).Verification.Id;
        (await f.Db.ProjectVerifications.SingleAsync()).Status = status;
        await f.Db.SaveChangesAsync();
        await Error("INVALID_VERIFICATION_STATE", () => f.Service.ResubmitEvidenceAsync(id, f.Owner.Id, new(Fixture.Evidence())));
        await Error("INVALID_VERIFICATION_STATE", () => f.Service.CancelAsync(id, f.Owner.Id, new("Cancel")));
        await Error("INVALID_VERIFICATION_STATE", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, Fixture.Slot()));
    }

    [Fact]
    public async Task AssignedRequestsAndStartedMeetingsCannotBeChanged()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync(true)).Verification.Id;
        var assignment = new VerificationAssignment
        {
            Id = Guid.NewGuid(), ProjectVerificationId = id, AssignedToUserId = f.Other.Profile!.Id,
            AdminId = f.Owner.Profile!.Id, AssignedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        f.Db.VerificationAssignments.Add(assignment);
        await f.Db.SaveChangesAsync();
        await Error("VERIFICATION_LOCKED", () => f.Service.ResubmitEvidenceAsync(id, f.Owner.Id, new(Fixture.Evidence())));
        await Error("VERIFICATION_LOCKED", () => f.Service.CancelAsync(id, f.Owner.Id, new("Cancel")));
        f.Db.VerificationAssignments.Remove(assignment);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        (await f.Db.ProjectVerifications.SingleAsync()).ScheduledStartAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        await Error("VERIFICATION_LOCKED", () => f.Service.CancelAsync(id, f.Owner.Id, new("Cancel")));
    }

    [Fact]
    public async Task CancellationRecordsReasonAndPreservesChargeWithoutRefund()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        await Error("INVALID_CANCELLATION_REASON", () => f.Service.CancelAsync(id, f.Owner.Id, new(" ")));
        var result = await f.Service.CancelAsync(id, f.Owner.Id, new(" Cannot attend "));
        Assert.Equal("Cannot attend", result.CancellationReason);
        Assert.NotNull(result.CancelledAt);
        Assert.Equal(ProjectVerificationStatus.Cancelled, result.Verification.Status);
        Assert.Equal(PaymentStatus.Success, (await f.Db.Transactions.SingleAsync()).Status);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public async Task ReportsAreReadOnlyAndFinalFailureDoesNotRefund()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await Error("VERIFICATION_RESULT_NOT_AVAILABLE", () => f.Service.GetFinalResultAsync(id, f.Owner.Id));
        var assignment = new VerificationAssignment
        {
            Id = Guid.NewGuid(), ProjectVerificationId = id, AssignedToUserId = f.Other.Profile!.Id,
            AdminId = f.Owner.Profile!.Id, Status = VerificationAssignmentStatus.Completed,
            AssignedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        assignment.Report = new VerificationReport
        {
            Id = Guid.NewGuid(), VerificationId = assignment.Id, Summary = "Insufficient result",
            Findings = "Reviewed", SubmittetAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        f.Db.VerificationAssignments.Add(assignment);
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.Status = ProjectVerificationStatus.Failed;
        verification.FinalizedAt = DateTimeOffset.UtcNow;
        verification.FinalizedByUserId = f.Other.Profile.Id;
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var result = await f.Service.GetFinalResultAsync(id, f.Owner.Id);
        Assert.Equal(ProjectVerificationStatus.Failed, result.Status);
        Assert.Equal(assignment.Id, Assert.Single(result.Reports).AssignmentId);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
    }

    [Fact]
    public void MeetingPolicySkipsNonWorkingDaysAndAmbiguousDstTimes()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var policy = new ProjectVerificationPolicy
        {
            TimeZoneId = zone.Id, WorkingDays = [DayOfWeek.Sunday],
            WorkdayStart = new(1, 0), WorkdayEnd = new(3, 0), SlotMinutes = 30,
            Capacity = 1, MinimumNoticeMinutes = 0, MaxAdvanceDays = 365
        };
        var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var slots = policy.GenerateSlots(from, from.AddDays(1), from.AddDays(-1), policy.RequireMeetingPolicy()).ToList();
        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(TimeZoneInfo.ConvertTime(s.Start, zone).Hour >= 2));
        Assert.Empty(policy.GenerateSlots(from.AddDays(1), from.AddDays(2), from, zone));
    }

    private static async Task Error(string code, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<ProjectVerificationException>(action);
        Assert.Equal(code, error.Code);
    }

    private sealed class Fixture(SqliteConnection connection, UniNetDbContext db,
        IConfiguration configuration, Account owner, Account other, Project project, Package package) : IAsyncDisposable
    {
        public UniNetDbContext Db { get; } = db;
        public IConfiguration Configuration { get; } = configuration;
        public Account Owner { get; } = owner;
        public Account Other { get; } = other;
        public Project Project { get; } = project;
        public Package Package { get; } = package;
        public ProjectVerificationService Service { get; } = new(db, configuration);
        public static Account Account(string name, AccountRole role = AccountRole.Student) => new()
        {
            Email = name + "@test.com", Role = role,
            Profile = new() { FullName = name, Nickname = name }
        };
        public static Project ProjectFor(Account account) => new()
        {
            CreatorId = account.Profile!.Id, Title = "Project", Description = "Completed project",
            ProjectField = "IT", MemberTarget = 2, Status = ProjectStatus.Completed
        };
        public static List<VerificationEvidenceRequest> Evidence() => [new("source", "Source", "https://example.com/source")];
        public static ScheduleProjectVerificationRequest Slot()
        {
            var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2).AddHours(10), TimeSpan.Zero);
            return new(start, start.AddHours(1));
        }
        public Task<ProjectVerificationDetailResponse> CreateVerificationAsync(bool evidence = false) =>
            Service.CreateAsync(Project.Id, Owner.Id, new(Package.Id, evidence ? Evidence() : null));
        public async Task<ProjectVerificationDetailResponse> CreateOtherVerificationAsync(bool evidence = false)
        {
            var project = ProjectFor(Other);
            Db.Projects.Add(project);
            await Db.SaveChangesAsync();
            return await Service.CreateAsync(project.Id, Other.Id, new(Package.Id, evidence ? Evidence() : null));
        }
        public async Task PayAsync(Guid id, Account owner, PaymentStatus status = PaymentStatus.Success)
        {
            Db.Transactions.Add(new()
            {
                Id = Guid.NewGuid(), UserId = owner.Profile!.Id, PackageId = Package.Id, Amount = Package.Price,
                Status = status, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                VerificationTransaction = new() { VerificationId = id, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }
            });
            await Db.SaveChangesAsync();
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await db.Database.EnsureCreatedAsync();
            var owner = Account("Owner");
            var other = Account("Other");
            db.Accounts.AddRange(owner, other, Account("Staff", AccountRole.Verificator));
            var project = ProjectFor(owner);
            var package = new Package { Id = Guid.NewGuid(), Code = "VERIFICATION", Name = "Verification",
                Type = PackageType.Verification, Role = TargetRole.Student, IsActive = true, Price = 32000 };
            db.Projects.Add(project);
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            var settings = new Dictionary<string, string?>
            {
                ["ProjectVerification:TimeZoneId"] = "UTC", ["ProjectVerification:WorkdayStart"] = "09:00:00",
                ["ProjectVerification:WorkdayEnd"] = "17:00:00", ["ProjectVerification:SlotMinutes"] = "60",
                ["ProjectVerification:Capacity"] = "1", ["ProjectVerification:MinimumNoticeMinutes"] = "60",
                ["ProjectVerification:MaxAdvanceDays"] = "30"
            };
            foreach (var day in Enum.GetValues<DayOfWeek>()) settings[$"ProjectVerification:WorkingDays:{(int)day}"] = day.ToString();
            return new(connection, db, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), owner, other, project, package);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}

// SQLite has no native DateTimeOffset comparison/order support; exercise the service using UTC ticks.
public sealed class UtcSqliteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        var converter = new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                property.SetValueConverter(converter);
    }
}
