using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniNet.Application;
using UniNet.Application.Interfaces;

namespace UniNet.API.Services;

public sealed class FcmChatPushSender(IHttpClientFactory clients, IConfiguration config, ILogger<FcmChatPushSender> logger) : IChatPushSender
{
    private static readonly JsonSerializerOptions FcmJson = new(JsonSerializerDefaults.Web) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? accessToken;
    private DateTimeOffset expiresAt;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Firebase:ServiceAccountJson"]) ||
        config["Firebase:ServiceAccountPath"] is { Length: > 0 } path && File.Exists(path);
    private async Task<JsonDocument> Credentials(CancellationToken ct)
    {
        var raw = config["Firebase:ServiceAccountJson"];
        if (string.IsNullOrWhiteSpace(raw)) raw = await File.ReadAllTextAsync(config["Firebase:ServiceAccountPath"]!, ct);
        return JsonDocument.Parse(raw);
    }
    private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private async Task<string> Token(JsonElement credentials, CancellationToken ct)
    {
        await tokenLock.WaitAsync(ct);
        try {
            if (accessToken != null && expiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return accessToken;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var claim = Base64(JsonSerializer.SerializeToUtf8Bytes(new { iss = credentials.GetProperty("client_email").GetString(), scope = "https://www.googleapis.com/auth/firebase.messaging", aud = "https://oauth2.googleapis.com/token", iat = now, exp = now + 3600 }));
            using var rsa = RSA.Create(); rsa.ImportFromPem(credentials.GetProperty("private_key").GetString());
            var signed = header + "." + claim; var jwt = signed + "." + Base64(rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            using var response = await clients.CreateClient("ChatPush").PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = jwt }), ct);
            response.EnsureSuccessStatusCode(); using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            accessToken = result.RootElement.GetProperty("access_token").GetString()!; expiresAt = DateTimeOffset.UtcNow.AddSeconds(result.RootElement.GetProperty("expires_in").GetInt32()); return accessToken;
        } finally { tokenLock.Release(); }
    }
    public static object Payload(string token, string platform, bool showPreview, ChatPushPayload data)
    {
        static string Bounded(string value, int maxBytes) { var result = new StringBuilder(); var used = 0; foreach (var rune in value.EnumerateRunes()) { if (used + rune.Utf8SequenceLength > maxBytes) break; result.Append(rune.ToString()); used += rune.Utf8SequenceLength; } return result.ToString(); }
        static string? Image(string? value) => value is { Length: <= 400 } ? value : null;
        var sender = Bounded(data.SenderName, 160); var title = Bounded(data.ConversationName, 160);
        var avatar = Image(data.ConversationAvatarUrl);
        var preview = showPreview ? Bounded(data.MessagePreview, 400) : "Bạn có tin nhắn mới trên UniNet.";
        var fields = new Dictionary<string, string> {
            ["type"] = data.Type, ["conversationId"] = data.ConversationId.ToString(), ["messageId"] = data.MessageId.ToString(),
            ["conversationType"] = data.ConversationType.ToString(), ["senderAccountId"] = data.SenderAccountId.ToString(),
            ["senderName"] = sender, ["senderAvatarUrl"] = Image(data.SenderAvatarUrl) ?? "", ["conversationName"] = title,
            ["conversationAvatarUrl"] = avatar ?? "", ["messagePreview"] = preview, ["deepLink"] = data.DeepLink };
        var body = data.ConversationType == 1 && showPreview ? $"{sender}: {preview}" : preview;
        // Web is data-only so the service worker is the sole system-notification presenter.
        if (platform == "Web") return new { message = new { token, data = fields, webpush = new { headers = new Dictionary<string, string> { ["TTL"] = "3600", ["Urgency"] = "high" } } } };
        return new { message = new { token, data = fields, notification = new { title, body },
            android = new { priority = "high", ttl = "3600s", notification = new { icon = "ic_uninet_notification", color = "#1769E0", channel_id = "uninet-messages", tag = data.MessageId.ToString(), sound = "default", image = avatar } },
            apns = new { headers = new Dictionary<string, string> { ["apns-priority"] = "10", ["apns-push-type"] = "alert", ["apns-collapse-id"] = data.MessageId.ToString() }, payload = new { aps = new { sound = "default", category = "CHAT_MESSAGE" } } } } };
    }
    public async Task<ChatPushResult> Send(string token, string platform, bool showPreview, ChatPushPayload payload, CancellationToken ct)
    {
        if (!IsConfigured) return new(false);
        try {
            using var credentials = await Credentials(ct); var project = credentials.RootElement.GetProperty("project_id").GetString();
            if (project == null || !System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-zA-Z0-9-]+$")) return new(false, Retryable: false);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{project}/messages:send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token(credentials.RootElement, ct));
            request.Content = JsonContent.Create(Payload(token, platform, showPreview, payload), options: FcmJson);
            using var response = await clients.CreateClient("ChatPush").SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return new(true);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var invalid = error.RootElement.TryGetProperty("error", out var details) && details.TryGetProperty("details", out var list) && list.ValueKind == JsonValueKind.Array &&
                list.EnumerateArray().Any(item => item.TryGetProperty("errorCode", out var code) && code.GetString() == "UNREGISTERED");
            if (response.StatusCode == HttpStatusCode.Unauthorized) { accessToken = null; expiresAt = default; }
            logger.LogWarning("Chat FCM delivery returned status {Status}; token and payload omitted.", (int)response.StatusCode);
            return new(false, invalid, !invalid && (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.Unauthorized));
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { logger.LogWarning("Chat FCM delivery failed; credentials/token/payload omitted. Retry scheduled."); return new(false); }
    }
}
