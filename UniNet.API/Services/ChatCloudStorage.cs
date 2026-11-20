using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Services;

public sealed class ChatCloudStorage(HttpClient http, IConfiguration config) : IChatAttachmentStorage
{
    private string Setting(string key) => config["Cloudinary:" + key] is { Length: > 0 } value ? value : throw new AuthException("CHAT_STORAGE_UNAVAILABLE", "Chưa cấu hình lưu trữ file trò chuyện.", 503);
    public static string Mime(string format) => format switch {
        "jpg" => "image/jpeg", "png" => "image/png", "webp" => "image/webp", "mp4" => "video/mp4", "mov" => "video/quicktime", "webm" => "video/webm",
        "pdf" => "application/pdf", "txt" => "text/plain", "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => throw new AuthException("CHAT_FILE_TYPE", "Định dạng file không được hỗ trợ.") };
    private string Cloud()
    {
        var cloud = Setting("CloudName");
        if (!Regex.IsMatch(cloud, "^[a-zA-Z0-9_-]+$")) throw new AuthException("CHAT_STORAGE_UNAVAILABLE", "Cấu hình Cloudinary không hợp lệ.", 503);
        return cloud;
    }
    private async Task<JsonDocument> Send(string action, string key, byte[]? bytes, string? mime, CancellationToken ct)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["public_id"] = key,
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ["type"] = "authenticated" };
        if (bytes != null) parameters["overwrite"] = "false";
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("&", parameters.Select(p => p.Key + "=" + p.Value)) + Setting("ApiSecret")))).ToLowerInvariant();
        using var form = new MultipartFormDataContent(); foreach (var item in parameters) form.Add(new StringContent(item.Value), item.Key);
        form.Add(new StringContent(Setting("ApiKey")), "api_key"); form.Add(new StringContent(signature), "signature");
        if (bytes != null) { var content = new ByteArrayContent(bytes); content.Headers.ContentType = new(mime!); form.Add(content, "file", "attachment"); }
        using var response = await http.PostAsync($"https://api.cloudinary.com/v1_1/{Cloud()}/raw/{action}", form, ct);
        if (!response.IsSuccessStatusCode) throw new AuthException("CHAT_STORAGE_FAILED", "Không lưu được file trò chuyện. Vui lòng thử lại.", 502);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public async Task<ChatUploadedFile> Upload(Guid actor, byte[] bytes, string format, string resourceType, string fileName, CancellationToken ct)
    {
        var id = Guid.NewGuid(); var key = $"uninet/chat/{actor:N}/{id:N}.{format}"; var mime = Mime(format);
        using var result = await Send("upload", key, bytes, mime, ct);
        var root = result.RootElement;
        if (root.GetProperty("public_id").GetString() != key || root.GetProperty("resource_type").GetString() != "raw" || root.GetProperty("type").GetString() != "authenticated")
            throw new AuthException("CHAT_STORAGE_INVALID", "Phản hồi lưu trữ không hợp lệ.", 502);
        var name = Path.GetFileName(fileName.Replace('\\', '/')); if (name.Length > 255) name = name[^255..];
        return new(id, (short)(resourceType == "image" ? 0 : resourceType == "video" ? 1 : 2), name, bytes.LongLength, mime, key);
    }
    public async Task<Stream> Open(string key, CancellationToken ct)
    {
        if (!Regex.IsMatch(key, "^uninet/chat/[a-f0-9]{32}/[a-f0-9]{32}\\.[a-z0-9]+$")) throw new AuthException("CHAT_FILE_NOT_FOUND", "Không tìm thấy file.", 404);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key + Setting("ApiSecret")));
        var signature = Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_')[..8];
        using var response = await http.GetAsync($"https://res.cloudinary.com/{Cloud()}/raw/authenticated/s--{signature}--/{key}", HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new AuthException("CHAT_FILE_UNAVAILABLE", "Không tải được file trò chuyện.", 502);
        if (response.Content.Headers.ContentLength > UploadFileReader.MaxVideoBytes) throw new AuthException("CHAT_FILE_TOO_LARGE", "File vượt quá kích thước cho phép.");
        await using var input = await response.Content.ReadAsStreamAsync(ct); var output = new MemoryStream(); var buffer = new byte[65536];
        while (true) { var count = await input.ReadAsync(buffer, ct); if (count == 0) break; if (output.Length + count > UploadFileReader.MaxVideoBytes) { output.Dispose(); throw new AuthException("CHAT_FILE_TOO_LARGE", "File vượt quá kích thước cho phép."); } await output.WriteAsync(buffer.AsMemory(0, count), ct); }
        output.Position = 0; return output;
    }
    public async Task Delete(string key, CancellationToken ct) { using var response = await Send("destroy", key, null, null, ct); }
}
