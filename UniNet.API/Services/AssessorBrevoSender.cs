using System.Net.Http.Json;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using UniNet.Application.Services;

namespace UniNet.API.Services;

public sealed class AssessorBrevoOptions
{
    public string ApiKey { get; set; } = "";
    public string From { get; set; } = "";
}

public sealed class AssessorBrevoSender(
    HttpClient client,
    IOptions<AssessorBrevoOptions> options,
    ILogger<AssessorBrevoSender> logger) : IAssessorOnboardingMailSender
{
    public async Task Send(AssessorEmailPayload email, string messageKey, CancellationToken ct)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { email = new MailAddress(settings.From).Address, name = "UniNet" },
                to = new[] { new { email = email.Recipient } },
                subject = email.Subject,
                textContent = email.Body
            })
        };
        request.Headers.Add("api-key", settings.ApiKey);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            logger.LogWarning("Brevo rejected an assessor onboarding email with HTTP {StatusCode}.", (int)response.StatusCode);
        response.EnsureSuccessStatusCode();
    }
}
