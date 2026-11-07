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
    public string SignatureStatement { get; set; } = "";
}

public sealed record ContractCredential([Required, StringLength(43, MinimumLength = 43)] string Token);
public sealed record VerifyContractOtp(
    [Required, StringLength(43, MinimumLength = 43)] string Token,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);
public sealed record SignAssessorContract(
    [Required, StringLength(43, MinimumLength = 43)] string Token,
    [Required, RegularExpression("^[0-9]{6}$")] string Code,
    [Required, MaxLength(255)] string SignedName,
    [Required, StringLength(64, MinimumLength = 64)] string ContractSha256,
    bool Accepted);
public sealed record ContractProfile(string CitizenId, string TaxCode, string BankName, string BankAccountNumber, string BankAccountHolder);
public sealed record StoredAssessorContract(string Key, string Sha256);
public sealed record AssessorEmailPayload(string Recipient, string Subject, string Body);

public interface IAssessorContractFiles
{
    Task<StoredAssessorContract> Generate(AssessorOnboarding row, ContractProfile profile, CancellationToken ct);
    Task<byte[]> Read(string key, string sha256, CancellationToken ct);
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

    // An encrypted random salt prevents offline enumeration of the six-digit code from a database dump.
    public string OtpHash(AssessorOnboarding row, string code, string salt)
        => Hash($"{row.Id:N}|{row.ContractSha256}|{row.OtpPurpose}|{salt}|{code}");

    public string CreateOtp(AssessorOnboarding row, string code)
    {
        var salt = Token();
        return Protect($"{salt}:{OtpHash(row, code, salt)}");
    }

    public bool VerifyOtp(AssessorOnboarding row, string code)
    {
        var parts = Unprotect(row.OtpHash!).Split(':');
        return parts.Length == 2 && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(parts[1]), Encoding.ASCII.GetBytes(OtpHash(row, code, parts[0])));
    }
}
