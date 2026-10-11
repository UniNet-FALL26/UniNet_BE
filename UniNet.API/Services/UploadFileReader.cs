using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using UniNet.Application;

namespace UniNet.API.Services;

public sealed record UploadFile(byte[] Bytes, string Format, string ResourceType, string FileName);

public static class UploadFileReader
{
    public const int MaxDocumentBytes = 10 * 1024 * 1024;
    public const int MaxRequestBytes = MaxDocumentBytes + 65536;

    private const int MaxOfficePackageEntries = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly IReadOnlyDictionary<string, string> DocumentContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".txt"] = "text/plain"
        };

    public static async Task<UploadFile> Read(IFormFile file, CancellationToken ct)
    {
        if (file is null)
            throw Invalid("File must be between 1 byte and 10 MB.");

        var imageUploadHint = IsImageUploadHint(file);
        if (imageUploadHint && (file.Length <= 0 || file.Length > ImageFileReader.MaxBytes))
            throw InvalidImage("Ảnh phải có dung lượng từ 1 byte đến 5 MB.");
        if (file.Length <= 0 || file.Length > MaxDocumentBytes)
            throw Invalid("File must be between 1 byte and 10 MB.");
        var maxBytes = imageUploadHint ? ImageFileReader.MaxBytes : MaxDocumentBytes;
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                if (imageUploadHint)
                    throw InvalidImage("Ảnh không được lớn hơn 5 MB.");
                throw Invalid("File must not exceed 10 MB.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        var bytes = buffer.ToArray();
        var imageExtension = ImageFileReader.Extension(bytes);
        if (imageExtension is not null)
        {
            if (bytes.Length > ImageFileReader.MaxBytes)
                throw InvalidImage("Ảnh không được lớn hơn 5 MB.");

            var imageFormat = imageExtension[1..];
            return new(bytes, imageFormat, "image", NormalizeImageFileName(file.FileName, imageFormat));
        }

        if (imageUploadHint)
            throw InvalidImage("Vui lòng chọn ảnh JPG, PNG hoặc WebP hợp lệ.");

        var fileName = SafeFileName(file.FileName);
        var extension = Path.GetExtension(fileName);
        if (!DocumentContentTypes.TryGetValue(extension, out var expectedContentType) ||
            !string.Equals(file.ContentType?.Trim(), expectedContentType, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Supported documents must have a matching file extension and content type.");

        var documentFormat = extension[1..].ToLowerInvariant();
        if (!IsValidDocument(bytes, documentFormat))
            throw Invalid("File content does not match a supported document format.");

        return new(bytes, documentFormat, "raw", fileName);
    }

    private static bool IsValidDocument(byte[] bytes, string format)
        => format switch
        {
            "pdf" => IsPdf(bytes),
            "txt" => IsUtf8Text(bytes),
            "docx" => IsOfficeOpenXml(bytes, "word/document.xml"),
            "xlsx" => IsOfficeOpenXml(bytes, "xl/workbook.xml"),
            "pptx" => IsOfficeOpenXml(bytes, "ppt/presentation.xml"),
            _ => false
        };

    private static bool IsPdf(byte[] bytes)
    {
        if (!bytes.AsSpan().StartsWith("%PDF-"u8))
            return false;

        var end = bytes.Length;
        while (end > 0 && char.IsWhiteSpace((char)bytes[end - 1]))
            end--;

        return end >= 5 && bytes.AsSpan(end - 5, 5).SequenceEqual("%%EOF"u8);
    }

    private static bool IsUtf8Text(byte[] bytes)
    {
        if (bytes.AsSpan().Contains((byte)0))
            return false;

        try
        {
            _ = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsOfficeOpenXml(byte[] bytes, string requiredPart)
    {
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B)
            return false;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count > MaxOfficePackageEntries)
                return false;

            var hasContentTypes = false;
            var hasRequiredPart = false;
            foreach (var entry in archive.Entries)
            {
                hasContentTypes |= string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.Ordinal);
                hasRequiredPart |= string.Equals(entry.FullName, requiredPart, StringComparison.Ordinal);
            }
            return hasContentTypes && hasRequiredPart;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static string SafeFileName(string? clientFileName)
    {
        var normalized = (clientFileName ?? string.Empty).Replace('\\', '/');
        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255 ||
            fileName.Any(char.IsControl) || fileName is "." or "..")
            throw Invalid("A valid file name is required.");
        return fileName;
    }

    private static string NormalizeImageFileName(string? clientFileName, string format)
    {
        var normalized = (clientFileName ?? string.Empty).Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl) || name is "." or "..")
            return $"upload.{format}";

        var stem = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(stem))
            stem = "upload";
        if (stem.Length > 250)
            stem = stem[..250];
        return $"{stem}.{format}";
    }

    private static bool IsImageUploadHint(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        var contentType = file.ContentType?.Trim();
        return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(contentType, "image/webp", StringComparison.OrdinalIgnoreCase);
    }

    private static AuthException Invalid(string message)
        => new("INVALID_UPLOAD", message, 400);

    private static AuthException InvalidImage(string message)
        => new("INVALID_IMAGE", message, 400);
}
