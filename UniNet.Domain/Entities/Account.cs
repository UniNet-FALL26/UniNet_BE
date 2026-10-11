namespace UniNet.Domain;

public sealed class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string? GoogleEmail { get; set; }
    public AccountRole Role { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public bool EmailVerified { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserProfile? Profile { get; set; }
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
