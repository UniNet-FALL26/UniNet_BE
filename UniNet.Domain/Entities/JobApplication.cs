namespace UniNet.Domain.Entities;

public sealed class JobApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid StudentUserId { get; set; }
    public string? CoverMessage { get; set; }
    public decimal? AiMatchScore { get; set; }
    public string? ProfileSnapshot { get; set; }
    public short Status { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Job Job { get; set; } = null!;
    public UserProfile Student { get; set; } = null!;
}
