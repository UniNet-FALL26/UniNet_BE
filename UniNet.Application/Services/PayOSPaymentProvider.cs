using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UniNet.Application.Interfaces;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

// Merchant API and HMAC rules: https://payos.vn/docs/api/ and
// https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/
public sealed class PayOSPaymentProvider(HttpClient http, IConfiguration configuration) : IPaymentProvider
{
    public string Name => "PayOS";
    private string? Setting(string key, string environmentName) =>
        configuration[$"PayOS:{key}"] ?? configuration[environmentName];
    private string ChecksumKey => Setting("ChecksumKey", "PAYOS_CHECKSUM_KEY")
        ?? throw NotConfigured();

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(Setting("ClientId", "PAYOS_CLIENT_ID")) ||
            string.IsNullOrWhiteSpace(Setting("ApiKey", "PAYOS_API_KEY")) ||
            string.IsNullOrWhiteSpace(Setting("ChecksumKey", "PAYOS_CHECKSUM_KEY")))
            throw NotConfigured();
    }

    public async Task<ProviderPaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
    {
        EnsureConfigured();
        var orderCode = long.Parse(request.OrderCode, CultureInfo.InvariantCulture);
        var signedData = JsonSerializer.SerializeToElement(new
        {
            amount = request.AmountVnd, cancelUrl = request.CancelUrl, description = request.Description,
            orderCode, returnUrl = request.ReturnUrl
        });
        using var message = NewRequest(HttpMethod.Post, "v2/payment-requests");
        message.Content = JsonContent.Create(new
        {
            orderCode, amount = request.AmountVnd, description = request.Description,
            returnUrl = request.ReturnUrl, cancelUrl = request.CancelUrl,
            expiredAt = request.ExpiresAt.ToUnixTimeSeconds(), signature = Sign(signedData)
        });
        var data = await SendAsync(message, creating: true, ct);
        return ReadResult(data, creating: true);
    }

    public async Task<ProviderPaymentResult> GetPaymentStatusAsync(string orderCode, CancellationToken ct)
    {
        EnsureConfigured();
        using var message = NewRequest(HttpMethod.Get, $"v2/payment-requests/{Uri.EscapeDataString(orderCode)}");
        var data = await SendAsync(message, creating: false, ct);
        return ReadResult(data, creating: false);
    }

    public ProviderPaymentResult VerifyWebhook(JsonElement payload)
    {
        EnsureConfigured();
        try
        {
            var data = payload.GetProperty("data");
            if (!VerifySignature(data, payload.GetProperty("signature").GetString()))
                throw new TransactionException("INVALID_WEBHOOK_SIGNATURE", "Invalid PayOS webhook signature.");
            // Only signed data determines the event outcome; the outer success/code fields are unsigned.
            var status = String(data, "code") == "00" ? PaymentStatus.Success : PaymentStatus.Pending;
            return new(Number(data, "orderCode").ToString(CultureInfo.InvariantCulture),
                Number(data, "amount"), RequiredString(data, "currency"),
                RequiredString(data, "paymentLinkId"), status,
                Reference: RequiredString(data, "reference"));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or ArgumentException or OverflowException)
        {
            throw new TransactionException("INVALID_WEBHOOK", "Invalid PayOS webhook payload.");
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var message = new HttpRequestMessage(method, $"https://api-merchant.payos.vn/{path}");
        message.Headers.Add("x-client-id", Setting("ClientId", "PAYOS_CLIENT_ID"));
        message.Headers.Add("x-api-key", Setting("ApiKey", "PAYOS_API_KEY"));
        return message;
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage message, bool creating, CancellationToken ct)
    {
        try
        {
            using var response = await http.SendAsync(message, ct);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;
            if (!response.IsSuccessStatusCode || String(root, "code") != "00")
            {
                // Only explicit authentication/validation rejection is safe to retry as a new attempt.
                // Duplicated order, 404, 429, 5xx and undocumented error codes remain unresolved.
                var rejected = creating && response.StatusCode is HttpStatusCode.BadRequest or
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.UnprocessableEntity;
                rejected &= String(root, "code") is not null and not "00" &&
                    (!root.TryGetProperty("data", out var errorData) || errorData.ValueKind == JsonValueKind.Null);
                throw new PaymentProviderException(rejected ? "PROVIDER_REJECTED" : "PROVIDER_STATUS_UNKNOWN",
                    rejected ? "PayOS rejected payment-link creation." : "PayOS payment state could not be confirmed.", rejected);
            }
            var data = root.GetProperty("data");
            // Creation responses are signed. Status GET is authenticated over TLS; check a signature if supplied.
            if (creating || root.TryGetProperty("signature", out _))
                if (!VerifySignature(data, root.GetProperty("signature").GetString()))
                    throw new PaymentProviderException("INVALID_PROVIDER_SIGNATURE", "PayOS response signature is invalid.");
            return data.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new PaymentProviderException("PROVIDER_STATUS_UNKNOWN", "PayOS is unavailable; reconcile this attempt before retrying.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or ArgumentException or OverflowException)
        {
            throw new PaymentProviderException("INVALID_PROVIDER_RESPONSE", "PayOS returned an unconfirmed payment response.");
        }
    }

    private static ProviderPaymentResult ReadResult(JsonElement data, bool creating)
    {
        try
        {
            var status = String(data, "status") switch
            {
                "PAID" => PaymentStatus.Success,
                "CANCELLED" or "EXPIRED" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
            };
            var url = String(data, "checkoutUrl");
            if (creating && (!PaymentOptions.IsWebUrl(url) || !url!.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                throw new PaymentProviderException("INVALID_PROVIDER_RESPONSE", "PayOS returned an invalid checkout URL.");
            return new(Number(data, "orderCode").ToString(CultureInfo.InvariantCulture), Number(data, "amount"),
                String(data, "currency") ?? "VND", RequiredString(data, creating ? "paymentLinkId" : "id"),
                status, url, creating ? null : Number(data, "amountPaid"), QrCode: String(data, "qrCode"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new PaymentProviderException("INVALID_PROVIDER_RESPONSE", "PayOS returned an unconfirmed payment response.");
        }
    }

    private string Sign(JsonElement data) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(ChecksumKey), Encoding.UTF8.GetBytes(Canonicalize(data)))).ToLowerInvariant();

    private bool VerifySignature(JsonElement data, string? signature)
    {
        if (signature?.Length != 64) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Sign(data)), Convert.FromHexString(signature));
        }
        catch (FormatException) { return false; }
    }

    private static string Canonicalize(JsonElement data)
    {
        // Reject duplicate property names to avoid ambiguous signed data versus parsed values.
        var properties = data.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
        return string.Join("&", properties.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={SignatureValue(p.Value)}"));
    }

    private static string SignatureValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.String => value.GetString() is null or "null" or "undefined" ? "" : value.GetString()!,
        JsonValueKind.Array or JsonValueKind.Object => SortedJson(value),
        _ => value.GetRawText()
    };

    private static string SortedJson(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            WriteSorted(writer, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSorted(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteSorted(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteSorted(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    private static string? String(JsonElement data, string key) => data.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string RequiredString(JsonElement data, string key) =>
        !string.IsNullOrWhiteSpace(String(data, key)) ? String(data, key)! : throw new FormatException();
    private static long Number(JsonElement data, string key) => data.GetProperty(key).GetInt64();
    private static TransactionException NotConfigured() => new("PAYMENT_NOT_CONFIGURED",
        "PayOS client ID, API key and checksum key must be configured on the server.", 503);
}
