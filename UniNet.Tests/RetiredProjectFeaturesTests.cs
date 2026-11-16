using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class RetiredProjectFeaturesTests
{
    [Fact]
    public void EveryRemovedStorageOperationReturnsGoneWithoutDatabaseAccess()
    {
        using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql("Host=localhost;Database=never_opened").Options);
        var configuration = new ConfigurationBuilder().Build();
        object[] services = [new ProjectTaskService(db, configuration),
            new WorkspaceChatService(db, new WorkspaceAccessService(db)), new ExternalProjectDraftService(db)];
        foreach (var service in services)
        foreach (var method in service.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var args = method.GetParameters().Select(p => p.ParameterType.IsValueType
                ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(service, args));
            var gone = Assert.IsType<ProjectException>(error.InnerException);
            Assert.Equal(410, gone.Status);
            Assert.Equal("PROJECT_FEATURE_RETIRED", gone.Code);
        }
    }
}
