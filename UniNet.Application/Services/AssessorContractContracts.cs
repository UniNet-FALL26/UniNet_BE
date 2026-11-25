using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using UniNet.Domain;

namespace UniNet.Application.Services;

public sealed class AssessorContractOptions
{
    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = "";
    public string StoragePath { get; set; } = "";
    public string TemplatePath { get; set; } = "";
    public string TemplateVersion { get; set; } = "";
    public string FontPath { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string CompanyTaxCode { get; set; } = "";
    public string CompanyAddress { get; set; } = "";
    public string RepresentativeName { get; set; } = "";
    public string RepresentativePosition { get; set; } = "";
    public string CompanyEmail { get; set; } = "";
    public string CompanyPhone { get; set; } = "";
    public string MinimumWithdrawalAmount { get; set; } = "";
    public string PaymentProcessingDays { get; set; } = "";
    public string SignatureStatement { get; set; } = "";
}

public sealed record ContractCredential([Required, StringLength(43, MinimumLength = 43)] string Token);
public sealed record SignAssessorContract(
    [Required, StringLength(43, MinimumLength = 43)] string Token,
    [Required, MaxLength(255)] string SignedName,
    [Required, StringLength(64, MinimumLength = 64)] string ContractSha256,
    bool Accepted);
public sealed record ContractProfile(string CitizenId, string BankName, string BankAccountNumber, string BankAccountHolder);
public sealed record StoredAssessorContract(string Key, string Sha256, string? Text = null, byte[]? OriginalPdf = null);
public sealed record AssessorEmailPayload(string Recipient, string Subject, string Body);

public interface IAssessorContractFiles
{
    Task<StoredAssessorContract> Generate(AssessorOnboarding row, ContractProfile profile, CancellationToken ct);
    Task<byte[]> Read(string key, string sha256, CancellationToken ct);
    Task<byte[]> Export(AssessorOnboarding row, CancellationToken ct);
}

public interface IAssessorOnboardingMailSender
{
    Task Send(AssessorEmailPayload email, string messageKey, CancellationToken ct);
}

public sealed class AssessorContractSecrets(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("UniNet.AssessorOnboarding.Credentials.v1");
    public string Protect(string value) => protector.Protect(value);
    public string Unprotect(string value) => protector.Unprotect(value);
    public static string Token() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

}
