namespace UniNet.Domain;

public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Bio { get; set; }
    public string? UniversityName { get; set; }
    public string? Major { get; set; }
    public string? StudentCode { get; set; }
    public PartnerType? PartnerType { get; set; }
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public string? ContactEmail { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? TaxCode { get; set; }
    public bool IsVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
