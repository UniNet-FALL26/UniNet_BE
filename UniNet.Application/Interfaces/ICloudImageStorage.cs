namespace UniNet.Application.Interfaces;

public sealed record ImageUploadRequest(byte[] Bytes, string PublicId, string Format);
public sealed record ImageUploadResult(string ImageUrl, string PublicId, string Format);

public interface ICloudImageStorage
{
    Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct);
    Task Destroy(string publicId, CancellationToken ct);
}
