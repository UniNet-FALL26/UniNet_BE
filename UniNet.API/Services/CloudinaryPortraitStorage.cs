using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Services;

public interface IPortraitCloudStorage
{
    Task<string> Upload(byte[] png, string publicId, CancellationToken ct);
    Task Destroy(string publicId, CancellationToken ct);
}

// Portrait format and error contract stay here; Cloudinary transport is shared by all features.
public sealed class CloudinaryPortraitStorage(ICloudImageStorage images) : IPortraitCloudStorage
{
    public async Task<string> Upload(byte[] png, string publicId, CancellationToken ct)
    {
        try { return (await images.Upload(new(png, publicId, "png"), ct)).ImageUrl; }
        catch (AuthException error) when (error.Code.StartsWith("IMAGE_", StringComparison.Ordinal))
        { throw new AuthException(error.Code.Replace("IMAGE_", "PORTRAIT_", StringComparison.Ordinal), error.Message, error.Status); }
    }

    public async Task Destroy(string publicId, CancellationToken ct)
    {
        try { await images.Destroy(publicId, ct); }
        catch (AuthException error) when (error.Code.StartsWith("IMAGE_", StringComparison.Ordinal))
        { throw new AuthException(error.Code.Replace("IMAGE_", "PORTRAIT_", StringComparison.Ordinal), error.Message, error.Status); }
    }
}
