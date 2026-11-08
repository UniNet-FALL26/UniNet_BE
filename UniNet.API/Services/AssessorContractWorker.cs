using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using UniNet.Application.Services;

namespace UniNet.API.Services;

public sealed class AssessorSmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string From { get; set; } = "";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? PickupDirectory { get; set; }
}

public sealed class AssessorSmtpSender(IOptions<AssessorSmtpOptions> options, IWebHostEnvironment environment)
    : IAssessorOnboardingMailSender
{
    public async Task Send(AssessorEmailPayload email, string messageKey, CancellationToken ct)
    {
        var settings = options.Value;
        using var message = new MailMessage(settings.From, email.Recipient, email.Subject, email.Body)
        {
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false
        };
        var messageId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(messageKey)));
        message.Headers.Add("Message-ID", $"<{messageId}@uninet.onboarding>");
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Timeout = 30000
        };
        if (!string.IsNullOrWhiteSpace(settings.PickupDirectory))
        {
            if (!environment.IsDevelopment())
                throw new InvalidOperationException("Email pickup is only available in Development.");
            Directory.CreateDirectory(settings.PickupDirectory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = Path.GetFullPath(settings.PickupDirectory);
            client.EnableSsl = false;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Username))
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await client.SendMailAsync(message, timeout.Token);
    }
}

public sealed class AssessorContractWorker(IServiceScopeFactory scopes, ILogger<AssessorContractWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var tasks = scope.ServiceProvider.GetRequiredService<AssessorContractTasks>();
                await tasks.GenerateNext(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Assessor PDF generation failed; pending work will be retried."); }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AssessorContractTasks>().SendNext(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Assessor email processing failed; persisted work will be retried."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
