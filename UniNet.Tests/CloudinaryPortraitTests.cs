using System.Net;
using Microsoft.Extensions.Configuration;
using UniNet.API.Services;
using UniNet.Application;
using Xunit;

namespace UniNet.Tests;

public sealed class CloudinaryPortraitTests
{
    private const string Id = "uninet/portfolio/portraits/11111111111111111111111111111111";
    private sealed class Handler(bool jpeg = false) : HttpMessageHandler
    {
        public string Body = ""; public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Assert.Equal("https://api.cloudinary.com/v1_1/fixture/image/upload", request.RequestUri!.AbsoluteUri);
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { public_id = Id, format = jpeg ? "jpg" : "png", resource_type = "image", secure_url = $"https://res.cloudinary.com/fixture/image/upload/v1/{Id}.png" })) };
        }
    }
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Cloudinary:CloudName"] = "fixture", ["Cloudinary:ApiKey"] = "fixture-key", ["Cloudinary:ApiSecret"] = "fixture-secret"
    }).Build();
    [Fact]
    public async Task UploadUsesServerSignaturePngFolderAndNoOverwrite()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var storage = new CloudinaryPortraitStorage(new CloudinaryImageStorage(http, Config())); var url = await storage.Upload([1, 2, 3], Id, default);
        Assert.EndsWith(".png", url); Assert.Contains("name=signature", handler.Body); Assert.Contains("name=timestamp", handler.Body);
        Assert.Contains("name=asset_folder", handler.Body); Assert.Contains("uninet/portfolio/portraits", handler.Body);
        Assert.Contains("name=overwrite\r\n\r\nfalse", handler.Body); Assert.Contains("name=format\r\n\r\npng", handler.Body);
        Assert.Contains("Content-Type: image/png", handler.Body); Assert.DoesNotContain("fixture-secret", handler.Body);
    }
    [Fact]
    public async Task MissingConfigIsStructuredAndDoesNotSendRequest()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var storage = new CloudinaryPortraitStorage(new CloudinaryImageStorage(http, new ConfigurationBuilder().Build()));
        var error = await Assert.ThrowsAsync<AuthException>(() => storage.Upload([1], Id, default));
        Assert.Equal(503, error.Status); Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task JpegResponseIsRejected()
    {
        using var handler = new Handler(true); using var http = new HttpClient(handler);
        var storage = new CloudinaryPortraitStorage(new CloudinaryImageStorage(http, Config()));
        var error = await Assert.ThrowsAsync<AuthException>(() => storage.Upload([1], Id, default)); Assert.Equal(502, error.Status);
    }
}
