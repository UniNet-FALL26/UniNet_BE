namespace UniNet.Domain.Entities;

public sealed class StudentSavedJob
{
    public Guid StudentUserId { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public UserProfile Student { get; set; } = null!;
    public Job Job { get; set; } = null!;
}
