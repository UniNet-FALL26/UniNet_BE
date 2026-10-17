using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using UniNet.Application;

namespace UniNet.API.Services;

public sealed record UploadFile(byte[] Bytes, string Format, string ResourceType, string FileName);

public static class UploadFileReader
{
    public const int MaxVideoBytes = 50 * 1024 * 1024;
    public const int MaxDocumentBytes = 10 * 1024 * 1024;
    public const int MaxRequestBytes = MaxVideoBytes + 65536;

    private const int MaxOfficePackageEntries = 1024;
    private const int MaxWebmHeaderBytes = 4096;
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

    private static readonly IReadOnlyDictionary<string, string> VideoContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
            [".mov"] = "video/quicktime"
        };

    private static readonly HashSet<string> Mp4Brands = new(StringComparer.Ordinal)
    {
        "isom", "iso2", "iso3", "iso4", "iso5", "iso6", "iso7", "iso8", "iso9",
        "mp41", "mp42", "avc1", "M4V ", "3gp4", "3gp5"
    };

    public static int GetMaxVideoBytes(IConfiguration configuration)
    {
        var maxVideoBytes = configuration.GetValue<int?>("Uploads:MaxVideoBytes") ?? MaxVideoBytes;
        if (maxVideoBytes is < 1 or > MaxVideoBytes)
            throw new InvalidOperationException(
                $"Uploads:MaxVideoBytes must be between 1 and {MaxVideoBytes} bytes.");
        return maxVideoBytes;
    }

    public static async Task<UploadFile> Read(
        IFormFile file,
        CancellationToken ct,
        int maxVideoBytes = MaxVideoBytes)
    {
        if (maxVideoBytes is < 1 or > MaxVideoBytes)
            throw new ArgumentOutOfRangeException(nameof(maxVideoBytes));
        if (file is null)
            throw Invalid("File must be between 1 byte and 10 MB.");

        var imageUploadHint = IsImageUploadHint(file);
        var videoUploadHint = IsVideoUploadHint(file);
        if (imageUploadHint && (file.Length <= 0 || file.Length > ImageFileReader.MaxBytes))
            throw InvalidImage("Ảnh phải có dung lượng từ 1 byte đến 5 MB.");
        if (videoUploadHint && (file.Length <= 0 || file.Length > maxVideoBytes))
            throw InvalidVideo("Video must be between 1 byte and the configured video limit.");
        if (file.Length <= 0 || !videoUploadHint && file.Length > MaxDocumentBytes)
        {
            if (videoUploadHint)
                throw InvalidVideo("Video must be between 1 byte and the configured video limit.");
            throw Invalid("File must be between 1 byte and 10 MB.");
        }
        var maxBytes = imageUploadHint
            ? ImageFileReader.MaxBytes
            : videoUploadHint ? maxVideoBytes : MaxDocumentBytes;
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
                if (videoUploadHint)
                    throw InvalidVideo("Video exceeds the configured video limit.");
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
        if (videoUploadHint)
        {
            if (!VideoContentTypes.TryGetValue(extension, out var expectedVideoContentType) ||
                !string.Equals(file.ContentType?.Trim(), expectedVideoContentType, StringComparison.OrdinalIgnoreCase))
                throw InvalidVideo("Supported videos must have a matching file extension and content type.");

            var videoFormat = extension[1..].ToLowerInvariant();
            if (!IsValidVideo(bytes, videoFormat))
                throw InvalidVideo("File content does not match a supported video container.");

            return new(bytes, videoFormat, "video", fileName);
        }

        if (!DocumentContentTypes.TryGetValue(extension, out var expectedContentType) ||
            !string.Equals(file.ContentType?.Trim(), expectedContentType, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Supported documents must have a matching file extension and content type.");

        var documentFormat = extension[1..].ToLowerInvariant();
        if (!IsValidDocument(bytes, documentFormat))
            throw Invalid("File content does not match a supported document format.");

        return new(bytes, documentFormat, "raw", fileName);
    }

    private static bool IsValidVideo(byte[] bytes, string format)
        => format == "webm" ? IsWebm(bytes) : IsIsoBaseMediaVideo(bytes, format);

    private static bool IsIsoBaseMediaVideo(byte[] bytes, string format)
    {
        if (bytes.Length < 16 || !bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            return false;

        var boxLength = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (boxLength < 16 || boxLength > bytes.Length || (boxLength - 16) % 4 != 0)
            return false;

        var brands = new List<string> { Encoding.ASCII.GetString(bytes, 8, 4) };
        for (var offset = 16; offset < boxLength; offset += 4)
            brands.Add(Encoding.ASCII.GetString(bytes, (int)offset, 4));

        return format switch
        {
            "mov" => brands.Contains("qt  ", StringComparer.Ordinal),
            "mp4" => brands.Any(Mp4Brands.Contains),
            _ => false
        };
    }

    private static bool IsWebm(byte[] bytes)
    {
        if (bytes.Length < 8 ||
            !bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
            return false;

        var headerSizeLength = ReadVint(bytes, 4, isIdentifier: false, out var headerSize);
        if (headerSizeLength == 0 || headerSize > MaxWebmHeaderBytes)
            return false;

        var offset = 4 + headerSizeLength;
        var end = offset + (int)headerSize;
        if (end > bytes.Length)
            return false;

        while (offset < end)
        {
            var idLength = ReadVint(bytes, offset, isIdentifier: true, out var id);
            if (idLength == 0)
                return false;
            offset += idLength;

            var sizeLength = ReadVint(bytes, offset, isIdentifier: false, out var elementSize);
            if (sizeLength == 0)
                return false;
            offset += sizeLength;
            if (elementSize > (ulong)(end - offset))
                return false;

            if (id == 0x4282)
                return elementSize == 4 && bytes.AsSpan(offset, 4).SequenceEqual("webm"u8);

            offset += (int)elementSize;
        }

        return false;
    }

    private static int ReadVint(byte[] bytes, int offset, bool isIdentifier, out ulong value)
    {
        value = 0;
        if (offset >= bytes.Length || bytes[offset] == 0)
            return 0;

        var first = bytes[offset];
        var mask = 0x80;
        var length = 1;
        while ((first & mask) == 0 && length <= 8)
        {
            mask >>= 1;
            length++;
        }
        if (length > 8 || offset + length > bytes.Length)
            return 0;

        value = (uint)(isIdentifier ? first : first & (mask - 1));
        for (var index = 1; index < length; index++)
            value = (value << 8) | bytes[offset + index];
        return length;
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

    private static bool IsVideoUploadHint(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        var contentType = file.ContentType?.Trim();
        return VideoContentTypes.ContainsKey(extension) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static AuthException Invalid(string message)
        => new("INVALID_UPLOAD", message, 400);

    private static AuthException InvalidImage(string message)
        => new("INVALID_IMAGE", message, 400);

    private static AuthException InvalidVideo(string message)
        => new("INVALID_VIDEO", message, 400);
}
