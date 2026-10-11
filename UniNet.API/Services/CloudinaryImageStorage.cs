using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Services;

public sealed class CloudinaryImageStorage(HttpClient http, IConfiguration configuration) : ICloudImageStorage
{
    private string Config(string name) => configuration[$"Cloudinary:{name}"] is { Length: > 0 } value ? value
        : throw new AuthException("IMAGE_STORAGE_UNAVAILABLE", "Chưa cấu hình lưu ảnh Cloudinary. Vui lòng thử lại sau.", 503);

    private static string ContentType(string format) => format switch {
        "png" => "image/png", "jpg" => "image/jpeg", "webp" => "image/webp",
        _ => throw new ArgumentException("Unsupported image format.", nameof(format))
    };

    private async Task<JsonDocument> Send(string action, SortedDictionary<string, string> parameters, byte[]? bytes, string? format, CancellationToken ct)
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
            var file = new ByteArrayContent(bytes); file.Headers.ContentType = new(ContentType(format!));
            form.Add(file, "file", $"image.{format}");
        }
        HttpResponseMessage remote;
        var code = action == "destroy" ? "IMAGE_DELETE_FAILED" : "IMAGE_UPLOAD_FAILED";
        try { remote = await http.PostAsync($"https://api.cloudinary.com/v1_1/{cloud}/image/{action}", form, ct); }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new AuthException(code, "Không kết nối được dịch vụ lưu ảnh. Vui lòng thử lại.", 502); }
        using var response = remote;
        if (!response.IsSuccessStatusCode) throw new AuthException(code, "Không thực hiện được yêu cầu lưu ảnh. Vui lòng thử lại.", 502);
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new AuthException(code, "Phản hồi lưu ảnh không hợp lệ.", 502); }
    }

    public async Task<ImageUploadResult> Upload(ImageUploadRequest request, CancellationToken ct)
    {
        _ = ContentType(request.Format);
        var separator = request.PublicId.LastIndexOf('/');
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal) {
            ["public_id"] = request.PublicId, ["overwrite"] = "false", ["format"] = request.Format
        };
        if (separator > 0) parameters["asset_folder"] = request.PublicId[..separator];
        using var result = await Send("upload", parameters, request.Bytes, request.Format, ct);
        var root = result.RootElement;
        if (String(root, "public_id") != request.PublicId || String(root, "format") != request.Format
            || !Uri.TryCreate(String(root, "secure_url"), UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.Host != "res.cloudinary.com" || !url.IsDefaultPort || url.UserInfo.Length > 0
            || !url.AbsolutePath.StartsWith($"/{Config("CloudName")}/image/upload/", StringComparison.Ordinal)
            || !url.AbsolutePath.EndsWith($"/{request.PublicId}.{request.Format}", StringComparison.Ordinal)
            || url.Query.Length > 0 || url.Fragment.Length > 0)
            throw new AuthException("IMAGE_UPLOAD_FAILED", "Phản hồi lưu ảnh không hợp lệ.", 502);
        return new(url.AbsoluteUri, request.PublicId, request.Format);
    }

    public async Task Destroy(string publicId, CancellationToken ct)
    {
        using var response = await Send("destroy", new(StringComparer.Ordinal) { ["public_id"] = publicId, ["invalidate"] = "true" }, null, null, ct);
        if (String(response.RootElement, "result") is not ("ok" or "not found"))
            throw new AuthException("IMAGE_DELETE_FAILED", "Không thể xóa ảnh. Vui lòng thử lại.", 502);
    }

    private static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
