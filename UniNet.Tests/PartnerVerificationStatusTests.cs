using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PartnerVerificationStatusTests
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatusReturnsOwnedSubmittedSnapshotAndReviewDateWithoutPrivateDocuments(bool approve)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var partner = new Account { Email = "snapshot-partner@example.test", Role = AccountRole.Partner,
            Profile = new() { FullName = "Original company", OrganizationName = "Submitted organization", Nickname = "snapshot-partner", PartnerType = PartnerType.Company } };
        var other = new Account { Email = "snapshot-other@example.test", Role = AccountRole.Partner,
            Profile = new() { FullName = "Other company", Nickname = "snapshot-other", PartnerType = PartnerType.Store } };
        var admin = new Account { Email = "snapshot-admin@example.test", Role = AccountRole.Admin,
            Profile = new() { FullName = "Admin", Nickname = "snapshot-admin" } };
        db.Accounts.AddRange(partner, other, admin);
        await db.SaveChangesAsync();
        var service = new PartnerVerificationService(db);
        var submitted = await service.SubmitDocumentsAsync(partner.Id, " 0123456789 ", " Submitted representative ",
            [new(VerificationDocumentType.BusinessRegistration, "Registration", null, 0)],
            [new("registration.pdf", "application/pdf", "%PDF-1.7\nprivate-document-payload"u8.ToArray())], default);
        var pending = Json(await service.GetMyStatusAsync(partner.Id, default));
        Assert.Equal("Submitted organization", pending.GetProperty("organizationName").GetString());
        Assert.Equal("0123456789", pending.GetProperty("taxCode").GetString());
        Assert.Equal("Submitted representative", Json(submitted).GetProperty("representativeName").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("reviewedAt").ValueKind);

        if (approve) await service.ApproveAsync(admin.Id, submitted.Id!.Value, default);
        else await service.RejectAsync(admin.Id, submitted.Id!.Value, "Updated registration required", default);
        partner.Profile!.OrganizationName = "Edited current profile";
        partner.Profile.PartnerType = PartnerType.Store;
        await db.SaveChangesAsync();
        var result = Json(await service.GetMyStatusAsync(partner.Id, default));
        Assert.Equal("Submitted organization", result.GetProperty("organizationName").GetString());
        Assert.Equal("Submitted representative", result.GetProperty("representativeName").GetString());
        Assert.Equal("0123456789", result.GetProperty("taxCode").GetString());
        Assert.Equal(approve ? 1 : 2, result.GetProperty("status").GetInt32());
        Assert.True(result.GetProperty("reviewedAt").GetDateTimeOffset() >= result.GetProperty("submittedAt").GetDateTimeOffset());
        Assert.False(result.TryGetProperty("verificationData", out _));
        Assert.False(result.TryGetProperty("documentUrls", out _));
        Assert.False(result.TryGetProperty("reviewedBy", out _));
        Assert.DoesNotContain("private-document-payload", result.GetRawText());
        Assert.DoesNotContain("documentBase64", result.GetRawText());
        var otherStatus = Json(await service.GetMyStatusAsync(other.Id, default));
        Assert.Equal(JsonValueKind.Null, otherStatus.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, otherStatus.GetProperty("organizationName").ValueKind);
        Assert.Equal(JsonValueKind.Null, otherStatus.GetProperty("reviewedAt").ValueKind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken")]
    [InlineData("{\"organizationName\":99,\"taxCode\":{},\"partnerType\":\"Unknown\"}")]
    public async Task LegacySnapshotDoesNotInventCurrentProfileDetails(string snapshot)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var partner = new Account { Email = "legacy-partner@example.test", Role = AccountRole.Partner,
            Profile = new() { FullName = "Current profile", Nickname = "legacy-partner", PartnerType = PartnerType.Company } };
        var reviewedAt = DateTimeOffset.UtcNow;
        db.Accounts.Add(partner);
        db.UserVerifications.Add(new() { User = partner.Profile!, VerificationType = VerificationType.Partner,
            Status = VerificationStatus.Approved, VerificationData = snapshot, ReviewedAt = reviewedAt });
        await db.SaveChangesAsync();
        var result = Json(await new PartnerVerificationService(db).GetMyStatusAsync(partner.Id, default));
        foreach (var key in new[] { "organizationName", "taxCode", "representativeName" })
            Assert.Equal(JsonValueKind.Null, result.GetProperty(key).ValueKind);
        Assert.Equal(reviewedAt, result.GetProperty("reviewedAt").GetDateTimeOffset());
    }
}
