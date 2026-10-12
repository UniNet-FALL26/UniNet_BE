namespace UniNet.Application.Interfaces;

public sealed record ImageUploadRequest(
    byte[] Bytes,
    string PublicId,
    string Format,
    string ResourceType = "image");
public sealed record ImageUploadResult(
    string ImageUrl,
    string PublicId,
    string Format,
    string ResourceType = "image",
    string? FileName = null);

public interface ICloudImageStorage
{
    Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct);
    Task Destroy(string publicId, CancellationToken ct);
}
