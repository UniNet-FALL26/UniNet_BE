using System.IO.Compression;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain.Entities;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectDocumentTextReaderTests
{
    private static UploadedFile File(string format) => new() { FileName = "report." + format, Format = format, ResourceType = "raw" };

    internal static byte[] Docx(string xml)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("[Content_Types].xml");
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), Encoding.UTF8);
            writer.Write(xml);
        }
        return buffer.ToArray();
    }

    [Fact]
    public void PdfKeepsRealPageNumbersIncludingBlankPages()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("UniNet project", 12, new PdfPoint(25, 700), font);
        builder.AddPage(PageSize.A4);
        builder.AddPage(PageSize.A4).AddText("Backend uses C#", 12, new PdfPoint(25, 700), font);
        var result = new ProjectDocumentTextReader().Read(File("pdf"), builder.Build());
        Assert.Equal(new int?[] { 1, 3 }, result.Segments.Select(s => s.PageNumber));
        Assert.Contains("UniNet project", result.Segments[0].Text);
        Assert.Equal(64, result.Sha256.Length);
    }

    [Fact]
    public void DocxReadsParagraphsAndTablesWithoutInventingPageNumbers()
    {
        var bytes = Docx("""
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
              <w:p><w:r><w:t>Dự án UniNet</w:t></w:r></w:p><w:p/>
              <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Nguyễn Văn An</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
              <w:p><w:del><w:r><w:delText>Removed</w:delText></w:r></w:del></w:p>
            </w:body></w:document>
            """);
        var result = new ProjectDocumentTextReader().Read(File("docx"), bytes);
        Assert.Equal(new[] { "Dự án UniNet", "Nguyễn Văn An" }, result.Segments.Select(s => s.Text));
        Assert.Equal(new int?[] { 1, 3 }, result.Segments.Select(s => s.ParagraphNumber));
        Assert.All(result.Segments, s => Assert.Null(s.PageNumber));
    }

    [Fact]
    public void TxtPreservesUtf8AndSourceLineNumbers()
    {
        var result = new ProjectDocumentTextReader().Read(File("txt"), Encoding.UTF8.GetBytes("\uFEFFDự án\r\n\r\nC# và React"));
        Assert.Equal("Dự án", result.Segments[0].Text);
        Assert.Equal(new int?[] { 1, 3 }, result.Segments.Select(s => s.LineNumber));
    }

    [Fact]
    public void ScannedOrEmptyPdfReportsNoText()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        var error = Assert.Throws<ProjectExtractionException>(() => new ProjectDocumentTextReader().Read(File("pdf"), builder.Build()));
        Assert.Equal("DOCUMENT_NO_TEXT", error.Code);
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("docx")]
    [InlineData("pdf")]
    public void MalformedFilesReportUnreadable(string format)
    {
        var error = Assert.Throws<ProjectExtractionException>(() => new ProjectDocumentTextReader().Read(File(format), [0xFF, 0x00, 0xFF]));
        Assert.Equal("DOCUMENT_UNREADABLE", error.Code);
    }

    [Fact]
    public void DocxCannotResolveExternalEntities()
    {
        var bytes = Docx("""
            <!DOCTYPE document [<!ENTITY secret SYSTEM "file:///private">]>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>&secret;</w:t></w:r></w:p></w:body></w:document>
            """);
        var error = Assert.Throws<ProjectExtractionException>(() => new ProjectDocumentTextReader().Read(File("docx"), bytes));
        Assert.Equal("DOCUMENT_UNREADABLE", error.Code);
    }

    [Fact]
    public void TextLimitRejectsInsteadOfSilentlyTruncating()
    {
        var error = Assert.Throws<ProjectExtractionException>(() => new ProjectDocumentTextReader().Read(File("txt"), Encoding.UTF8.GetBytes(new string('a', 100001))));
        Assert.Equal("DOCUMENT_TEXT_LIMIT", error.Code);
    }
}
