namespace UniNet.API.Services;

public sealed class ProfileImageStorage(IWebHostEnvironment environment)
{
    public const int MaxBytes = ImageFileReader.MaxBytes;
    private readonly string directory = Path.Combine(environment.ContentRootPath, "App_Data", "profile-images");

    public static string? Extension(ReadOnlySpan<byte> bytes) => ImageFileReader.Extension(bytes);

    public async Task<string> Save(IFormFile file, CancellationToken ct)
    {
        var image = await ImageFileReader.Read(file, ct);
        Directory.CreateDirectory(directory);
        var name = Guid.NewGuid().ToString("N") + "." + image.Format;
        var path = Path.Combine(directory, name);
        try { await File.WriteAllBytesAsync(path, image.Bytes, ct); }
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
}
