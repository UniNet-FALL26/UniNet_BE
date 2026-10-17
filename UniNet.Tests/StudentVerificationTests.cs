using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.DTOs.Verification;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class StudentVerificationTests
{
    private static readonly SubmitStudentVerificationRequest Request = new("Trịnh Trọng Quyền", "Any College", "SE170347");
    private static readonly StudentVerificationDocument Document = new("card.pdf", "application/pdf", "%PDF-1.7\nprivate"u8.ToArray());

    [Fact]
    public async Task SubmitIsPrivatePendingAndCannotBeDuplicatedOrSelfApproved()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Service.GetMyStatusAsync(f.Student.Id, default);
        Assert.Null(initial.Status);
        var result = await f.Service.SubmitAsync(f.Student.Id, Request, Document, default);
        Assert.Equal(VerificationStatus.Pending, result.Status);
        Assert.False(result.IsVerified);
        Assert.Equal(Request.SchoolName, result.SchoolName);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
        Assert.DoesNotContain("documentBase64", JsonSerializer.Serialize(result));
        Assert.Null((await f.Db.UserVerifications.SingleAsync()).DocumentUrls);
        Assert.Equal(409, (await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request, Document, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() => f.Service.ReviewAsync(f.Student.Id, result.Id!.Value, true, null, default))).Status);
        Assert.Equal(Document.Bytes, (await f.Service.GetDocumentAsync(f.Student.Id, result.Id!.Value, default)).Bytes);
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() => f.Service.GetDocumentAsync(f.Other.Id, result.Id!.Value, default))).Status);
    }

    [Fact]
    public async Task RejectRequiresReasonPreservesHistoryAndAllowsResubmitAfterCooldown()
    {
        await using var f = await Fixture.Create();
        var submitted = await f.Service.SubmitAsync(f.Student.Id, Request, Document, default);
        Assert.Equal(400, (await Assert.ThrowsAsync<AuthException>(() => f.Service.ReviewAsync(f.Admin.Id, submitted.Id!.Value, false, " ", default))).Status);
        var rejected = await f.Service.ReviewAsync(f.Admin.Id, submitted.Id!.Value, false, "Ảnh chưa rõ", default);
        Assert.Equal("Ảnh chưa rõ", rejected.RejectReason);
        Assert.NotNull(rejected.ReviewedAt);
        Assert.Equal(429, (await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request, Document, default))).Status);
        (await f.Db.UserVerifications.SingleAsync()).SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-6);
        await f.Db.SaveChangesAsync();
        var again = await f.Service.SubmitAsync(f.Student.Id, Request with { SchoolName = "Updated College" }, Document, default);
        Assert.NotEqual(submitted.Id, again.Id);
        Assert.Equal(2, await f.Db.UserVerifications.CountAsync());
        var approved = await f.Service.ReviewAsync(f.Admin.Id, again.Id!.Value, true, null, default);
        Assert.True(approved.IsVerified);
        Assert.Equal(VerificationStatus.Approved, (await f.Service.GetMyStatusAsync(f.Student.Id, default)).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request, Document, default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<AuthException>(() => f.Service.ReviewAsync(f.Admin.Id, again.Id!.Value, false, "changed", default))).Status);
    }

    [Theory]
    [InlineData("evil.svg", "image/svg+xml", "<svg>")]
    [InlineData("fake.png", "image/png", "not an image")]
    [InlineData("evil.html", "application/pdf", "%PDF-1.7")]
    public async Task InvalidFileContentOrTypeRejected(string name, string type, string content)
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request,
            new(name, type, System.Text.Encoding.UTF8.GetBytes(content)), default));
        Assert.Empty(await f.Db.UserVerifications.ToListAsync());
    }

    [Fact]
    public async Task ApprovalUsesReviewedIdentityAndInvalidatedProfileCanRequestVerificationAgain()
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.SubmitAsync(f.Student.Id, Request, Document, default);
        f.Student.Profile!.FullName = "Changed after submission";
        await f.Db.SaveChangesAsync();
        await f.Service.ReviewAsync(f.Admin.Id, result.Id!.Value, true, null, default);
        Assert.Equal(Request.FullName, f.Student.Profile.FullName);
        Assert.Equal(Request.SchoolName, f.Student.Profile.UniversityName);
        Assert.Equal(Request.StudentCode, f.Student.Profile.StudentCode);
        f.Student.Profile.IsVerified = false; // Existing ProfileService invalidation after identity edits.
        (await f.Db.UserVerifications.SingleAsync()).SubmittedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        await f.Db.SaveChangesAsync();
        Assert.Null((await f.Service.GetMyStatusAsync(f.Student.Id, default)).Status);
        Assert.Equal(VerificationStatus.Approved, (await f.Service.GetDetailAsync(f.Admin.Id, result.Id!.Value, default)).Status);
        Assert.Equal(VerificationStatus.Pending, (await f.Service.SubmitAsync(f.Student.Id, Request, Document, default)).Status);
        Assert.Equal(2, await f.Db.UserVerifications.CountAsync());
    }

    [Fact]
    public async Task ValidatesSizeRequiredFieldsRolesAndAdminReads()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request with { FullName = " " }, Document, default));
        await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request with { StudentCode = "<script>" }, Document, default));
        await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request with { StudentCode = "AB" }, Document, default));
        await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request with { FullName = "A" }, Document, default));
        await Assert.ThrowsAsync<AuthException>(() => f.Service.SubmitAsync(f.Student.Id, Request, Document with { Bytes = new byte[5 * 1024 * 1024 + 1] }, default));
        f.Student.Role = AccountRole.Partner;
        await f.Db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() => f.Service.GetMyStatusAsync(f.Student.Id, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AuthException>(() => f.Service.ListAsync(f.Other.Id, null, 1, 20, default))).Status);
        Assert.Equal(0, (await f.Service.ListAsync(f.Admin.Id, null, 1, 20, default)).TotalItems);
    }

    [Fact]
    public async Task SupportsStudentCodesContainingSchoolSeparators()
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.SubmitAsync(f.Student.Id, Request with { StudentCode = "SV-2026_001/A" }, Document, default);
        Assert.Equal("SV-2026_001/A", result.StudentCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required UniNetDbContext Db { get; init; }
        public required Account Student { get; init; }
        public required Account Other { get; init; }
        public required Account Admin { get; init; }
        public StudentVerificationService Service => new(Db);
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
                .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
            await db.Database.EnsureCreatedAsync();
            var accounts = new[] { AccountRole.Student, AccountRole.Student, AccountRole.Admin }.Select((role, i) =>
                new Account { Email = $"student-verification-{i}@example.test", Role = role, Profile = new UserProfile { FullName = "Student", Nickname = $"user{i}" } }).ToArray();
            db.Accounts.AddRange(accounts); await db.SaveChangesAsync();
            return new() { Connection = connection, Db = db, Student = accounts[0], Other = accounts[1], Admin = accounts[2] };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
