using System.IO.Compression;
using System.Buffers.Binary;
using System.Text;
using Microsoft.AspNetCore.Http;
using UniNet.API.Services;
using UniNet.Application;
using Xunit;

namespace UniNet.Tests;

public sealed class UploadFileReaderTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");

    [Theory]
    [InlineData("clip.mp4", "video/mp4", "isom")]
    [InlineData("clip.MOV", "video/quicktime", "qt  ")]
    public async Task ReadsIsoBaseMediaVideoContainers(string fileName, string contentType, string brand)
    {
        var upload = await UploadFileReader.Read(File(IsoBaseMediaFile(brand), fileName, contentType), default);

        Assert.Equal(Path.GetExtension(fileName)[1..].ToLowerInvariant(), upload.Format);
        Assert.Equal("video", upload.ResourceType);
        Assert.Equal(fileName, upload.FileName);
    }

    [Fact]
    public async Task ReadsWebmByEbmlContainerAndDocType()
    {
        var upload = await UploadFileReader.Read(File(WebmFile(), "clip.webm", "video/webm"), default);

        Assert.Equal("webm", upload.Format);
        Assert.Equal("video", upload.ResourceType);
    }

    [Theory]
    [InlineData("clip.MP4", "video/mp4", "isom", "mp4")]
    [InlineData("clip.WEBM", "video/webm", "webm", "webm")]
    [InlineData("clip.MOV", "video/quicktime", "qt  ", "mov")]
    public async Task VideoExtensionsAreCaseInsensitive(
        string fileName,
        string contentType,
        string brand,
        string format)
    {
        var bytes = format == "webm" ? WebmFile() : IsoBaseMediaFile(brand);

        var upload = await UploadFileReader.Read(File(bytes, fileName, contentType), default);

        Assert.Equal(format, upload.Format);
        Assert.Equal("video", upload.ResourceType);
    }

    [Theory]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("clip.mov", "video/quicktime")]
    public async Task RejectsEmptyVideos(string fileName, string contentType)
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File([], fileName, contentType), default));

        Assert.Equal("INVALID_VIDEO", error.Code);
    }

    [Theory]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("clip.mov", "video/quicktime")]
    public async Task RejectsSpoofedVideoContent(string fileName, string contentType)
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(Encoding.ASCII.GetBytes("not a video container"), fileName, contentType), default));

        Assert.Equal("INVALID_VIDEO", error.Code);
    }

    [Fact]
    public async Task RejectsVideoWithMismatchedMimeType()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(IsoBaseMediaFile("isom"), "clip.mp4", "application/octet-stream"), default));

        Assert.Equal("INVALID_VIDEO", error.Code);
    }

    [Fact]
    public async Task EnforcesConfiguredVideoSizeLimit()
    {
        const int maxBytes = 32;
        var bytes = IsoBaseMediaFile("isom", maxBytes + 1);

        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(bytes, "clip.mp4", "video/mp4"), default, maxBytes));

        Assert.Equal("INVALID_VIDEO", error.Code);
    }

    [Fact]
    public async Task RejectsUnsupportedVideoExtension()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(IsoBaseMediaFile("isom"), "clip.avi", "video/x-msvideo"), default));

        Assert.Equal("INVALID_VIDEO", error.Code);
    }

    [Fact]
    public async Task RejectsVideoLargerThanConfiguredLimit()
    {
        const int maxBytes = 50 * 1024 * 1024;
        var bytes = IsoBaseMediaFile("isom", maxBytes + 1);

        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(bytes, "clip.mp4", "video/mp4"), default));

        Assert.Equal("INVALID_VIDEO", error.Code);
    }


    [Fact]
    public async Task ReadsImagesByContentAndKeepsTheirExistingUploadFormat()
    {
        var uploaded = await UploadFileReader.Read(File(Png, "photo.png", "image/png"), default);

        Assert.Equal("png", uploaded.Format);
        Assert.Equal("image", uploaded.ResourceType);
        Assert.Equal("photo.png", uploaded.FileName);
        Assert.Equal(Png, uploaded.Bytes);
    }

    [Fact]
    public async Task ReadsJpegAndWebpAndNormalizesTheImageExtensionFromContent()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0x00, 0xFF, 0xD9];
        byte[] webp = [.. Encoding.ASCII.GetBytes("RIFF"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("WEBP"), 0, 0, 0, 0];

        var jpegUpload = await UploadFileReader.Read(File(jpeg, "photo.jpeg", "image/jpeg"), default);
        var webpUpload = await UploadFileReader.Read(File(webp, "photo.webp", "image/webp"), default);

        Assert.Equal("jpg", jpegUpload.Format);
        Assert.Equal("photo.jpg", jpegUpload.FileName);
        Assert.Equal("webp", webpUpload.Format);
        Assert.Equal("image", webpUpload.ResourceType);
    }

    [Theory]
    [InlineData("pdf", "application/pdf", "%PDF-1.7\nfixture\n%%EOF")]
    [InlineData("txt", "text/plain", "Plain UTF-8 task evidence")]
    public async Task ReadsDocumentFormatsWithMatchingExtensionAndMime(string extension, string mime, string body)
    {
        var uploaded = await UploadFileReader.Read(
            File(Encoding.UTF8.GetBytes(body), $"evidence.{extension}", mime), default);

        Assert.Equal(extension, uploaded.Format);
        Assert.Equal("raw", uploaded.ResourceType);
        Assert.Equal($"evidence.{extension}", uploaded.FileName);
    }

    [Fact]
    public async Task ReadsUppercaseDocumentExtensionAndNormalizesTheStoredFormat()
    {
        var uploaded = await UploadFileReader.Read(
            File(Encoding.UTF8.GetBytes("%PDF-1.7\nfixture\n%%EOF"), "evidence.PDF", "application/pdf"),
            default);

        Assert.Equal("pdf", uploaded.Format);
        Assert.Equal("evidence.PDF", uploaded.FileName);
    }

    [Theory]
    [InlineData("doc", "application/msword")]
    [InlineData("xls", "application/vnd.ms-excel")]
    [InlineData("ppt", "application/vnd.ms-powerpoint")]
    public async Task RejectsLegacyOfficeDocumentsWhenOnlyGenericOleHeaderIsKnown(string extension, string mime)
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(new byte[512], $"evidence.{extension}", mime), default));

        Assert.Equal("INVALID_UPLOAD", error.Code);
    }

    [Theory]
    [InlineData("docx", "word/document.xml")]
    [InlineData("xlsx", "xl/workbook.xml")]
    [InlineData("pptx", "ppt/presentation.xml")]
    public async Task ReadsOfficeOpenXmlOnlyWhenExpectedPackageEntryExists(string extension, string requiredEntry)
    {
        var bytes = CreateOfficePackage(requiredEntry);
        var uploaded = await UploadFileReader.Read(
            File(bytes, $"evidence.{extension}", MimeFor(extension)), default);

        Assert.Equal(extension, uploaded.Format);
        Assert.Equal("raw", uploaded.ResourceType);
    }

    [Fact]
    public async Task RejectsDocumentWhenExtensionAndMimeDoNotMatchItsContent()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(Encoding.UTF8.GetBytes("%PDF-1.7\n%%EOF"), "evidence.docx", "application/pdf"), default));

        Assert.Equal("INVALID_UPLOAD", error.Code);
    }

    [Fact]
    public async Task RejectsOpenXmlArchiveWithoutExpectedOfficeDocumentPart()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(CreateOfficePackage("word/styles.xml"), "evidence.docx", MimeFor("docx")), default));

        Assert.Equal("INVALID_UPLOAD", error.Code);
    }

    [Fact]
    public async Task RejectsUnsupportedOrMislabeledBinaryFile()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File([0x00, 0x01, 0x02, 0x03], "evidence.zip", "application/zip"), default));

        Assert.Equal("INVALID_UPLOAD", error.Code);
    }

    [Fact]
    public async Task RejectsDocumentsOverTheDocumentSizeLimit()
    {
        var bytes = new byte[UploadFileReader.MaxDocumentBytes + 1];
        Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("%%EOF").CopyTo(bytes, bytes.Length - 5);

        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(bytes, "large.pdf", "application/pdf"), default));

        Assert.Equal("INVALID_UPLOAD", error.Code);
    }

    [Fact]
    public async Task UsesContentDetectedImageExtensionInsteadOfReflectingAnUntrustedFilename()
    {
        var uploaded = await UploadFileReader.Read(File(Png, "../../user-controlled.html", "text/html"), default);

        Assert.Equal("user-controlled.png", uploaded.FileName);
        Assert.Equal("png", uploaded.Format);
        Assert.Equal("image", uploaded.ResourceType);
    }

    [Fact]
    public async Task PreservesImageErrorCodeForMalformedImageUploads()
    {
        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File([0x00, 0x01, 0x02], "photo.png", "image/png"), default));

        Assert.Equal("INVALID_IMAGE", error.Code);
    }

    [Fact]
    public async Task PreservesImageErrorCodeForImagesOverTheExistingFiveMegabyteLimit()
    {
        var bytes = new byte[ImageFileReader.MaxBytes + 1];
        Png.CopyTo(bytes, 0);

        var error = await Assert.ThrowsAsync<AuthException>(() => UploadFileReader.Read(
            File(bytes, "large.png", "image/png"), default));

        Assert.Equal("INVALID_IMAGE", error.Code);
    }

    private static FormFile File(byte[] bytes, string fileName, string contentType)
        => new(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private static byte[] IsoBaseMediaFile(string brand, int? totalLength = null)
    {
        var length = Math.Max(totalLength ?? 24, 16);
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 16);
        Encoding.ASCII.GetBytes("ftyp").CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes(brand).CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 0);
        if (length >= 20)
            Encoding.ASCII.GetBytes(brand).CopyTo(bytes, 16);
        return bytes;
    }

    private static byte[] WebmFile()
        => [
            0x1A, 0x45, 0xDF, 0xA3, 0x87,
            0x42, 0x82, 0x84, (byte)'w', (byte)'e', (byte)'b', (byte)'m',
            0x18, 0x53, 0x80, 0x67, 0xFF
        ];

    private static string MimeFor(string extension) => extension switch
    {
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => throw new ArgumentOutOfRangeException(nameof(extension))
    };

    private static byte[] CreateOfficePackage(string mainPart)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("[Content_Types].xml");
            archive.CreateEntry(mainPart);
        }
        return stream.ToArray();
    }

}
