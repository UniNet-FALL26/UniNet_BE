using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using UniNet.API.Services;
using UniNet.Application;
using Xunit;

namespace UniNet.Tests;

public sealed class ProfileImageStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "uninet-image-tests-" + Guid.NewGuid().ToString("N"));
    private ProfileImageStorage Storage => new(new TestEnvironment { ContentRootPath = root });
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aAd8AAAAASUVORK5CYII=");

    [Fact]
    public async Task Upload_UsesGeneratedNameAndPersistsImageWithoutTrustingFilenameOrMime()
    {
        var file = new FormFile(new MemoryStream(Png), 0, Png.Length, "file", "../../malicious.html") { Headers = new HeaderDictionary(), ContentType = "text/html" };
        var name = await Storage.Save(file, default);
        Assert.Matches("^[a-f0-9]{32}\\.png$", name);
        Assert.Equal(Png, await File.ReadAllBytesAsync(Storage.Find(name)!));
        Assert.Equal("image/png", ProfileImageStorage.ContentType(name));
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("<html>not an image</html>")]
    [InlineData("")]
    public async Task Upload_RejectsNonImages(string body)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "photo.png");
        var error = await Assert.ThrowsAsync<AuthException>(() => Storage.Save(file, default));
        Assert.Equal(400, error.Status);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Upload_RejectsOversizeEvenWhenReportedLengthIsSmall()
    {
        var bytes = new byte[ProfileImageStorage.MaxBytes + 1]; Png.CopyTo(bytes, 0);
        await Assert.ThrowsAsync<AuthException>(() => Storage.Save(new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "large.png"), default));
        await Assert.ThrowsAsync<AuthException>(() => Storage.Save(new MisreportedFile(bytes), default));
        Assert.False(Directory.Exists(root));
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("C:\\secret.png")]
    [InlineData("photo.svg")]
    [InlineData("00000000000000000000000000000000.png")]
    public void Read_RejectsTraversalUnsupportedNamesAndMissingFiles(string name) => Assert.Null(Storage.Find(name));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
    private sealed class MisreportedFile(byte[] bytes) : IFormFile
    {
        public string ContentType => "image/png";
        public string ContentDisposition => "";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length => 1;
        public string Name => "file";
        public string FileName => "photo.png";
        public Stream OpenReadStream() => new MemoryStream(bytes);
        public void CopyTo(Stream target) => target.Write(bytes);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => target.WriteAsync(bytes, cancellationToken).AsTask();
    }
}
