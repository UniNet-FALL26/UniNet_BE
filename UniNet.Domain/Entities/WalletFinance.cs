namespace UniNet.Domain.Entities;

public enum WithdrawalState : short { Requested, Approved, Ordered, Paid, Rejected, Cancelled }
public enum RefundApproval : short { NotRequired, Pending, Approved, Rejected }

public sealed class WalletEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public string SourceReference { get; set; } = "";
    public string Reason { get; set; } = "";
    public Guid ActorAccountId { get; set; }
    public Guid? WithdrawalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WalletWithdrawal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid RequestKey { get; set; }
    public decimal Amount { get; set; }
    public string BankName { get; set; } = "";
    public string BankAccountNumber { get; set; } = "";
    public string BankAccountHolder { get; set; } = "";
    public WithdrawalState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? RejectedBy { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? OrderedBy { get; set; }
    public DateTimeOffset? OrderedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? PayingAccount { get; set; }
    public string? BankReference { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
