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
        if (!IsWebUrl(ReturnUrl) || !IsWebUrl(CancelUrl) || (requireLinkDuration && (LinkExpirationMinutes is < 1 or > 1440)))
            throw new TransactionException("PAYMENT_NOT_CONFIGURED",
                "Configure payment return/cancel URLs and link expiration before creating checkout.", 503);
    }

    public static bool IsWebUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        string.IsNullOrEmpty(uri.UserInfo);
}
