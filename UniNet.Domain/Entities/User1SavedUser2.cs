namespace UniNet.Domain.Entities;

public sealed class User1SavedUser2
{
    public Guid UserBookmarkId { get; set; }
    public Guid UserId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public UniNet.Domain.UserProfile UserBookmark { get; set; } = null!;
    public UniNet.Domain.UserProfile User { get; set; } = null!;
}