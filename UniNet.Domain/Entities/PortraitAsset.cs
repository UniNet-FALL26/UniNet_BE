namespace UniNet.Domain;

public sealed class PortraitAsset
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string PublicId { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public long CreatedAtUnix { get; set; }
    // 0: uploaded draft, 1: retained, 2: cleanup pending, 3: durable upload intent.
    public int State { get; set; }
}
