using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Enums;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Theory]
    [InlineData(EvidenceModerationStatus.Pending)]
    [InlineData(EvidenceModerationStatus.Processing)]
    [InlineData(EvidenceModerationStatus.Rejected)]
    [InlineData(EvidenceModerationStatus.NeedsEvidence)]
    [InlineData(EvidenceModerationStatus.Error)]
    public async Task Level2CannotBypassNonPassedLevel1(EvidenceModerationStatus status)
    {
        await using var f = await Fixture.CreateAsync();
        (await f.Db.ProjectEvidenceModerations.SingleAsync()).Status = status;
        await f.Db.SaveChangesAsync();
        await Error("LEVEL1_REQUIRED", () => f.CreateVerificationAsync());
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
    }

    [Fact]
    public async Task MissingOrSupersededLevel1CannotAuthorizeCase()
    {
        await using var f = await Fixture.CreateAsync();
        var review = await f.Db.ProjectEvidenceModerations.SingleAsync();
        review.SupersededAt = DateTimeOffset.UtcNow;
        await f.Db.SaveChangesAsync();
        await Error("LEVEL1_REQUIRED", () => f.CreateVerificationAsync());
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleSourceOrPolicyBlocksEveryTransitionAndPreservesCase(bool policyChanged)
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var slot = Fixture.Slot();
        await f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot);
        await f.PayAsync(id, f.Owner);
        var pin = (await f.Db.ProjectVerifications.SingleAsync()).EvidenceModerationId;
        if (policyChanged) f.Configuration["EvidenceModeration:PolicyVersion"] = "changed";
        else
        {
            f.Db.ProjectTasks.Add(new() { ProjectId = f.Project.Id, CreatorId = f.Owner.Profile!.Id, Title = "New task", Description = "Changed source",
                Status = ProjectTaskStatus.Cancelled });
            await f.Db.SaveChangesAsync();
        }
        await Error("LEVEL1_STALE", () => f.Service.GetAvailableMeetingSlotsAsync(id, f.Owner.Id, slot.StartAt, slot.EndAt));
        await Error("LEVEL1_STALE", () => f.Service.ScheduleMeetingAsync(id, f.Owner.Id, slot));
        var checkout = new TransactionService(f.Db, new NoPayments(), f.Configuration, NullLogger<TransactionService>.Instance);
        await Error("LEVEL1_STALE", () => checkout.CreateAsync(id, f.Owner.Id));
        var staff = await f.Db.UserProfiles.SingleAsync(p => p.Account.Role == AccountRole.Verificator);
        await Error("LEVEL1_STALE", () => new AssignmentService(f.Db, f.Configuration).CreateAsync(f.Other.Id, new(id, staff.Id)));
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.ProjectVerifications.SingleAsync();
        Assert.Equal(pin, stored.EvidenceModerationId);
        Assert.Equal(slot.StartAt, stored.ScheduledStartAt);
        Assert.Equal(PaymentStatus.Success, (await f.Db.Transactions.SingleAsync()).Status);
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
        Assert.False((await new EvidenceModerationService(f.Db, f.Configuration).GetAsync(f.Project.Id, f.Owner.Id)).IsCurrent);
    }

    [Fact]
    public async Task NewPassedSnapshotNeverSilentlyRebindsExistingCase()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var pin = (await f.Db.ProjectVerifications.SingleAsync()).EvidenceModerationId;
        f.Configuration["EvidenceModeration:PolicyVersion"] = "v2";
        var service = new EvidenceModerationService(f.Db, f.Configuration);
        var next = await service.RetryAsync(f.Project.Id, f.Owner.Id);
        Assert.Equal(2, next.Version);
        Assert.Equal(EvidenceModerationStatus.Pending, next.Status);
        var latest = await f.Db.ProjectEvidenceModerations.SingleAsync(m => m.Id == next.Id);
        latest.Status = EvidenceModerationStatus.Passed;
        await f.Db.SaveChangesAsync();
        await Error("LEVEL1_STALE", () => new VerificationEligibility(f.Db, f.Configuration)
            .RequireCaseAsync(f.Db.ProjectVerifications.Single(), default));
        var detail = await f.Service.GetDetailAsync(id, f.Owner.Id);
        Assert.Equal(pin, detail.EvidenceModeration!.Id);
        Assert.False(detail.EvidenceModeration.IsCurrent);
        Assert.Equal(2, await f.Db.ProjectEvidenceModerations.CountAsync());
    }

    [Fact]
    public async Task Level1ReadAndRetryEnforceOwnershipAndDoNotResetBudget()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new EvidenceModerationService(f.Db, f.Configuration);
        await Error("FORBIDDEN", () => service.GetAsync(f.Project.Id, f.Other.Id));
        await Error("FORBIDDEN", () => service.RetryAsync(f.Project.Id, f.Other.Id));
        await Error("LEVEL1_UNCHANGED", () => service.RetryAsync(f.Project.Id, f.Owner.Id));
        var review = await f.Db.ProjectEvidenceModerations.SingleAsync();
        review.Status = EvidenceModerationStatus.Error;
        review.AttemptCount = 5;
        await f.Db.SaveChangesAsync();
        await Error("LEVEL1_RETRY_EXHAUSTED", () => service.RetryAsync(f.Project.Id, f.Owner.Id));
        review.AttemptCount = 2;
        review.NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(10);
        await f.Db.SaveChangesAsync();
        var retryAt = review.NextRetryAt;
        await service.RetryAsync(f.Project.Id, f.Owner.Id);
        await service.RetryAsync(f.Project.Id, f.Owner.Id);
        Assert.Equal(2, review.AttemptCount);
        Assert.Equal(retryAt, review.NextRetryAt);
        Assert.Contains("file contents are not read", (await service.GetAsync(f.Project.Id, f.Owner.Id)).ScopeLimitation);
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
    }

    [Fact]
    public async Task LegacyCaseIsReadableButCannotCheckoutWithoutExplicitLink()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.EvidenceModeration = null;
        verification.EvidenceModerationId = null;
        await f.Db.SaveChangesAsync();
        Assert.Null((await f.Service.GetDetailAsync(id, f.Owner.Id)).EvidenceModeration);
        await Error("LEVEL1_LINK_REQUIRED", () => new TransactionService(f.Db, new NoPayments(), f.Configuration,
            NullLogger<TransactionService>.Instance).CreateAsync(id, f.Owner.Id));
    }

    [Fact]
    public async Task AdminAndAssignedVerificatorReadPinnedSnapshotAndAssignmentRequiresPayment()
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        var verification = await f.Db.ProjectVerifications.SingleAsync();
        verification.ScheduledStartAt = Fixture.Slot().StartAt;
        verification.ScheduledEndAt = Fixture.Slot().EndAt;
        await f.Db.SaveChangesAsync();
        var staff = await f.Db.UserProfiles.Include(p => p.Account).SingleAsync(p => p.Account.Role == AccountRole.Verificator);
        var assignments = new AssignmentService(f.Db, f.Configuration);
        var error = await Assert.ThrowsAsync<AssignmentException>(() => assignments.CreateAsync(f.Other.Id, new(id, staff.Id)));
        Assert.Equal("BOOKING_NOT_CONFIRMED", error.Code);
        await f.PayAsync(id, f.Owner);
        var assignment = await assignments.CreateAsync(f.Other.Id, new(id, staff.Id));
        var detail = await assignments.GetDetailAsync(assignment.Id, staff.AccountId);
        Assert.Equal(verification.EvidenceModerationId, detail.EvidenceModeration!.Id);
        Assert.Equal(verification.EvidenceModerationId,
            (await new AdminProjectVerificationService(f.Db).GetDetailAsync(id, f.Other.Id)).Detail.EvidenceModeration!.Id);
        await Error("FORBIDDEN", () => new EvidenceModerationService(f.Db, f.Configuration).GetAsync(f.Project.Id, staff.AccountId));
    }

    [Fact]
    public async Task ExplicitInitialLevel1RequestQueuesOnceWithoutPurchasingLevel2()
    {
        await using var f = await Fixture.CreateAsync(passed: false);
        var service = new EvidenceModerationService(f.Db, f.Configuration);
        await Error("LEVEL1_NOT_FOUND", () => service.GetAsync(f.Project.Id, f.Owner.Id));
        var first = await service.RetryAsync(f.Project.Id, f.Owner.Id);
        var duplicate = await service.RetryAsync(f.Project.Id, f.Owner.Id);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal(EvidenceModerationStatus.Pending, duplicate.Status);
        Assert.Single(await f.Db.ProjectEvidenceModerations.ToListAsync());
        Assert.Empty(await f.Db.ProjectVerifications.ToListAsync());
        Assert.Empty(await f.Db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task AutomaticReplacementCannotBypassStaleLevel1()
    {
        await using var f = await Fixture.CreateAsync();
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        await f.PayAsync(id, f.Owner);
        var now = DateTimeOffset.UtcNow;
        await PrepareOverdueAsync(f, id, now.AddHours(-48));
        f.Db.Accounts.Add(Fixture.Account("Replacement", AccountRole.Verificator));
        await f.Db.SaveChangesAsync();
        var tasks = new VerificationDeadlineTasks(f.Db, f.Configuration);
        await tasks.MarkOverdueAndEscalateAsync(now, default);
        f.Configuration["EvidenceModeration:PolicyVersion"] = "changed";
        await tasks.MarkOverdueAndEscalateAsync(now.AddHours(24), default);
        Assert.Single(await f.Db.VerificationAssignments.ToListAsync());
        Assert.Equal(PaymentStatus.Success, (await f.Db.Transactions.SingleAsync()).Status);
    }

    private sealed class NoPayments : IPaymentProvider
    {
        public string Name => "PayOS";
        public void EnsureConfigured() => throw new Exception("Provider must not be called");
        public Task<ProviderPaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest r, CancellationToken ct) => throw new Exception("Provider must not be called");
        public Task<ProviderPaymentResult> GetPaymentStatusAsync(string code, CancellationToken ct) => throw new Exception("Provider must not be called");
        public ProviderPaymentResult VerifyWebhook(JsonElement payload) => throw new Exception("Provider must not be called");
    }
}
