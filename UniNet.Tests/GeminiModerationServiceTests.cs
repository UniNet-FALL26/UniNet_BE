using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class GeminiModerationServiceTests
{
    [Theory]
    [InlineData("custom-model", "custom-model")]
    [InlineData(null, "gemini-3.1-flash-lite")]
    [InlineData("", "gemini-3.1-flash-lite")]
    public async Task SendsConfiguredModelAndHeaderWithSimpleJson(string? model, string expectedModel)
    {
        using var handler = new StubHandler(async (request, _) =>
        {
            Assert.Equal($"/v1beta/models/{expectedModel}:generateContent", request.RequestUri!.AbsolutePath);
            Assert.Equal(string.Empty, request.RequestUri.Query);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Contains("Test project", body.RootElement.GetProperty("contents")[0]
                .GetProperty("parts")[0].GetProperty("text").GetString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"candidates":[{"content":{"parts":[{"text":"{\"contentResult\":\"safe\",\"linkResult\":\"safe\",\"confidence\":0.9,\"reason\":\"Looks safe\",\"flags\":[]}"}]}}]}
                    """, Encoding.UTF8, "application/json")
            };
        });
        using var client = new HttpClient(handler);
        var result = await CreateService(client, model).AnalyzeProjectAsync(Input());
        Assert.Equal("safe", result.ContentResult);
        Assert.Equal("Looks safe", result.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 1)]
    [InlineData(HttpStatusCode.NotFound, 1)]
    [InlineData(HttpStatusCode.TooManyRequests, 3)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 3)]
    public async Task ReturnsActualErrorBodyIncludingAfterRetries(HttpStatusCode status, int expectedAttempts)
    {
        var attempts = 0;
        using var handler = new StubHandler((request, _) =>
        {
            attempts++;
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent($"{{\"error\":{{\"message\":\"Failure {attempts}\"}}}}")
            });
        });
        using var client = new HttpClient(handler);
        var result = await CreateService(client, null).AnalyzeProjectAsync(Input());
        Assert.Equal(expectedAttempts, attempts);
        Assert.Equal("error", result.ContentResult);
        Assert.Contains($"{(int)status} ({status})", result.Reason);
        Assert.Contains($"{{\"error\":{{\"message\":\"Failure {expectedAttempts}\"}}}}", result.Reason);
    }

    private static GeminiModerationService CreateService(HttpClient client, string? model) => new(client,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key",
            ["Gemini:Model"] = model
        }).Build());

    private static ProjectModerationInput Input() => new("Test project", null, null, [], 2, null, [], []);

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => send(request, token);
    }
}
