using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Domain.Entities;

namespace UniNet.Application.Services;

public sealed class ProjectDocumentTextReader : IProjectDocumentTextReader
{
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxTextCharacters = 100_000;
    public const int MaxPdfPages = 100;
    private const int MaxDocxXmlBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public ProjectDocumentText Read(UploadedFile file, byte[] content, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (file.ResourceType != "raw" || file.Format is not ("pdf" or "docx" or "txt"))
            throw Error("DOCUMENT_FORMAT_UNSUPPORTED", "Only PDF, DOCX and UTF-8 TXT can be read.");
        if (content.Length is 0 or > MaxFileBytes)
            throw Error("DOCUMENT_SIZE_LIMIT", "Document size must be between 1 byte and 10 MB.");
        var segments = new List<DocumentTextSegment>();
        var total = 0;
        void Add(string text, int? page = null, int? paragraph = null, int? line = null)
        {
            ct.ThrowIfCancellationRequested();
            text = text.Trim();
            total += text.Length;
            if (total > MaxTextCharacters || segments.Count >= 5000)
                throw Error("DOCUMENT_TEXT_LIMIT", "Document exceeds the supported text limit.");
            if (text.Length > 0) segments.Add(new(segments.Count + 1, text, page, paragraph, line));
        }
        try
        {
            switch (file.Format)
            {
                case "pdf":
                    if (!content.AsSpan().StartsWith("%PDF-"u8)) throw new InvalidDataException();
                    using (var pdf = PdfDocument.Open(content))
                    {
                        if (pdf.NumberOfPages > MaxPdfPages)
                            throw Error("DOCUMENT_PAGE_LIMIT", "PDF exceeds 100 pages.");
                        for (var number = 1; number <= pdf.NumberOfPages; number++)
                        {
                            ct.ThrowIfCancellationRequested();
                            Add(ContentOrderTextExtractor.GetText(pdf.GetPage(number)), page: number);
                        }
                    }
                    break;
                case "docx":
                    ReadDocx(content, (text, paragraph) => Add(text, paragraph: paragraph), ct);
                    break;
                case "txt":
                    var text = StrictUtf8.GetString(content).TrimStart('\uFEFF');
                    if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                        throw new InvalidDataException();
                    using (var reader = new StringReader(text))
                    {
                        string? line;
                        var number = 0;
                        while ((line = reader.ReadLine()) != null) Add(line, line: ++number);
                    }
                    break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ProjectExtractionException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw Error("DOCUMENT_UNREADABLE", "Document is malformed, encrypted or cannot be read.");
        }
        if (segments.Count == 0)
            throw Error("DOCUMENT_NO_TEXT", "No readable text found. Scanned PDFs require OCR, which is not supported yet.");
        return new(file.Id, file.FileName, file.Format, Convert.ToHexString(SHA256.HashData(content)), segments);
    }

    private static void ReadDocx(byte[] content, Action<string, int> add, CancellationToken ct)
    {
        using var memory = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        if (archive.Entries.Count > 1024) throw new InvalidDataException();
        var parts = archive.Entries.Where(x => x.FullName == "word/document.xml").ToList();
        if (parts.Count != 1 || archive.GetEntry("[Content_Types].xml") == null) throw new InvalidDataException();
        var part = parts[0];
        if (part.Length > MaxDocxXmlBytes)
            throw Error("DOCUMENT_TEXT_LIMIT", "DOCX document XML exceeds the supported limit.");
        using var stream = part.Open();
        using var xml = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxDocxXmlBytes
        });
        var document = XDocument.Load(xml);
        if (document.Root?.Name != Word + "document") throw new InvalidDataException();
        var paragraph = 0;
        foreach (var p in document.Descendants(Word + "p"))
        {
            ct.ThrowIfCancellationRequested();
            // Includes table cells; deleted text and field instructions are not treated as visible text.
            var text = string.Concat(p.Descendants().Select(e => e.Name == Word + "t" &&
                !e.Ancestors(Word + "del").Any() ? e.Value :
                e.Name == Word + "tab" ? "\t" : e.Name == Word + "br" ? "\n" : ""));
            add(text, ++paragraph);
        }
    }

    private static ProjectExtractionException Error(string code, string message) => new(code, message);
}
