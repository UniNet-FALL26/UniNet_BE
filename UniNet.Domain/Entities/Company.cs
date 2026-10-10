using UniNet.Domain.Entities;

namespace UniNet.Domain;

public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public string CompanyName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Website { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Account Account { get; set; } = null!;
    public List<Job> Jobs { get; set; } = [];
    public List<CompanySavedStudent> SavedStudents { get; set; } = [];
}
