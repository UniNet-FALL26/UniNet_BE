using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application.Services;
using UniNet.Domain.Entities;

namespace UniNet.API.Controllers;

public sealed record WalletEntryResponse(Guid Id, decimal Amount, string SourceReference, string Reason, DateTimeOffset CreatedAt)
{
    public static WalletEntryResponse From(WalletEntry e) => new(e.Id, e.Amount, e.SourceReference, e.Reason, e.CreatedAt);
}
public sealed record WithdrawalResponse(Guid Id, Guid AccountId, decimal Amount, string BankName, string BankAccountNumber, string BankAccountHolder, WithdrawalState State,
    DateTimeOffset CreatedAt, Guid? ReviewedBy, DateTimeOffset? ReviewedAt, string? ReviewReason, Guid? OrderedBy, DateTimeOffset? OrderedAt, Guid? ConfirmedBy, DateTimeOffset? ConfirmedAt, string? PayingAccount, string? BankReference, Guid? RejectedBy, DateTimeOffset? RejectedAt, string? RejectionReason)
{
    public static WithdrawalResponse From(WalletWithdrawal w) => new(w.Id, w.AccountId, w.Amount, w.BankName, w.BankAccountNumber, w.BankAccountHolder, w.State, w.CreatedAt, w.ReviewedBy, w.ReviewedAt, w.ReviewReason, w.OrderedBy, w.OrderedAt, w.ConfirmedBy, w.ConfirmedAt, w.PayingAccount, w.BankReference, w.RejectedBy, w.RejectedAt, w.RejectionReason);
}
public sealed record OwnerWithdrawalResponse(Guid Id, decimal Amount, string BankName, string BankAccountNumber, string BankAccountHolder, WithdrawalState State, DateTimeOffset CreatedAt, string? ReviewReason, string? BankReference)
{
    public static OwnerWithdrawalResponse From(WalletWithdrawal w) => new(w.Id, w.Amount, w.BankName, w.BankAccountNumber, w.BankAccountHolder, w.State, w.CreatedAt, w.RejectionReason ?? w.ReviewReason, w.BankReference);
}

// FinanceService checks the active owner's current database role on every wallet operation.
// A cached role claim must not prevent a role-2 account from opening its own wallet.
[UniNet.API.Services.FinanceJson, ApiController, Authorize, Route("api/wallet"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WalletController(FinanceService finance) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    [HttpGet] public async Task<object> Get(CancellationToken ct)
    {
        var result = await finance.HistoryAsync(Actor, ct);
        return new { result.Balance, RequestKey = Guid.NewGuid(), Entries = result.Entries.Select(WalletEntryResponse.From), Withdrawals = result.Withdrawals.Select(OwnerWithdrawalResponse.From) };
    }
    [HttpPost("withdrawals")] public async Task<OwnerWithdrawalResponse> CreateWithdrawal(WithdrawalRequest request, CancellationToken ct) => OwnerWithdrawalResponse.From(await finance.RequestAsync(Actor, request, ct));
    [HttpPost("withdrawals/{id:guid}/cancel")] public async Task<OwnerWithdrawalResponse> Cancel(Guid id, CancellationToken ct) => OwnerWithdrawalResponse.From(await finance.CancelAsync(Actor, id, ct));
}

[UniNet.API.Services.FinanceJson, ApiController, Authorize(Roles = "Admin,Accountant"), Route("api/admin/finance"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FinanceController(FinanceService finance, UniNet.Infrastructure.Data.UniNetDbContext db) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    [HttpGet("summary")] public Task<FinanceSummary> Summary(CancellationToken ct) => finance.SummaryAsync(Actor, ct);
    [HttpGet("recipients")] public Task<IReadOnlyList<FinanceRecipient>> Recipients([FromQuery] string? search, CancellationToken ct) => finance.RecipientsAsync(Actor, search, ct);
    [HttpGet("ledger")] public async Task<object> Ledger([FromQuery] int page = 1, CancellationToken ct = default)
    {
        var result = await finance.LedgerAsync(Actor, page, ct);
        return new { result.Page, result.PageSize, result.Total, Items = result.Items.Select(e => new { e.Id, e.AccountId, e.Amount, e.SourceReference, e.Reason, e.ActorAccountId, e.WithdrawalId, e.CreatedAt }) };
    }
    [HttpGet("payments")] public Task<FinancePage<FinancePayment>> Payments([FromQuery] int page = 1, CancellationToken ct = default) => finance.PaymentsAsync(Actor, page, ct);
    [HttpGet("refunds")] public Task<FinancePage<FinanceRefund>> Refunds([FromQuery] int page = 1, CancellationToken ct = default) => finance.RefundsAsync(Actor, page, ct);
    [HttpPost("refunds")] public Task<FinanceRefund> CreateRefund(FinanceRefundRequest request, CancellationToken ct) => finance.CreateRefundAsync(Actor, request, ct);
    [HttpPost("refunds/{id:guid}/review")] public Task<FinanceRefund> ReviewRefund(Guid id, FinanceReviewRequest request, CancellationToken ct) => finance.ReviewRefundAsync(Actor, id, request, ct);
    [HttpPost("refunds/{id:guid}/confirm")] public Task<FinanceRefund> ConfirmRefund(Guid id, PayoutConfirmation request, CancellationToken ct) => finance.ConfirmRefundAsync(Actor, id, request, ct);
    [HttpGet("withdrawals")] public async Task<FinancePage<WithdrawalResponse>> Withdrawals([FromQuery] int page = 1, CancellationToken ct = default)
    {
        var result = await finance.WithdrawalsAsync(Actor, page, ct);
        return new(result.Items.Select(WithdrawalResponse.From).ToArray(), result.Page, result.PageSize, result.Total);
    }
    [HttpPost("wallet/credits")] public async Task<WalletEntryResponse> Credit(WalletCreditRequest request, CancellationToken ct) => WalletEntryResponse.From(await finance.CreditAsync(Actor, request, ct));
    [HttpPost("withdrawals/{id:guid}/review")] public async Task<WithdrawalResponse> Review(Guid id, FinanceReviewRequest request, CancellationToken ct) => WithdrawalResponse.From(await finance.ReviewAsync(Actor, id, request, ct));
    [HttpPost("withdrawals/{id:guid}/order")] public async Task<WithdrawalResponse> Order(Guid id, CancellationToken ct) => WithdrawalResponse.From(await finance.OrderAsync(Actor, id, ct));
    [HttpPost("withdrawals/{id:guid}/confirm")] public async Task<WithdrawalResponse> Confirm(Guid id, PayoutConfirmation request, CancellationToken ct) => WithdrawalResponse.From(await finance.ConfirmAsync(Actor, id, request, ct));
    [HttpGet("report.csv")] public async Task<FileContentResult> Export(CancellationToken ct)
    {
        await NotificationPermissionPolicy.RequireAsync(db, Actor, NotificationPermissionPolicy.FinanceExport, ct);
        var r = await finance.SummaryAsync(Actor, ct);
        var csv = "Metric,VND\r\n" + string.Join("\r\n", new[] { ("PaymentsReceived", r.PaymentsReceived), ("RefundsPaid", r.RefundsPaid), ("WithdrawalsPaid", r.WithdrawalsPaid), ("WalletIncome", r.WalletIncome), ("PendingWithdrawals", r.PendingWithdrawals) }.Select(x => x.Item1 + "," + x.Item2.ToString(CultureInfo.InvariantCulture)));
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "finance-summary.csv");
    }
}
