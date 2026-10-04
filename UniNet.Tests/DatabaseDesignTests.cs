using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class DatabaseDesignTests
{
    [Fact]
    public void PostgreSqlModelMatchesSixTableDesign()
    {
        using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test;Username=postgres").Options);
        var entities = db.GetService<IDesignTimeModel>().Model.GetEntityTypes().ToDictionary(x => x.GetTableName()!);
        Assert.Equal(new[] { "Accounts", "CareerProfiles", "RefreshTokens", "Skills", "UserVerifications", "Users" }, entities.Keys.Order(StringComparer.Ordinal).ToArray());
        var user = entities["Users"];
        Assert.False(user.FindProperty("FullName")!.IsNullable);
        Assert.Equal(255, user.FindProperty("FullName")!.GetMaxLength());
        Assert.False(user.FindProperty("Nickname")!.IsNullable);
        Assert.Equal(100, user.FindProperty("Nickname")!.GetMaxLength());
        Assert.Null(user.FindProperty("DisplayName"));
        Assert.Null(user.FindProperty("TaxCode"));
        Assert.True(user.GetIndexes().Single(x => x.Properties.Count == 1 && x.Properties[0].Name == "AccountId").IsUnique);
        var career = entities["CareerProfiles"];
        Assert.True(career.GetIndexes().Single(x => x.Properties.Count == 1 && x.Properties[0].Name == "UserId").IsUnique);
        foreach (var name in new[] { "BasicInfoJson", "EducationJson", "SkillsJson", "ExperienceJson", "ProjectsJson", "CertificatesJson", "ActivitiesJson", "LanguagesJson", "SocialLinksJson", "AppearanceJson" })
            Assert.Equal("jsonb", career.FindProperty(name)!.GetColumnType());
        foreach (var name in new[] { "SkillsJson", "ProjectsJson" })
            Assert.Equal("gin", career.GetIndexes().Single(x => x.Properties.Count == 1 && x.Properties[0].Name == name).FindAnnotation("Npgsql:IndexMethod")!.Value);
        var verification = entities["UserVerifications"];
        Assert.Equal("jsonb", verification.FindProperty("VerificationData")!.GetColumnType());
        Assert.Equal("jsonb", verification.FindProperty("DocumentUrls")!.GetColumnType());
        Assert.False(verification.GetForeignKeys().Single(x => x.Properties.Single().Name == "UserId").IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, verification.GetForeignKeys().Single(x => x.Properties.Single().Name == "ReviewedBy").DeleteBehavior);
        Assert.True(entities["Skills"].GetIndexes().Single(x => x.Properties.Count == 1 && x.Properties[0].Name == "Slug").IsUnique);
    }
}
