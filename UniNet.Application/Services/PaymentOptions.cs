using Microsoft.Extensions.Configuration;

namespace UniNet.Application.Services;

public sealed class PaymentOptions
{
    public string? ReturnUrl { get; set; }
    public string? CancelUrl { get; set; }
    public int LinkExpirationMinutes { get; set; } = 30;
    public bool AllowUserReconciliation { get; set; } = true;

    public static PaymentOptions Load(IConfiguration configuration)
    {
        try { return configuration.GetSection("Payments").Get<PaymentOptions>() ?? new(); }
        catch (InvalidOperationException)
        {
            throw new TransactionException("PAYMENT_NOT_CONFIGURED", "Payment settings are invalid.", 503);
        }
    }

    public void RequireCheckoutConfiguration(bool requireLinkDuration = true)
    {
        var invalid = new List<string>();
        if (!IsWebUrl(ReturnUrl)) invalid.Add("Payments:ReturnUrl (Payments__ReturnUrl)");
        if (!IsWebUrl(CancelUrl)) invalid.Add("Payments:CancelUrl (Payments__CancelUrl)");
        if (requireLinkDuration && LinkExpirationMinutes is < 1 or > 1440)
            invalid.Add("Payments:LinkExpirationMinutes (1-1440)");
        if (invalid.Count > 0)
            throw new TransactionException("PAYMENT_NOT_CONFIGURED",
                "Missing or invalid payment configuration: " + string.Join(", ", invalid) + ".", 503);
    }

    public static bool IsWebUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        string.IsNullOrEmpty(uri.UserInfo);
}
