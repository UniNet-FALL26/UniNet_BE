using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class FinanceWalletTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private UniNetDbContext db = null!;
    private FinanceService service = null!;
    private readonly Account owner = Make(AccountRole.Student);
    private readonly Account accountant = Make(AccountRole.Accountant);
    private readonly Account moderator = Make(AccountRole.Moderator);
    private static Account Make(AccountRole role) => new() { Email = Guid.NewGuid() + "@finance.test", Role = role, PasswordHash = "fixture", Profile = new() { FullName = role.ToString() } };
    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync(); db.Accounts.AddRange(owner, accountant, moderator); await db.SaveChangesAsync(); service = new(db);
    }
    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    private async Task Credit(decimal amount = 1000) => await service.CreditAsync(accountant.Id, new(owner.Id, amount, "INCOME-001", "Project income"), default);
    private WithdrawalRequest Request(decimal amount = 600) => new(Guid.NewGuid(), amount, "Bank", "123456", "OWNER");

    [Fact]
    public async Task ReservationsPreventOverdrawAndCancellationReleasesThem()
    {
        await Credit(); var withdrawal = await service.RequestAsync(owner.Id, Request(), default);
        Assert.Equal(400, (await service.WalletAsync(owner.Id, default)).Available);
        await Assert.ThrowsAsync<AuthException>(() => service.RequestAsync(owner.Id, Request(500), default));
        await service.CancelAsync(owner.Id, withdrawal.Id, default);
        Assert.Equal(1000, (await service.WalletAsync(owner.Id, default)).Available);
        await Assert.ThrowsAsync<AuthException>(() => service.CancelAsync(moderator.Id, withdrawal.Id, default));
    }

    [Fact]
    public async Task DurableIdempotencyRejectsChangedPayloadAndDuplicateCredit()
    {
        await Credit(); await Credit();
        await service.CreditAsync(accountant.Id, new(owner.Id, 1000, "income-001", "Project income"), default);
        await Assert.ThrowsAsync<AuthException>(() => service.CreditAsync(accountant.Id, new(owner.Id, 1, "withdrawal:reserved", "Invalid namespace"), default));
        Assert.Equal(1000, (await service.WalletAsync(owner.Id, default)).Balance);
        await Assert.ThrowsAsync<AuthException>(() => service.CreditAsync(accountant.Id, new(owner.Id, 2000, "INCOME-001", "Project income"), default));
        var request = Request(); var first = await service.RequestAsync(owner.Id, request, default);
        await service.CancelAsync(owner.Id, first.Id, default);
        Assert.Equal(first.Id, (await service.RequestAsync(owner.Id, request, default)).Id);
        await Assert.ThrowsAsync<AuthException>(() => service.RequestAsync(owner.Id, request with { Amount = 700 }, default));
    }

    [Fact]
    public async Task ManualPayoutRequiresOrderAndDebitsExactlyOnce()
    {
        await Credit(); var w = await service.RequestAsync(owner.Id, Request(), default);
        await Assert.ThrowsAsync<AuthException>(() => service.ConfirmAsync(accountant.Id, w.Id, new("BANK-ACCOUNT", "TX-001"), default));
        await service.ReviewAsync(accountant.Id, w.Id, new(true, "Approved"), default);
        await service.OrderAsync(accountant.Id, w.Id, default);
        await Assert.ThrowsAsync<AuthException>(() => service.ReviewAsync(accountant.Id, w.Id, new(false, "Reject"), default));
        await service.ConfirmAsync(accountant.Id, w.Id, new("BANK-ACCOUNT", "TX-001"), default);
        await service.ConfirmAsync(accountant.Id, w.Id, new("BANK-ACCOUNT", "TX-001"), default);
        Assert.Equal(400, (await service.WalletAsync(owner.Id, default)).Balance);
        Assert.Single(await db.Set<WalletEntry>().Where(e => e.WithdrawalId == w.Id).ToListAsync());
        await Assert.ThrowsAsync<AuthException>(() => service.ConfirmAsync(accountant.Id, w.Id, new("BANK-ACCOUNT", "TX-OTHER"), default));
    }

    [Fact]
    public async Task RoleScopeAndRevocationAreEnforcedAtBackend()
    {
        db.Set<NotificationPermissionOverride>().Add(new() { AccountId = moderator.Id, Permission = NotificationPermissionPolicy.WalletCredit, IsGranted = true });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ProjectVerificationException>(() => service.CreditAsync(moderator.Id, new(owner.Id, 100, "x", "reason"), default));
        db.Set<NotificationPermissionOverride>().Add(new() { AccountId = accountant.Id, Permission = NotificationPermissionPolicy.WalletCredit, IsGranted = false });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ProjectVerificationException>(() => Credit());
    }

    [Fact]
    public async Task RejectionAfterApprovalPreservesOriginalDecision()
    {
        await Credit(); var w = await service.RequestAsync(owner.Id, Request(), default);
        await service.ReviewAsync(accountant.Id, w.Id, new(true, "Original approval"), default);
        var originalTime = w.ReviewedAt;
        await service.ReviewAsync(accountant.Id, w.Id, new(false, "Recipient incorrect"), default);
        Assert.Equal("Original approval", w.ReviewReason); Assert.Equal(originalTime, w.ReviewedAt);
        Assert.Equal("Recipient incorrect", w.RejectionReason); Assert.Equal(accountant.Id, w.RejectedBy);
        Assert.Equal(1000, (await service.WalletAsync(owner.Id, default)).Available);
    }

    [Fact]
    public async Task RefundRequiresAnotherReviewerAndCannotOverspendReservedPayment()
    {
        var reviewer = Make(AccountRole.Admin); db.Add(reviewer);
        var package = new Package { Id = Guid.NewGuid(), Code = "FINANCE_TEST", Name = "Service", Price = 1000, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var payment = new UniNet.Domain.Entities.Transaction { Id = Guid.NewGuid(), UserId = owner.Profile!.Id, Package = package, Amount = 1000, Status = UniNet.Domain.Enums.PaymentStatus.Success, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Add(payment); await db.SaveChangesAsync();
        var refund = await service.CreateRefundAsync(accountant.Id, new(Guid.NewGuid(), payment.Id, 600, "Refund requested"), default);
        await Assert.ThrowsAsync<AuthException>(() => service.CreateRefundAsync(accountant.Id, new(Guid.NewGuid(), payment.Id, 500, "Too much"), default));
        await Assert.ThrowsAsync<AuthException>(() => service.ReviewRefundAsync(accountant.Id, refund.Id, new(true, "Self approval"), default));
        await Assert.ThrowsAsync<AuthException>(() => service.ConfirmRefundAsync(accountant.Id, refund.Id, new("BANK", "TX"), default));
        await service.ReviewRefundAsync(reviewer.Id, refund.Id, new(true, "Checked"), default);
        await service.ConfirmRefundAsync(accountant.Id, refund.Id, new("BANK", "TX"), default);
        await service.ConfirmRefundAsync(accountant.Id, refund.Id, new("BANK", "TX"), default);
        Assert.Equal(600, (await service.SummaryAsync(accountant.Id, default)).RefundsPaid);
        Assert.Equal(0, (await service.WalletAsync(owner.Id, default)).Balance);
        Assert.Equal(UniNet.Domain.Enums.PaymentStatus.Success, payment.Status);
    }

    [Fact]
    public async Task FailedTimeoutRefundRemainsReservedButRejectedReviewReleasesReservation()
    {
        var reviewer = Make(AccountRole.Admin); db.Add(reviewer);
        var package = new Package { Id = Guid.NewGuid(), Code = "RESERVE_TEST", Name = "Service", Price = 1000, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var payment = new UniNet.Domain.Entities.Transaction { Id = Guid.NewGuid(), UserId = owner.Profile!.Id, Package = package, Amount = 1000, Status = UniNet.Domain.Enums.PaymentStatus.Success, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Add(payment); db.Add(new RefundTransaction { Id = Guid.NewGuid(), Transaction = payment, Amount = 300, Status = UniNet.Domain.Enums.PaymentStatus.Failed, Reason = VerificationDeadlineTasks.AssignmentTimeout, PaymentProvider = "Manual", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(() => service.CreateRefundAsync(accountant.Id, new(Guid.NewGuid(), payment.Id, 800, "Too much"), default));
        var refund = await service.CreateRefundAsync(accountant.Id, new(Guid.NewGuid(), payment.Id, 700, "Reserve"), default);
        await service.ReviewRefundAsync(reviewer.Id, refund.Id, new(false, "Rejected"), default);
        await service.CreateRefundAsync(accountant.Id, new(Guid.NewGuid(), payment.Id, 700, "Replacement"), default);
    }
}
