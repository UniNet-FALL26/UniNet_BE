using Microsoft.Extensions.Configuration;
using UniNet.API.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class LocalEnvConfigurationTests
{
    [Fact]
    public void ReadsSingleLineValuesQuotesCommentsAndNestedKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uninet-env-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, ".env");
            File.WriteAllText(path, "# comment\nCloudinary__CloudName=fixture\nCloudinary__ApiKey=\nCloudinary__ApiSecret='secret=a#b'\nexport PortraitWorker__Token=\"fixture-token\"\nmalformed\n");
            var values = LocalEnvConfiguration.Read(path);
            Assert.Equal("fixture", values["Cloudinary:CloudName"]); Assert.Equal("", values["Cloudinary:ApiKey"]);
            Assert.Equal("secret=a#b", values["Cloudinary:ApiSecret"]); Assert.Equal("fixture-token", values["PortraitWorker:Token"]); Assert.Equal(4, values.Count);
        }
        finally { File.Delete(Path.Combine(directory, ".env")); Directory.Delete(directory); }
    }
    [Fact]
    public void RuntimeFindsRepoEnvAndCommandLineOverridesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "uninet-env-" + Guid.NewGuid().ToString("N")); var api = Path.Combine(directory, "UniNet.API"); Directory.CreateDirectory(api);
        try
        {
            File.WriteAllText(Path.Combine(directory, "UniNet.sln"), "fixture");
            File.WriteAllText(Path.Combine(directory, ".env"), "Cloudinary__CloudName=fixture\nPortraitWorker__Url=http://127.0.0.1:7000/remove-background\n");
            var configuration = new ConfigurationManager(); LocalEnvConfiguration.Apply(configuration, api, ["--Cloudinary:CloudName=override"]);
            Assert.Equal("override", configuration["Cloudinary:CloudName"]); Assert.Equal("http://127.0.0.1:7000/remove-background", configuration["PortraitWorker:Url"]);
        }
        finally { File.Delete(Path.Combine(directory, ".env")); File.Delete(Path.Combine(directory, "UniNet.sln")); Directory.Delete(api); Directory.Delete(directory); }
    }
}
