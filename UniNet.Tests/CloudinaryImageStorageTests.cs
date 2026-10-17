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
    private const string DocxId = "uninet/evidence/owner/22222222222222222222222222222222.docx";
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Cloudinary:CloudName"] = "fixture", ["Cloudinary:ApiKey"] = "fixture-key", ["Cloudinary:ApiSecret"] = "fixture-secret"
    }).Build();
    private sealed class Handler(
        string format = "png",
        string? response = null,
        HttpStatusCode status = HttpStatusCode.OK,
        string resourceType = "image",
        string? publicId = null) : HttpMessageHandler
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
            var endpointResourceType = deleting ? "image" : resourceType;
            Assert.Equal($"https://api.cloudinary.com/v1_1/fixture/{endpointResourceType}/{(deleting ? "destroy" : "upload")}", request.RequestUri.AbsoluteUri);
            var id = publicId ?? Id;
            return new(status) { Content = new StringContent(response ?? (deleting ? "{\"result\":\"not found\"}" : JsonSerializer.Serialize(new {
                asset_id = "fixture-asset-id",
                public_id = id,
                version = 123,
                format,
                resource_type = resourceType,
                type = "upload",
                secure_url = $"https://res.cloudinary.com/fixture/{resourceType}/upload/v1/{id}{(resourceType is "image" or "video" ? $".{format}" : "")}"
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
        Assert.Equal("image", result.ResourceType);
        Assert.Null(result.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, handler.Bytes); Assert.Equal(mime, handler.Mime);
        Assert.Equal("uninet/images/owner", handler.Fields["asset_folder"]); Assert.Equal("false", handler.Fields["overwrite"]);
        var parameters = new SortedDictionary<string, string>(handler.Fields, StringComparer.Ordinal);
        parameters.Remove("signature"); parameters.Remove("api_key");
        var canonical = string.Join("&", parameters.Select(x => $"{x.Key}={x.Value}"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical + "fixture-secret"))).ToLowerInvariant(), handler.Fields["signature"]);
        Assert.DoesNotContain("fixture-secret", handler.Fields.Values);
    }

    [Fact]
    public async Task RawDocumentUploadUsesRawResourceAndReturnsVerifiedCloudinaryMetadata()
    {
        const string id = "uninet/evidence/owner/11111111111111111111111111111111.pdf";
        using var handler = new Handler("pdf", resourceType: "raw", publicId: id);
        using var http = new HttpClient(handler);

        var result = await new CloudinaryImageStorage(http, Config()).Upload(
            new([1, 2, 3], id, "pdf", "raw"), default);

        Assert.Equal("raw", result.ResourceType);
        Assert.Equal(id, result.PublicId);
        Assert.Equal("pdf", result.Format);
        Assert.Equal($"https://res.cloudinary.com/fixture/raw/upload/v1/{id}", result.ImageUrl);
        Assert.Equal("application/pdf", handler.Mime);
        Assert.DoesNotContain("format", handler.Fields.Keys);
        Assert.Equal("uninet/evidence/owner", handler.Fields["asset_folder"]);
    }

    [Fact]
    public async Task RawDocxUploadAcceptsResponseFixtureWhenFormatMatchesValidatedUploadFormat()
    {
        using var handler = new Handler("docx", resourceType: "raw", publicId: DocxId);
        using var http = new HttpClient(handler);

        var result = await new CloudinaryImageStorage(http, Config()).Upload(
            new([1, 2, 3], DocxId, "docx", "raw"), default);

        Assert.Equal("raw", result.ResourceType);
        Assert.Equal(DocxId, result.PublicId);
        Assert.Equal("docx", result.Format);
        Assert.Equal($"https://res.cloudinary.com/fixture/raw/upload/v1/{DocxId}", result.ImageUrl);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            handler.Mime);
    }

    [Fact]
    public async Task RawDocxUploadAcceptsVerifiedResponseWithoutOptionalFormatField()
    {
        var response = JsonSerializer.Serialize(new
        {
            asset_id = "fixture-asset-id",
            public_id = DocxId,
            version = 123,
            resource_type = "raw",
            type = "upload",
            secure_url = $"https://res.cloudinary.com/fixture/raw/upload/v123/{DocxId}"
        });
        using var handler = new Handler(response: response, resourceType: "raw", publicId: DocxId);
        using var http = new HttpClient(handler);

        var result = await new CloudinaryImageStorage(http, Config()).Upload(
            new([1, 2, 3], DocxId, "docx", "raw"), default);

        Assert.Equal(DocxId, result.PublicId);
        Assert.Equal("raw", result.ResourceType);
        Assert.Equal("docx", result.Format);
        Assert.Equal($"https://res.cloudinary.com/fixture/raw/upload/v123/{DocxId}", result.ImageUrl);
    }

    [Theory]
    [InlineData("mp4", "video/mp4")]
    [InlineData("webm", "video/webm")]
    [InlineData("mov", "video/quicktime")]
    public async Task VideoUploadUsesVideoEndpointAndExpectedDeliveryUrl(string format, string mime)
    {
        var id = $"uninet/evidence/owner/{new string('a', 32)}";
        using var handler = new Handler(format, resourceType: "video", publicId: id);
        using var http = new HttpClient(handler);

        var result = await new CloudinaryImageStorage(http, Config()).Upload(
            new([1, 2, 3], id, format, "video"), default);

        Assert.Equal("video", result.ResourceType);
        Assert.Equal(id, result.PublicId);
        Assert.Equal(format, result.Format);
        Assert.Equal($"https://res.cloudinary.com/fixture/video/upload/v1/{id}.{format}", result.ImageUrl);
        Assert.Equal(mime, handler.Mime);
        Assert.Equal("false", handler.Fields["overwrite"]);
        Assert.DoesNotContain("format", handler.Fields.Keys);
    }

    [Fact]
    public async Task VideoUploadRejectsResponseWithoutFormat()
    {
        var id = $"uninet/evidence/owner/{new string('a', 32)}";
        var response = JsonSerializer.Serialize(new
        {
            public_id = id,
            resource_type = "video",
            secure_url = $"https://res.cloudinary.com/fixture/video/upload/v123/{id}.mp4"
        });
        using var handler = new Handler(response: response, resourceType: "video", publicId: id);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() =>
            new CloudinaryImageStorage(http, Config()).Upload(
                new([1], id, "mp4", "video"), default));

        Assert.Equal("FILE_UPLOAD_FAILED", error.Code);
    }

    [Fact]
    public async Task VideoUploadRejectsPresentMismatchedFormat()
    {
        var id = $"uninet/evidence/owner/{new string('a', 32)}";
        var response = JsonSerializer.Serialize(new
        {
            public_id = id,
            format = "webm",
            resource_type = "video",
            secure_url = $"https://res.cloudinary.com/fixture/video/upload/v123/{id}.mp4"
        });
        using var handler = new Handler("mp4", response: response, resourceType: "video", publicId: id);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1], id, "mp4", "video"), default));

        Assert.Equal("FILE_UPLOAD_FAILED", error.Code);
    }

    [Fact]
    public async Task RawDocxUploadRejectsMismatchedPublicId()
    {
        var unexpectedId = "uninet/evidence/owner/33333333333333333333333333333333.docx";
        var response = JsonSerializer.Serialize(new
        {
            public_id = unexpectedId,
            version = 123,
            format = "docx",
            resource_type = "raw",
            secure_url = $"https://res.cloudinary.com/fixture/raw/upload/v123/{DocxId}"
        });
        using var handler = new Handler("docx", response: response, resourceType: "raw", publicId: DocxId);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1], DocxId, "docx", "raw"), default));

        Assert.Equal("FILE_UPLOAD_FAILED", error.Code);
    }

    [Fact]
    public async Task RawDocxUploadRejectsWrongResourceType()
    {
        var response = JsonSerializer.Serialize(new
        {
            public_id = DocxId,
            version = 123,
            format = "docx",
            resource_type = "image",
            secure_url = $"https://res.cloudinary.com/fixture/raw/upload/v123/{DocxId}"
        });
        using var handler = new Handler("docx", response: response, resourceType: "raw", publicId: DocxId);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1], DocxId, "docx", "raw"), default));

        Assert.Equal("FILE_UPLOAD_FAILED", error.Code);
    }

    [Fact]
    public async Task RawDocxUploadRejectsMismatchedPresentFormat()
    {
        var response = JsonSerializer.Serialize(new
        {
            public_id = DocxId,
            version = 123,
            format = "pdf",
            resource_type = "raw",
            secure_url = $"https://res.cloudinary.com/fixture/raw/upload/v123/{DocxId}"
        });
        using var handler = new Handler("docx", response: response, resourceType: "raw", publicId: DocxId);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1], DocxId, "docx", "raw"), default));

        Assert.Equal("FILE_UPLOAD_FAILED", error.Code);
    }

    [Fact]
    public async Task ImageUploadRejectsResponseWithMismatchedFormat()
    {
        var response = JsonSerializer.Serialize(new
        {
            public_id = Id,
            version = 123,
            format = "jpg",
            resource_type = "image",
            secure_url = $"https://res.cloudinary.com/fixture/image/upload/v123/{Id}.png"
        });
        using var handler = new Handler("png", response: response, publicId: Id);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1], Id, "png"), default));

        Assert.Equal("IMAGE_UPLOAD_FAILED", error.Code);
    }

    [Fact]
    public async Task ImageUploadRejectsResponseWithoutFormat()
    {
        var response = JsonSerializer.Serialize(new
        {
            public_id = Id,
            version = 123,
            resource_type = "image",
            secure_url = $"https://res.cloudinary.com/fixture/image/upload/v123/{Id}.png"
        });
        using var handler = new Handler("png", response: response, publicId: Id);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1], Id, "png"), default));

        Assert.Equal("IMAGE_UPLOAD_FAILED", error.Code);
    }

    [Theory]
    [InlineData("http://res.cloudinary.com/fixture/raw/upload/v123/uninet/evidence/owner/22222222222222222222222222222222.docx")]
    [InlineData("https://attacker.example/fixture/raw/upload/v123/uninet/evidence/owner/22222222222222222222222222222222.docx")]
    [InlineData("https://res.cloudinary.com/fixture/raw/upload/v123/uninet/evidence/owner/other.docx")]
    public async Task RawDocxUploadRejectsInsecureForeignOrMismatchedDeliveryUrl(string secureUrl)
    {
        var response = JsonSerializer.Serialize(new
        {
            public_id = DocxId,
            version = 123,
            format = "docx",
            resource_type = "raw",
            secure_url = secureUrl
        });
        using var handler = new Handler("docx", response: response, resourceType: "raw", publicId: DocxId);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1], DocxId, "docx", "raw"), default));

        Assert.Equal("FILE_UPLOAD_FAILED", error.Code);
    }

    [Fact]
    public async Task RawDocumentUploadRejectsProviderResponseForDifferentResourceType()
    {
        const string id = "uninet/evidence/owner/11111111111111111111111111111111.pdf";
        var wrongResource = JsonSerializer.Serialize(new
        {
            public_id = id,
            format = "pdf",
            resource_type = "image",
            secure_url = $"https://res.cloudinary.com/fixture/image/upload/v1/{id}"
        });
        using var handler = new Handler("pdf", response: wrongResource, resourceType: "raw", publicId: id);
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<AuthException>(() => new CloudinaryImageStorage(http, Config())
            .Upload(new([1, 2, 3], id, "pdf", "raw"), default));

        Assert.Equal("FILE_UPLOAD_FAILED", error.Code);
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
