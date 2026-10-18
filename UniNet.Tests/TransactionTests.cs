using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class TransactionTests
{
    [Fact]
    public async Task CheckoutSnapshotsPackagePriceAndCommitsAttemptBeforeProviderCall()
    {
        await using var f = await Fixture.CreateAsync();
        f.Provider.OnCreate = async request =>
        {
            await using var observer = new UniNetDbContext(f.Options);
            var stored = await observer.Transactions.AsNoTracking().Include(t => t.VerificationTransaction).SingleAsync();
            Assert.Equal(PaymentStatus.Pending, stored.Status);
            Assert.Equal(f.Verification.Id, stored.VerificationTransaction!.VerificationId);
            Assert.Equal(37000, request.AmountVnd);
            return FakeProvider.Result(request.OrderCode, request.AmountVnd);
        };
        var result = await f.CheckoutAsync();
        Assert.Equal(37000, result.AmountVnd);
        Assert.Equal("https://pay.payos.vn/web/test", result.CheckoutUrl);
        Assert.False(result.ReconciliationRequired);
        f.Package.Price = 42000;
        await f.Db.SaveChangesAsync();
        var reused = await f.CheckoutAsync();
        Assert.Equal(result.Id, reused.Id);
        Assert.Equal(37000, reused.AmountVnd);
        Assert.Equal(1, f.Provider.CreateCalls);
        Assert.Equal(ProjectVerificationStatus.Pending, f.Verification.Status);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("advertisement")]
    [InlineData("company")]
    [InlineData("zero")]
    [InlineData("fractional")]
    public async Task InvalidPackageNeverCreatesAttempt(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        if (scenario == "inactive") f.Package.IsActive = false;
        if (scenario == "advertisement") f.Package.Type = PackageType.Advertisement;
        if (scenario == "company") f.Package.Role = TargetRole.Company;
        if (scenario == "zero") f.Package.Price = 0;
        if (scenario == "fractional") f.Package.Price = 25000.5m;
        await f.Db.SaveChangesAsync();
        await Error(scenario is "zero" or "fractional" ? "INVALID_PAYMENT_AMOUNT" : "INVALID_VERIFICATION_PACKAGE", f.CheckoutAsync);
        Assert.Empty(await f.Db.Transactions.ToListAsync());
        Assert.Equal(0, f.Provider.CreateCalls);
    }

    [Fact]
    public async Task OwnershipAndAssociationAreEnforcedForEveryUserOperation()
    {
        await using var f = await Fixture.CreateAsync();
        await Error("FORBIDDEN", () => f.Service.CreateAsync(f.Verification.Id, f.Other.Id));
        var result = await f.CheckoutAsync();
        await Error("FORBIDDEN", () => f.Service.GetHistoryAsync(f.Verification.Id, f.Other.Id));
        await Error("FORBIDDEN", () => f.Service.GetAsync(f.Verification.Id, result.Id, f.Other.Id));
        await Error("FORBIDDEN", () => f.Service.ReconcileAsync(result.Id, f.Other.Id));
        var otherVerification = new ProjectVerification
        {
            Id = Guid.NewGuid(), Project = f.Project, RequestByUserId = f.Owner.Profile!.Id,
            PackageId = f.Package.Id, Status = ProjectVerificationStatus.Cancelled
        };
        f.Db.ProjectVerifications.Add(otherVerification);
        await f.Db.SaveChangesAsync();
        await Error("TRANSACTION_NOT_FOUND", () => f.Service.GetAsync(otherVerification.Id, result.Id, f.Owner.Id));
    }

    [Theory]
    [InlineData(ProjectVerificationStatus.Cancelled)]
    [InlineData(ProjectVerificationStatus.Verified)]
    [InlineData(ProjectVerificationStatus.Failed)]
    public async Task FinalVerificationCannotBePaid(ProjectVerificationStatus status)
    {
        await using var f = await Fixture.CreateAsync();
        f.Verification.Status = status;
        await f.Db.SaveChangesAsync();
        await Error("INVALID_VERIFICATION_STATE", f.CheckoutAsync);
        Assert.Empty(await f.Db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task TimeoutRemainsPendingAndRetryReturnsSameAttemptUntilReconciled()
    {
        await using var f = await Fixture.CreateAsync();
        f.Provider.OnCreate = _ => throw new PaymentProviderException("PROVIDER_STATUS_UNKNOWN", "Timeout");
        var pending = await f.CheckoutAsync();
        Assert.Equal(PaymentStatus.Pending, pending.Status);
        Assert.True(pending.ReconciliationRequired);
        Assert.Null(pending.CheckoutUrl);
        Assert.Equal(pending.Id, (await f.CheckoutAsync()).Id);
        Assert.Equal(1, f.Provider.CreateCalls);
        f.Provider.StatusResult = FakeProvider.Result(pending.ProviderOrderCode!, 37000, PaymentStatus.Success);
        var paid = await f.Service.ReconcileAsync(pending.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Success, paid.Status);
        Assert.NotNull(paid.PaidAt);
        await Error("VERIFICATION_ALREADY_PAID", f.CheckoutAsync);
    }

    [Fact]
    public async Task ExplicitRejectionAllowsNewAttemptButHistoryPreservesFailure()
    {
        await using var f = await Fixture.CreateAsync();
        f.Provider.OnCreate = _ => throw new PaymentProviderException("PROVIDER_REJECTED", "Rejected", true);
        var failed = await f.CheckoutAsync();
        Assert.Equal(PaymentStatus.Failed, failed.Status);
        f.Provider.OnCreate = null;
        var retried = await f.CheckoutAsync();
        Assert.NotEqual(failed.Id, retried.Id);
        var history = await f.Service.GetHistoryAsync(f.Verification.Id, f.Owner.Id, 1, 1);
        Assert.Equal(2, history.TotalItems);
        Assert.Equal(2, history.TotalPages);
        Assert.Single(history.Transactions);
    }

    [Fact]
    public async Task RepeatedWebhookIsIdempotentAndStaleStatusDoesNotDowngradeSuccess()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.CheckoutAsync();
        f.Provider.WebhookResult = FakeProvider.Result(created.ProviderOrderCode!, 37000, PaymentStatus.Success);
        var response = await f.Service.HandlePayOSWebhookAsync(default);
        Assert.True(response.Processed);
        var first = await f.Service.GetAsync(f.Verification.Id, created.Id, f.Owner.Id);
        await f.Service.HandlePayOSWebhookAsync(default);
        f.Provider.WebhookResult = f.Provider.WebhookResult with { Status = PaymentStatus.Pending };
        await f.Service.HandlePayOSWebhookAsync(default);
        var second = await f.Service.GetAsync(f.Verification.Id, created.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Success, second.Status);
        Assert.Equal(first.PaidAt, second.PaidAt);
        Assert.Equal(first.UpdatedAt, second.UpdatedAt);
        Assert.Single(await f.Db.Transactions.ToListAsync());
        Assert.Empty(await f.Db.VerificationAssignments.ToListAsync());
        Assert.Empty(await f.Db.RefundTransactions.ToListAsync());
        Assert.Equal(ProjectVerificationStatus.Pending, f.Verification.Status);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("currency")]
    [InlineData("link")]
    public async Task WebhookDataMismatchCannotCreditPayment(string scenario)
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.CheckoutAsync();
        var result = FakeProvider.Result(created.ProviderOrderCode!, 37000, PaymentStatus.Success);
        f.Provider.WebhookResult = scenario switch
        {
            "amount" => result with { AmountVnd = 25000 },
            "currency" => result with { Currency = "USD" },
            _ => result with { PaymentLinkId = "another-link" }
        };
        await Error("PAYMENT_DATA_MISMATCH", () => f.Service.HandlePayOSWebhookAsync(default));
        Assert.Equal(PaymentStatus.Pending, (await f.Service.GetAsync(f.Verification.Id, created.Id, f.Owner.Id)).Status);
    }

    [Fact]
    public async Task WebhookCanRecoverPaymentLinkIdAfterAmbiguousCreation()
    {
        await using var f = await Fixture.CreateAsync();
        f.Provider.OnCreate = _ => throw new PaymentProviderException("PROVIDER_STATUS_UNKNOWN", "Timeout");
        var created = await f.CheckoutAsync();
        f.Provider.WebhookResult = FakeProvider.Result(created.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        var result = await f.Service.GetAsync(f.Verification.Id, created.Id, f.Owner.Id);
        Assert.Equal("test-link", result.ProviderPaymentLinkId);
        Assert.Equal(PaymentStatus.Success, result.Status);
    }

    [Fact]
    public async Task ProviderClosureWithPartialPaymentBlocksAnotherCheckout()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.CheckoutAsync();
        f.Provider.StatusResult = FakeProvider.Result(created.ProviderOrderCode!, 37000, PaymentStatus.Failed)
            with { AmountPaidVnd = 10000 };
        var partial = await f.Service.ReconcileAsync(created.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Pending, partial.Status);
        Assert.True(partial.RequiresReview);
        Assert.Equal(created.Id, (await f.CheckoutAsync()).Id);
    }

    [Fact]
    public async Task ConfirmedClosedUnpaidAttemptAllowsSafeRetry()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.CheckoutAsync();
        f.Provider.StatusResult = FakeProvider.Result(created.ProviderOrderCode!, 37000, PaymentStatus.Failed)
            with { AmountPaidVnd = 0 };
        Assert.Equal(PaymentStatus.Failed, (await f.Service.ReconcileAsync(created.Id, f.Owner.Id)).Status);
        f.Provider.OnCreate = request => Task.FromResult(FakeProvider.Result(request.OrderCode, request.AmountVnd)
            with { PaymentLinkId = "retry-link" });
        Assert.NotEqual(created.Id, (await f.CheckoutAsync()).Id);
    }

    [Fact]
    public async Task ClosedLinkWithoutConfirmedPaidAmountRemainsUnresolved()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.CheckoutAsync();
        f.Provider.StatusResult = FakeProvider.Result(created.ProviderOrderCode!, 37000, PaymentStatus.Failed)
            with { AmountPaidVnd = null };
        var result = await f.Service.ReconcileAsync(created.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Pending, result.Status);
        Assert.True(result.ReconciliationRequired);
        Assert.Equal(created.Id, (await f.CheckoutAsync()).Id);
    }

    [Fact]
    public async Task InsufficientConfirmedAmountCannotCreditPayment()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.CheckoutAsync();
        f.Provider.StatusResult = FakeProvider.Result(created.ProviderOrderCode!, 37000, PaymentStatus.Success)
            with { AmountPaidVnd = 1000 };
        await Error("PAYMENT_DATA_MISMATCH", () => f.Service.ReconcileAsync(created.Id, f.Owner.Id));
        Assert.Equal(PaymentStatus.Pending, (await f.Service.GetAsync(f.Verification.Id, created.Id, f.Owner.Id)).Status);
    }

    [Fact]
    public async Task InactiveAccountAndMissingCheckoutSettingsCannotCreatePayment()
    {
        await using var f = await Fixture.CreateAsync();
        f.Owner.Status = AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await Error("FORBIDDEN", f.CheckoutAsync);
        f.Owner.Status = AccountStatus.Active;
        await f.Db.SaveChangesAsync();
        f.Configuration["Payments:ReturnUrl"] = null;
        await Error("PAYMENT_NOT_CONFIGURED", f.CheckoutAsync);
        Assert.Empty(await f.Db.Transactions.ToListAsync());
    }

    [Fact]
    public async Task ReconciliationCanBeDisabledAndLocalExpiryDoesNotAllowRetry()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.CheckoutAsync();
        var stored = await f.Db.Transactions.SingleAsync();
        stored.ExpiredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        Assert.Equal(created.Id, (await f.CheckoutAsync()).Id);
        f.Configuration["Payments:AllowUserReconciliation"] = "false";
        await Error("RECONCILIATION_DISABLED", () => f.Service.ReconcileAsync(created.Id, f.Owner.Id));
    }

    [Fact]
    public async Task DuplicateRealChargeIsRecordedForReviewWithoutSecondAcceptedCharge()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.CheckoutAsync();
        f.Provider.WebhookResult = FakeProvider.Result(first.ProviderOrderCode!, 37000, PaymentStatus.Success);
        await f.Service.HandlePayOSWebhookAsync(default);
        var second = new Transaction
        {
            Id = Guid.NewGuid(), UserId = f.Owner.Profile!.Id, PackageId = f.Package.Id, Amount = 37000,
            PaymentMethod = "PayOS", ProviderOrderCode = "second", CreatedAt = DateTimeOffset.UtcNow,
            VerificationTransaction = new() { VerificationId = f.Verification.Id }
        };
        f.Db.Transactions.Add(second);
        await f.Db.SaveChangesAsync();
        f.Provider.WebhookResult = FakeProvider.Result("second", 37000, PaymentStatus.Success) with { PaymentLinkId = "second-link" };
        await f.Service.HandlePayOSWebhookAsync(default);
        var result = await f.Service.GetAsync(f.Verification.Id, second.Id, f.Owner.Id);
        Assert.Equal(PaymentStatus.Success, result.Status);
        Assert.True(result.RequiresReview);
        Assert.Equal(TransactionMapping.DuplicatePaymentReview, result.DiagnosticCode);
        Assert.False((await f.Service.GetAsync(f.Verification.Id, first.Id, f.Owner.Id)).RequiresReview);
    }

    [Fact]
    public async Task SignedUnmatchedWebhookDoesNotModifyAdvertisementTransactions()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.Transactions.Add(new()
        {
            Id = Guid.NewGuid(), UserId = f.Owner.Profile!.Id, PackageId = f.Package.Id,
            Amount = 37000, Status = PaymentStatus.Pending, PaymentMethod = "PayOS", ProviderOrderCode = "ad-order"
        });
        await f.Db.SaveChangesAsync();
        f.Provider.WebhookResult = FakeProvider.Result("ad-order", 37000, PaymentStatus.Success);
        Assert.False((await f.Service.HandlePayOSWebhookAsync(default)).Processed);
        Assert.Equal(PaymentStatus.Pending, (await f.Db.Transactions.SingleAsync()).Status);
    }

    [Fact]
    public async Task SecondCheckoutWhileFirstProviderRequestIsInFlightReusesCommittedAttempt()
    {
        await using var f = await Fixture.CreateAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Provider.OnCreate = async request =>
        {
            started.SetResult();
            await finish.Task;
            return FakeProvider.Result(request.OrderCode, request.AmountVnd);
        };
        var first = f.CheckoutAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var otherContext = new UniNetDbContext(f.Options);
        var otherService = new TransactionService(otherContext, f.Provider, f.Configuration, NullLogger<TransactionService>.Instance);
        var second = await otherService.CreateAsync(f.Verification.Id, f.Owner.Id);
        Assert.True(second.ReconciliationRequired);
        Assert.Equal(1, f.Provider.CreateCalls);
        finish.SetResult();
        Assert.Equal(second.Id, (await first).Id);
        Assert.Single(await f.Db.Transactions.ToListAsync());
    }

    private static async Task Error(string code, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<TransactionException>(action);
        Assert.Equal(code, error.Code);
    }

    private sealed class FakeProvider : IPaymentProvider
    {
        public string Name => "PayOS";
        public int CreateCalls { get; private set; }
        public Func<CreateProviderPaymentRequest, Task<ProviderPaymentResult>>? OnCreate { get; set; }
        public ProviderPaymentResult? StatusResult { get; set; }
        public ProviderPaymentResult? WebhookResult { get; set; }
        public void EnsureConfigured() { }
        public Task<ProviderPaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
        {
            CreateCalls++;
            return OnCreate?.Invoke(request) ?? Task.FromResult(Result(request.OrderCode, request.AmountVnd));
        }
        public Task<ProviderPaymentResult> GetPaymentStatusAsync(string orderCode, CancellationToken ct)
            => Task.FromResult(StatusResult ?? Result(orderCode, 37000));
        public ProviderPaymentResult VerifyWebhook(JsonElement payload) => WebhookResult!;
        public static ProviderPaymentResult Result(string orderCode, decimal amount, PaymentStatus status = PaymentStatus.Pending)
            => new(orderCode, amount, "VND", "test-link", status, "https://pay.payos.vn/web/test",
                status == PaymentStatus.Success ? amount : 0, "reference");
    }

    private sealed class Fixture(SqliteConnection connection, UniNetDbContext db,
        DbContextOptions<UniNetDbContext> options, IConfiguration configuration,
        Account owner, Account other, Project project, Package package, ProjectVerification verification) : IAsyncDisposable
    {
        public UniNetDbContext Db { get; } = db;
        public DbContextOptions<UniNetDbContext> Options { get; } = options;
        public IConfiguration Configuration { get; } = configuration;
        public Account Owner { get; } = owner;
        public Account Other { get; } = other;
        public Project Project { get; } = project;
        public Package Package { get; } = package;
        public ProjectVerification Verification { get; } = verification;
        public FakeProvider Provider { get; } = new();
        public TransactionService Service => new(Db, Provider, Configuration, NullLogger<TransactionService>.Instance);
        public Task<TransactionResponse> CheckoutAsync() => Service.CreateAsync(Verification.Id, Owner.Id);
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options;
            var db = new UniNetDbContext(options);
            await db.Database.EnsureCreatedAsync();
            Account Student(string name) => new()
            {
                Email = $"{name}@test.com", Role = AccountRole.Student,
                Profile = new() { FullName = name, Nickname = name }
            };
            var owner = Student("Owner");
            var other = Student("Other");
            var project = new Project { Creator = owner.Profile!, Title = "Test", Description = "Completed project",
                ProjectField = "IT", MemberTarget = 3, Status = ProjectStatus.Completed };
            var package = new Package { Id = Guid.NewGuid(), Code = "VERIFY", Name = "Verification",
                Type = PackageType.Verification, Role = TargetRole.Student, IsActive = true, Price = 37000 };
            var verification = new ProjectVerification { Id = Guid.NewGuid(), Project = project,
                RequestByUser = owner.Profile!, Package = package, Status = ProjectVerificationStatus.Pending };
            db.Accounts.AddRange(owner, other);
            db.ProjectVerifications.Add(verification);
            await db.SaveChangesAsync();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:ReturnUrl"] = "https://frontend.test/paid",
                ["Payments:CancelUrl"] = "https://frontend.test/cancel"
            }).Build();
            var source = new EvidenceModerationSource(db, configuration);
            var snapshot = JsonSerializer.Serialize(await source.CaptureAsync(project.Id, default));
            var policy = JsonSerializer.Serialize(source.CapturePolicy());
            var review = new ProjectEvidenceModeration { ProjectId = project.Id, Version = 1,
                SnapshotJson = snapshot, SourceHash = EvidenceModerationSource.Hash(snapshot),
                PolicyJson = policy, PolicyHash = EvidenceModerationSource.Hash(policy),
                Status = EvidenceModerationStatus.Passed, CreatedAt = DateTimeOffset.UtcNow };
            verification.EvidenceModeration = review;
            db.ProjectEvidenceModerations.Add(review);
            await db.SaveChangesAsync();
            return new(connection, db, options, configuration, owner, other, project, package, verification);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
