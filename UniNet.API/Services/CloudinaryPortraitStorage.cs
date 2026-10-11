using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniNet.Application;

namespace UniNet.API.Services;

public interface IPortraitCloudStorage
{
    Task<string> Upload(byte[] png, string publicId, CancellationToken ct);
    Task Destroy(string publicId, CancellationToken ct);
}

public sealed class CloudinaryPortraitStorage(HttpClient http, IConfiguration configuration) : IPortraitCloudStorage
{
    private string Config(string name) => configuration[$"Cloudinary:{name}"] is { Length: > 0 } value ? value
        : throw new AuthException("PORTRAIT_STORAGE_UNAVAILABLE", "Chưa cấu hình lưu ảnh Cloudinary. Vui lòng thử lại sau.", 503);
    private async Task<JsonDocument> Send(string action, SortedDictionary<string, string> parameters, byte[]? bytes, CancellationToken ct)
    {
        var cloud = Config("CloudName"); var key = Config("ApiKey"); var secret = Config("ApiSecret");
        if (!System.Text.RegularExpressions.Regex.IsMatch(cloud, "^[a-zA-Z0-9_-]+$")) throw new AuthException("PORTRAIT_STORAGE_UNAVAILABLE", "Cấu hình lưu ảnh chưa hợp lệ.", 503);
        parameters["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var canonical = string.Join("&", parameters.Select(pair => $"{pair.Key}={pair.Value}"));
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + secret))).ToLowerInvariant();
        using var form = new MultipartFormDataContent();
        foreach (var pair in parameters) form.Add(new StringContent(pair.Value), pair.Key);
        form.Add(new StringContent(key), "api_key"); form.Add(new StringContent(signature), "signature");
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes); file.Headers.ContentType = new("image/png"); form.Add(file, "file", "portrait.png");
        }
        HttpResponseMessage remote;
        try { remote = await http.PostAsync($"https://api.cloudinary.com/v1_1/{cloud}/image/{action}", form, ct); }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new AuthException("PORTRAIT_UPLOAD_FAILED", "Không kết nối được dịch vụ lưu ảnh. Vui lòng thử lại.", 502); }
        using var response = remote;
        if (!response.IsSuccessStatusCode) throw new AuthException("PORTRAIT_UPLOAD_FAILED", "Không lưu được ảnh. Vui lòng thử lại.", 502);
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new AuthException("PORTRAIT_UPLOAD_FAILED", "Phản hồi lưu ảnh không hợp lệ.", 502); }
    }
    public async Task<string> Upload(byte[] png, string publicId, CancellationToken ct)
    {
        using var result = await Send("upload", new() { ["asset_folder"] = "uninet/portfolio/portraits", ["public_id"] = publicId, ["overwrite"] = "false", ["format"] = "png" }, png, ct);
        var root = result.RootElement;
        if (!root.TryGetProperty("public_id", out var id) || id.GetString() != publicId || !root.TryGetProperty("format", out var format) || format.GetString() != "png"
            || !root.TryGetProperty("secure_url", out var value) || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.Host != "res.cloudinary.com" || !url.AbsolutePath.StartsWith($"/{Config("CloudName")}/image/upload/", StringComparison.Ordinal))
            throw new AuthException("PORTRAIT_UPLOAD_FAILED", "Phản hồi lưu ảnh không hợp lệ.", 502);
        return url.AbsoluteUri;
    }
    public async Task Destroy(string publicId, CancellationToken ct)
    {
        using var response = await Send("destroy", new() { ["public_id"] = publicId, ["invalidate"] = "true" }, null, ct);
        if (!response.RootElement.TryGetProperty("result", out var result) || result.GetString() is not ("ok" or "not found"))
            throw new AuthException("PORTRAIT_DELETE_FAILED", "Không thể dọn ảnh chưa sử dụng.", 502);
    }
}
