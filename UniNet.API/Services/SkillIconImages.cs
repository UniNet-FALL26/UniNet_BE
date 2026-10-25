using SkiaSharp;
using UniNet.Application;

namespace UniNet.API.Services;

public static class SkillIconImages
{
    private static readonly SemaphoreSlim Processing = new(1);
    public static byte[] FitToCanvas(byte[] source)
    {
        using var image = GeminiPortraitProcessor.Decode(source);
        var pixels = image.Pixels; var width = image.Width; var height = image.Height;
        var background = pixels[0];
        int Difference(SKColor color) => Math.Max(Math.Abs(color.Red - background.Red), Math.Max(Math.Abs(color.Green - background.Green), Math.Abs(color.Blue - background.Blue)));
        var border = 0; var matching = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) {
            if (x != 0 && y != 0 && x != width - 1 && y != height - 1) continue;
            border++; if (pixels[y * width + x].Alpha >= 250 && Difference(pixels[y * width + x]) <= 12) matching++;
        }
        var solidBackground = background.Alpha >= 250 && matching >= border * 0.95;
        var left = width; var top = height; var right = -1; var bottom = -1;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) {
            var pixel = pixels[y * width + x];
            if (pixel.Alpha <= 8 || solidBackground && Difference(pixel) <= 12) continue;
            left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left || bottom < top) { left = 0; top = 0; right = width - 1; bottom = height - 1; }
        using var cropped = new SKBitmap();
        if (!image.ExtractSubset(cropped, new SKRectI(left, top, right + 1, bottom + 1))) throw new AuthException("INVALID_IMAGE", "Không chuẩn hóa được ảnh icon.");
        var scale = 448d / Math.Max(cropped.Width, cropped.Height);
        using var resized = cropped.Resize(new SKImageInfo(Math.Max(1, (int)Math.Round(cropped.Width * scale)), Math.Max(1, (int)Math.Round(cropped.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear));
        if (resized is null) throw new AuthException("INVALID_IMAGE", "Không chuẩn hóa được ảnh icon.");
        using var output = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(solidBackground ? background : SKColors.Transparent);
        canvas.DrawBitmap(resized, (512 - resized.Width) / 2, (512 - resized.Height) / 2);
        using var png = output.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

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
        try {
            var normalized = Normalize(source);
            var solidBackground = RemoveSolidBackground(normalized, ct);
            return solidBackground ?? Normalize(await processor.Process(normalized, ct));
        }
        catch (AuthException error) when (error.Code == "INVALID_PORTRAIT_RESULT") {
            throw new AuthException("ICON_BACKGROUND_NOT_FOUND", "Không tách được nền ảnh icon này. Bạn có thể đổi ảnh hoặc lưu ảnh gốc.", 422);
        }
        catch (Exception error) when (!ct.IsCancellationRequested && error is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException or TypeInitializationException) {
            throw new AuthException("ICON_PROCESSING_UNAVAILABLE", "Chưa sẵn sàng dịch vụ xóa nền. Bạn có thể lưu ảnh gốc hoặc thử lại sau.", 503);
        }
        finally { Processing.Release(); }
    }

    // Flat backgrounds of any color are edge-connected; enclosed logo details stay intact.
    private static byte[]? RemoveSolidBackground(byte[] source, CancellationToken ct)
    {
        using var bitmap = SKBitmap.Decode(source);
        var width = bitmap.Width; var height = bitmap.Height; var pixels = bitmap.Pixels;
        var background = pixels[0];
        if (background.Alpha < 250) return null;
        int Difference(SKColor color) => Math.Max(Math.Abs(color.Red - background.Red), Math.Max(Math.Abs(color.Green - background.Green), Math.Abs(color.Blue - background.Blue)));
        var border = 0; var matching = 0;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) {
            if (x != 0 && y != 0 && x != width - 1 && y != height - 1) continue;
            border++; if (pixels[y * width + x].Alpha >= 250 && Difference(pixels[y * width + x]) <= 12) matching++;
        }
        if (matching < border * 0.95) return null;
        var visited = new bool[pixels.Length]; var queue = new int[pixels.Length]; var head = 0; var tail = 0;
        void Add(int index) {
            if (visited[index] || pixels[index].Alpha != 0 && Difference(pixels[index]) > 32) return;
            visited[index] = true; queue[tail++] = index;
        }
        for (var x = 0; x < width; x++) { Add(x); Add((height - 1) * width + x); }
        for (var y = 0; y < height; y++) { Add(y * width); Add(y * width + width - 1); }
        while (head < tail) {
            if ((head & 1023) == 0) ct.ThrowIfCancellationRequested();
            var index = queue[head++]; var x = index % width; var y = index / width;
            var color = pixels[index];
            var alpha = (byte)(color.Alpha * Math.Clamp((Difference(color) - 8) / 24d, 0, 1));
            pixels[index] = color.WithAlpha(alpha);
            if (x > 0) Add(index - 1); if (x + 1 < width) Add(index + 1);
            if (y > 0) Add(index - width); if (y + 1 < height) Add(index + width);
        }
        if (!pixels.Any(pixel => pixel.Alpha > 32)) throw new AuthException("ICON_BACKGROUND_NOT_FOUND", "Không tìm thấy phần icon để giữ lại trên ảnh nền màu này. Hãy chọn ảnh khác.", 422);
        bitmap.Pixels = pixels;
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
