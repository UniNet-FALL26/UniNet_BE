using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using UniNet.Domain;

namespace UniNet.Application.Services;

public sealed class AssessorContractFiles(IOptions<AssessorContractOptions> options) : IAssessorContractFiles
{
    private readonly AssessorContractOptions settings = options.Value;

    public async Task<StoredAssessorContract> Generate(AssessorOnboarding row, ContractProfile profile, CancellationToken ct)
    {
        var template = await File.ReadAllTextAsync(settings.TemplatePath, ct);
        var values = new Dictionary<string, string>
        {
            ["OnboardingId"] = row.Id.ToString(),
            ["FullName"] = row.FullName!,
            ["Email"] = row.EmailNormalized,
            ["CitizenId"] = profile.CitizenId,
            ["TaxCode"] = "",
            ["IdentityNumber"] = profile.CitizenId,
            ["ContractCode"] = row.Id.ToString("N").ToUpperInvariant(),
            ["SigningDate"] = row.SubmittedAt?.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy") ?? "",
            ["CompanyName"] = settings.CompanyName,
            ["CompanyTaxCode"] = settings.CompanyTaxCode,
            ["CompanyAddress"] = settings.CompanyAddress,
            ["RepresentativeName"] = settings.RepresentativeName,
            ["RepresentativePosition"] = settings.RepresentativePosition,
            ["CompanyEmail"] = settings.CompanyEmail,
            ["CompanyPhone"] = settings.CompanyPhone,
            ["MinimumWithdrawalAmount"] = settings.MinimumWithdrawalAmount,
            ["PaymentProcessingDays"] = settings.PaymentProcessingDays,
            ["BankName"] = profile.BankName,
            ["BankAccountNumber"] = profile.BankAccountNumber,
            ["BankAccountHolder"] = profile.BankAccountHolder,
            ["TemplateVersion"] = settings.TemplateVersion,
            ["SignatureStatement"] = settings.SignatureStatement
        };
        // Replace once so profile values cannot introduce additional template substitutions.
        var text = Regex.Replace(template, @"\{\{([A-Za-z]+)\}\}", match =>
        {
            var key = match.Groups[1].Value;
            if (!values.TryGetValue(key, out var value))
                throw new InvalidOperationException("Unknown contract placeholder.");
            if (string.IsNullOrWhiteSpace(value) && key != "TaxCode")
                throw new AuthException("CONTRACT_DETAILS_NOT_CONFIGURED", "Thông tin mẫu hợp đồng chưa được cấu hình đầy đủ.", 503);
            return value;
        });
        var bytes = Render(text, await File.ReadAllBytesAsync(settings.FontPath, ct));
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var key = $"{row.Id:N}/{sha}.pdf";
        return new(key, sha, text.Normalize(NormalizationForm.FormC), bytes);
    }

    public async Task<byte[]> Read(string key, string sha256, CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(Resolve(key), ct);
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new AuthException("CONTRACT_FILE_MISSING",
                "Máy chủ này không có tệp hợp đồng gốc. Vui lòng kiểm tra nơi hợp đồng được tạo và kho lưu trữ.", 404);
        }
        VerifyHash(bytes, sha256);
        return bytes;
    }

    internal static void VerifyHash(byte[] bytes, string sha256)
    {
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Contract integrity check failed.");
    }

    public async Task<byte[]> Export(AssessorOnboarding row, CancellationToken ct)
    {
        var bytes = row.ContractOriginalPdf ?? await Read(row.ContractStorageKey!, row.ContractSha256!, ct);
        VerifyHash(bytes, row.ContractSha256!);
        if (row.SignedAt is not { } signedAt) return bytes;

        byte[] fontBytes;
        try { fontBytes = await File.ReadAllBytesAsync(settings.FontPath, ct); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new AuthException("CONTRACT_FONT_UNAVAILABLE",
                "Máy chủ chưa có phông chữ để xuất trang xác nhận ký hợp đồng.", 503);
        }

        var signedTime = signedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm:ss");
        var receiptText = $"""
            XÁC NHẬN KÝ HỢP ĐỒNG ONLINE – UNINET

            Người ký (Bên B): {row.SignedName}
            Email: {row.EmailNormalized}
            Thời điểm xác nhận: {signedTime} (UTC+7)
            Phiên bản hợp đồng: {row.TemplateVersion}
            SHA-256 của bản hợp đồng được xác nhận:
            {row.ContractSha256}

            Nội dung xác nhận:
            {row.SignatureStatement}
            """;
        using var original = PdfDocument.Open(bytes);
        using var receipt = PdfDocument.Open(Render(receiptText, fontBytes));
        using var export = new PdfDocumentBuilder();
        for (var page = 1; page <= original.NumberOfPages; page++)
            export.AddPage(original, page);
        for (var page = 1; page <= receipt.NumberOfPages; page++)
            export.AddPage(receipt, page);
        return export.Build();
    }

    private string Resolve(string key)
    {
        var root = Path.GetFullPath(settings.StoragePath);
        var target = Path.GetFullPath(Path.Combine(root, key));
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid contract storage key.");
        return target;
    }

    public static byte[] Render(string text, byte[] fontBytes)
    {
        using var document = new PdfDocumentBuilder();
        var font = document.AddTrueTypeFont(fontBytes);
        var page = document.AddPage(PageSize.A4);
        var y = 785d;
        const double size = 11;
        const double lineHeight = 17;
        const double width = 485;
        foreach (var paragraph in text.Replace("\r", "").Normalize(NormalizationForm.FormC).Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Fits(candidate))
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0) { WriteLine(line); line = ""; }
                // Unspaced input can exceed a line, including long names allowed by the profile form.
                foreach (var character in word)
                {
                    if (!Fits(line + character) && line.Length > 0)
                    {
                        WriteLine(line);
                        line = "";
                    }
                    line += character;
                }
            }
            WriteLine(line);
        }
        return document.Build();

        bool Fits(string line)
        {
            var letters = page.MeasureText(line, size, new PdfPoint(55, y), font);
            return letters.Count == 0 || letters.Max(x => x.BoundingBox.Right) - 55 <= width;
        }

        void WriteLine(string line)
        {
            if (y < 55) { page = document.AddPage(PageSize.A4); y = 785; }
            page.AddText(line, size, new PdfPoint(55, y), font);
            y -= lineHeight;
        }
    }
}
