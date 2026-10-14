using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Controllers;

[ApiController, Authorize(Roles = "Student"), Route("api/project-verifications/{verificationId:guid}/payments")]
public sealed class TransactionController(ITransactionService transactions) : ControllerBase
{
    private Guid AccountId => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
        ? id : throw new TransactionException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401);

    /// <summary>Tạo checkout hoặc trả lại giao dịch Pending hiện có. Giá lấy từ package của verification.</summary>
    [HttpPost]
    public async Task<ActionResult<TransactionResponse>> Create(Guid verificationId, CancellationToken ct)
    {
        var result = await transactions.CreateAsync(verificationId, AccountId, ct);
        return CreatedAtAction(nameof(Get), new { verificationId, transactionId = result.Id }, result);
    }

    [HttpGet]
    public Task<TransactionsResponse> GetHistory(Guid verificationId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => transactions.GetHistoryAsync(verificationId, AccountId, page, pageSize, ct);

    /// <summary>Đọc trạng thái đã lưu; dùng reconcile để đối soát với PayOS.</summary>
    [HttpGet("{transactionId:guid}")]
    public Task<TransactionResponse> Get(Guid verificationId, Guid transactionId, CancellationToken ct)
        => transactions.GetAsync(verificationId, transactionId, AccountId, ct);

    [HttpPost("~/api/payments/{transactionId:guid}/reconcile")]
    public Task<TransactionResponse> Reconcile(Guid transactionId, CancellationToken ct)
        => transactions.ReconcileAsync(transactionId, AccountId, ct);

    /// <summary>PayOS webhook được xác thực bằng checksum signature, không dùng JWT.</summary>
    [AllowAnonymous, HttpPost("~/api/webhooks/payos"), RequestSizeLimit(65536)]
    public Task<PaymentWebhookResponse> PayOSWebhook([FromBody] JsonElement payload, CancellationToken ct)
        => transactions.HandlePayOSWebhookAsync(payload, ct);
}
