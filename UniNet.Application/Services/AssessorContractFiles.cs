using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
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
            ["TaxCode"] = profile.TaxCode,
            ["BankName"] = profile.BankName,
            ["BankAccountNumber"] = profile.BankAccountNumber,
            ["BankAccountHolder"] = profile.BankAccountHolder,
            ["TemplateVersion"] = settings.TemplateVersion,
            ["SignatureStatement"] = settings.SignatureStatement
        };
        // Replace once so profile values cannot introduce additional template substitutions.
        var text = Regex.Replace(template, @"\{\{([A-Za-z]+)\}\}", match =>
            values.TryGetValue(match.Groups[1].Value, out var value) ? value :
                throw new InvalidOperationException("Unknown contract placeholder."));
        var bytes = Render(text, await File.ReadAllBytesAsync(settings.FontPath, ct));
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var key = $"{row.Id:N}/{sha}.pdf";
        var target = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            try { File.Move(temporary, target, false); }
            catch (IOException) when (File.Exists(target)) {
                var existing = await File.ReadAllBytesAsync(target, ct);
                if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), SHA256.HashData(bytes))) throw;
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(key, sha);
    }

    public async Task<byte[]> Read(string key, string sha256, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(Resolve(key), ct);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), sha256, StringComparison.Ordinal))
            throw new InvalidOperationException("Contract integrity check failed.");
        return bytes;
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
