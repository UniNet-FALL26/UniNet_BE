using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Services;

public sealed class CloudinaryImageStorage(
    HttpClient http,
    IConfiguration configuration) : ICloudImageStorage
{
    private string Config(string name) => configuration[$"Cloudinary:{name}"] is { Length: > 0 } value ? value
        : throw new AuthException("IMAGE_STORAGE_UNAVAILABLE", "Chưa cấu hình lưu ảnh Cloudinary. Vui lòng thử lại sau.", 503);

    private static string ContentType(string format, string resourceType) => (resourceType, format) switch {
        ("image", "png") => "image/png",
        ("image", "jpg") => "image/jpeg",
        ("image", "webp") => "image/webp",
        ("raw", "pdf") => "application/pdf",
        ("raw", "docx") => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ("raw", "xlsx") => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ("raw", "pptx") => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ("raw", "txt") => "text/plain",
        _ => throw new ArgumentException("Unsupported Cloudinary upload format.", nameof(format))
    };

    private async Task<JsonDocument> Send(
        string action,
        string resourceType,
        SortedDictionary<string, string> parameters,
        byte[]? bytes,
        string? format,
        CancellationToken ct)
    {
        var cloud = Config("CloudName"); var key = Config("ApiKey"); var secret = Config("ApiSecret");
        if (!Regex.IsMatch(cloud, "^[a-zA-Z0-9_-]+$")) throw new AuthException("IMAGE_STORAGE_UNAVAILABLE", "Cấu hình lưu ảnh chưa hợp lệ.", 503);
        parameters["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var canonical = string.Join("&", parameters.Select(pair => $"{pair.Key}={pair.Value}"));
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + secret))).ToLowerInvariant();
        using var form = new MultipartFormDataContent();
        foreach (var pair in parameters) form.Add(new StringContent(pair.Value), pair.Key);
        form.Add(new StringContent(key), "api_key"); form.Add(new StringContent(signature), "signature");
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes); file.Headers.ContentType = new(ContentType(format!, resourceType));
            form.Add(file, "file", $"upload.{format}");
        }
        HttpResponseMessage remote;
        var code = action == "destroy"
            ? "IMAGE_DELETE_FAILED"
            : resourceType == "image" ? "IMAGE_UPLOAD_FAILED" : "FILE_UPLOAD_FAILED";
        try { remote = await http.PostAsync($"https://api.cloudinary.com/v1_1/{cloud}/{resourceType}/{action}", form, ct); }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            var message = resourceType == "image"
                ? "Không kết nối được dịch vụ lưu ảnh. Vui lòng thử lại."
                : "Không kết nối được dịch vụ lưu file. Vui lòng thử lại.";
            throw new AuthException(code, message, 502);
        }
        using var response = remote;
        if (!response.IsSuccessStatusCode)
        {
            var message = resourceType == "image"
                ? "Không thực hiện được yêu cầu lưu ảnh. Vui lòng thử lại."
                : "Không thực hiện được yêu cầu lưu file. Vui lòng thử lại.";
            throw new AuthException(code, message, 502);
        }
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new AuthException(code, "Phản hồi lưu ảnh không hợp lệ.", 502); }
    }

    public async Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct)
    {
        if (request.ResourceType is not ("image" or "raw"))
            throw new ArgumentException("Unsupported Cloudinary resource type.", nameof(request));
        _ = ContentType(request.Format, request.ResourceType);
        if (request.ResourceType == "raw" &&
            !request.PublicId.EndsWith($".{request.Format}", StringComparison.Ordinal))
            throw new ArgumentException("Raw Cloudinary public IDs must include their file extension.", nameof(request));
        var separator = request.PublicId.LastIndexOf('/');
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal) {
            ["public_id"] = request.PublicId, ["overwrite"] = "false"
        };
        if (request.ResourceType == "image")
            parameters["format"] = request.Format;
        if (separator > 0) parameters["asset_folder"] = request.PublicId[..separator];
        using var result = await Send("upload", request.ResourceType, parameters, request.Bytes, request.Format, ct);
        var root = result.RootElement;
        var expectedSuffix = request.ResourceType == "image"
            ? $"/{request.PublicId}.{request.Format}"
            : $"/{request.PublicId}";
        var publicIdMatches = String(root, "public_id") == request.PublicId;
        var responseFormat = String(root, "format");
        var hasResponseFormat = root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("format", out _);
        var formatMatches =
            (request.ResourceType == "raw" && !hasResponseFormat) ||
            responseFormat == request.Format;
        var resourceTypeMatches = String(root, "resource_type") == request.ResourceType;
        var hasAbsoluteSecureUrl = Uri.TryCreate(
            String(root, "secure_url"), UriKind.Absolute, out var url);
        var isHttps = hasAbsoluteSecureUrl && url!.Scheme == Uri.UriSchemeHttps;
        var trustedHost = hasAbsoluteSecureUrl &&
            string.Equals(url!.Host, "res.cloudinary.com", StringComparison.OrdinalIgnoreCase) &&
            url.IsDefaultPort;
        var hasNoCredentials = hasAbsoluteSecureUrl && string.IsNullOrEmpty(url!.UserInfo);
        var hasNoQueryOrFragment = hasAbsoluteSecureUrl &&
            string.IsNullOrEmpty(url!.Query) &&
            string.IsNullOrEmpty(url.Fragment);
        var deliveryPath = hasAbsoluteSecureUrl
            ? ValidateDeliveryPath(url!, Config("CloudName"), request.ResourceType, expectedSuffix)
            : default;
        var hasExpectedDeliveryPath = deliveryPath.IsValid;
        if (!publicIdMatches || !formatMatches || !resourceTypeMatches ||
            !hasAbsoluteSecureUrl || !isHttps || !trustedHost || !hasNoCredentials ||
            !hasNoQueryOrFragment || !hasExpectedDeliveryPath)
        {
            throw new AuthException(
                request.ResourceType == "image" ? "IMAGE_UPLOAD_FAILED" : "FILE_UPLOAD_FAILED",
                request.ResourceType == "image"
                    ? "Phản hồi lưu ảnh không hợp lệ."
                    : "Phản hồi lưu file không hợp lệ.",
                502);
        }
        return new(url!.AbsoluteUri, request.PublicId, request.Format, request.ResourceType);
    }

    private readonly record struct DeliveryPathValidation(
        bool HasExpectedSegmentCount,
        bool CloudNameMatches,
        bool ResourceTypeMatches,
        bool UsesUploadMode,
        bool VersionIsValid,
        bool AssetMatches)
    {
        public bool IsValid =>
            HasExpectedSegmentCount &&
            CloudNameMatches &&
            ResourceTypeMatches &&
            UsesUploadMode &&
            VersionIsValid &&
            AssetMatches;
    }

    private static DeliveryPathValidation ValidateDeliveryPath(
        Uri url, string cloudName, string resourceType, string expectedSuffix)
    {
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var enoughSegments = segments.Length >= 5;
        return new(
            enoughSegments,
            enoughSegments && string.Equals(segments[0], cloudName, StringComparison.Ordinal),
            enoughSegments && string.Equals(segments[1], resourceType, StringComparison.Ordinal),
            enoughSegments && string.Equals(segments[2], "upload", StringComparison.Ordinal),
            enoughSegments && Regex.IsMatch(segments[3], "^v[0-9]+$", RegexOptions.CultureInvariant),
            enoughSegments && string.Equals(
                "/" + string.Join('/', segments.Skip(4)), expectedSuffix, StringComparison.Ordinal));
    }

    public async Task Destroy(string publicId, CancellationToken ct)
    {
        using var response = await Send("destroy", "image", new(StringComparer.Ordinal) { ["public_id"] = publicId, ["invalidate"] = "true" }, null, null, ct);
        if (String(response.RootElement, "result") is not ("ok" or "not found"))
            throw new AuthException("IMAGE_DELETE_FAILED", "Không thể xóa ảnh. Vui lòng thử lại.", 502);
    }

    private static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
