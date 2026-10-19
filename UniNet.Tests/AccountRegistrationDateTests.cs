using System.Text.Json;
using UniNet.Application;
using UniNet.Domain;
using Xunit;

namespace UniNet.Tests;

public sealed class AccountRegistrationDateTests
{
    [Fact]
    public void AccountResponseExposesOriginalCreationDateAsCamelCaseJson()
    {
        var createdAt = new DateTimeOffset(2026, 10, 17, 3, 0, 0, TimeSpan.Zero);
        var account = new Account { CreatedAt = createdAt, Email = "registration@example.test" };
        var response = AuthMapping.ToResponse(account);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(createdAt, json.RootElement.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(createdAt, AuthMapping.ToResponse(account).CreatedAt);
    }
}
