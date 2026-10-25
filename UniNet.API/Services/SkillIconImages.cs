using SkiaSharp;
using UniNet.Application;

namespace UniNet.API.Services;

public static class SkillIconImages
{
    private static readonly SemaphoreSlim Processing = new(1);
    public static byte[] Normalize(byte[] source)
    {
        using var image = GeminiPortraitProcessor.Decode(source);
        var scale = Math.Min(1d, 512d / Math.Max(image.Width, image.Height));
        using var resized = image.Resize(new SKImageInfo(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear));
        if (resized is null) throw new AuthException("INVALID_IMAGE", "Không đọc được ảnh icon.");
        using var png = resized.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    public static async Task<byte[]> RemoveBackground(byte[] source, OnnxPortraitProcessor processor, CancellationToken ct)
    {
        if (!await Processing.WaitAsync(0, ct)) throw new AuthException("ICON_PROCESSING_BUSY", "Đang xử lý ảnh khác. Vui lòng thử lại sau.", 429);
        try { return Normalize(await processor.Process(Normalize(source), ct)); }
        catch (AuthException error) when (error.Code == "INVALID_PORTRAIT_RESULT") {
            throw new AuthException("ICON_BACKGROUND_NOT_FOUND", "Không tách được nền ảnh icon này. Bạn có thể đổi ảnh hoặc lưu ảnh gốc.", 422);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException) {
            throw new AuthException("ICON_PROCESSING_UNAVAILABLE", "Chưa sẵn sàng dịch vụ xóa nền. Bạn có thể lưu ảnh gốc hoặc thử lại sau.", 503);
        }
        finally { Processing.Release(); }
    }
}
