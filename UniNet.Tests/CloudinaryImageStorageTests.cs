using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using UniNet.API.Services;
using UniNet.Application;
using UniNet.Application.Interfaces;
using Xunit;

namespace UniNet.Tests;

public sealed class CloudinaryImageStorageTests
{
    private const string Id = "uninet/images/owner/11111111111111111111111111111111";
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Cloudinary:CloudName"] = "fixture", ["Cloudinary:ApiKey"] = "fixture-key", ["Cloudinary:ApiSecret"] = "fixture-secret"
    }).Build();
    private sealed class Handler(string format = "png", string? response = null, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls;
        public Dictionary<string, string> Fields = [];
        public byte[] Bytes = [];
        public string? Mime;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                if (name == "file") { Bytes = await part.ReadAsByteArrayAsync(ct); Mime = part.Headers.ContentType!.MediaType; }
                else Fields[name] = await part.ReadAsStringAsync(ct);
            }
            var deleting = request.RequestUri!.AbsolutePath.EndsWith("/destroy");
            Assert.Equal($"https://api.cloudinary.com/v1_1/fixture/image/{(deleting ? "destroy" : "upload")}", request.RequestUri.AbsoluteUri);
            return new(status) { Content = new StringContent(response ?? (deleting ? "{\"result\":\"not found\"}" : JsonSerializer.Serialize(new {
                public_id = Id, format, secure_url = $"https://res.cloudinary.com/fixture/image/upload/v1/{Id}.{format}"
            }))) };
        }
    }
    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public async Task UploadSignsServerParametersAndPreservesBytesAndFormat(string format, string mime)
    {
        using var handler = new Handler(format); using var http = new HttpClient(handler);
        var result = await new CloudinaryImageStorage(http, Config()).Upload(new([1, 2, 3], Id, format), default);
        Assert.Equal(new ImageUploadResult($"https://res.cloudinary.com/fixture/image/upload/v1/{Id}.{format}", Id, format), result);
        Assert.Equal(new byte[] { 1, 2, 3 }, handler.Bytes); Assert.Equal(mime, handler.Mime);
        Assert.Equal("uninet/images/owner", handler.Fields["asset_folder"]); Assert.Equal("false", handler.Fields["overwrite"]);
        var parameters = new SortedDictionary<string, string>(handler.Fields, StringComparer.Ordinal);
        parameters.Remove("signature"); parameters.Remove("api_key");
        var canonical = string.Join("&", parameters.Select(x => $"{x.Key}={x.Value}"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + "fixture-secret"))).ToLowerInvariant(), handler.Fields["signature"]);
        Assert.DoesNotContain("fixture-secret", handler.Fields.Values);
    }
    [Theory]
    [InlineData("{\"public_id\":123,\"format\":\"png\",\"secure_url\":\"https://res.cloudinary.com/fixture/image/upload/x.png\"}")]
    [InlineData("{\"public_id\":\"wrong\",\"format\":\"png\"}")]
    [InlineData("{\"public_id\":\"uninet/images/owner/11111111111111111111111111111111\",\"format\":\"png\",\"secure_url\":\"https://res.cloudinary.com/other/image/upload/a.png\"}")]
    [InlineData("{\"public_id\":\"uninet/images/owner/11111111111111111111111111111111\",\"format\":\"png\",\"secure_url\":\"https://res.cloudinary.com/fixture/image/upload/v1/wrong.png\"}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task InvalidProviderResponsesBecomeStructuredErrors(string response)
    {
        using var handler = new Handler(response: response); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config()).Upload(new([1], Id, "png"), default));
        Assert.Equal("IMAGE_UPLOAD_FAILED", error.Code); Assert.Equal(502, error.Status);
    }
    [Fact]
    public async Task MissingCredentialsFailWithoutSending()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, new ConfigurationBuilder().Build()).Upload(new([1], Id, "png"), default));
        Assert.Equal("IMAGE_STORAGE_UNAVAILABLE", error.Code); Assert.Equal(503, error.Status); Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task DestroyIsSignedAndIdempotent()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        await new CloudinaryImageStorage(http, Config()).Destroy(Id, default);
        Assert.Equal(Id, handler.Fields["public_id"]); Assert.Equal("true", handler.Fields["invalidate"]);
        Assert.True(handler.Fields.ContainsKey("signature")); Assert.Empty(handler.Bytes);
    }
    [Fact]
    public async Task ProviderFailureHasGenericErrorCode()
    {
        using var handler = new Handler(status: HttpStatusCode.TooManyRequests); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config()).Upload(new([1], Id, "png"), default));
        Assert.Equal("IMAGE_UPLOAD_FAILED", error.Code); Assert.Equal(502, error.Status);
    }
}
