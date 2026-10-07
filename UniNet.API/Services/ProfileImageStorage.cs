using UniNet.Application;

namespace UniNet.API.Services;

public sealed class ProfileImageStorage(IWebHostEnvironment environment)
{
    public const int MaxBytes = 5 * 1024 * 1024;
    private readonly string directory = Path.Combine(environment.ContentRootPath, "App_Data", "profile-images");

    public static string? Extension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 33 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return ".png";
        if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255 && bytes[^2] == 255 && bytes[^1] == 217) return ".jpg";
        if (bytes.Length >= 16 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    public async Task<string> Save(IFormFile file, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > MaxBytes) throw Invalid("Ảnh phải có dung lượng từ 1 byte đến 5 MB.");
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw Invalid("Ảnh không được lớn hơn 5 MB.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        var bytes = buffer.ToArray();
        var extension = Extension(bytes) ?? throw Invalid("Vui lòng chọn ảnh JPG, PNG hoặc WebP hợp lệ.");
        Directory.CreateDirectory(directory);
        var name = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(directory, name);
        try { await File.WriteAllBytesAsync(path, bytes, ct); }
        catch { File.Delete(path); throw; }
        return name;
    }

    public string? Find(string name)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-f0-9]{32}\\.(png|jpg|webp)$")) return null;
        var path = Path.Combine(directory, name);
        return File.Exists(path) ? path : null;
    }

    public static string ContentType(string name) => Path.GetExtension(name) switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
    private static AuthException Invalid(string message) => new("INVALID_IMAGE", message, 400);
}
