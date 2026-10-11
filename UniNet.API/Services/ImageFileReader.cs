using UniNet.Application;

namespace UniNet.API.Services;

public sealed record ImageFile(byte[] Bytes, string Format);

public static class ImageFileReader
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public static string? Extension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 33 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return ".png";
        if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217) return ".jpg";
        if (bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    public static async Task<ImageFile> Read(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > MaxBytes) throw Invalid("Ảnh phải có dung lượng từ 1 byte đến 5 MB.");
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920]; int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw Invalid("Ảnh không được lớn hơn 5 MB.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var bytes = buffer.ToArray();
        var extension = Extension(bytes) ?? throw Invalid("Vui lòng chọn ảnh JPG, PNG hoặc WebP hợp lệ.");
        return new(bytes, extension[1..]);
    }

    private static AuthException Invalid(string message) => new("INVALID_IMAGE", message, 400);
}
