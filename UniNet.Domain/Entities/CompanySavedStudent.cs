namespace UniNet.Domain.Entities;

public sealed class CompanySavedStudent
{
    public Guid CompanyUserId { get; set; }
    public Guid StudentUserId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public Company Company { get; set; } = null!;
    public UserProfile Student { get; set; } = null!;
}
