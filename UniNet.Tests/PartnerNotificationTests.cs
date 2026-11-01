using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application.DTOs.Verification;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PartnerNotificationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PartnerVerificationNotifiesEligibleStaffAndOwnerUsingAccountIds(bool approve)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var partner = new Account { Email = "partner-notice@example.test", Role = AccountRole.Partner,
            Profile = new UserProfile { FullName = "Partner", Nickname = "partner-notice", OrganizationName = "UniNet Partner", PartnerType = PartnerType.Company } };
        var admin = new Account { Email = "admin-notice@example.test", Role = AccountRole.Admin,
            Profile = new UserProfile { FullName = "Admin", Nickname = "admin-notice" } };
        var moderator = new Account { Email = "moderator-notice@example.test", Role = AccountRole.Moderator,
            Profile = new UserProfile { FullName = "Moderator", Nickname = "moderator-notice" } };
        db.Accounts.AddRange(partner, admin, moderator);
        db.Set<NotificationPermissionOverride>().Add(new() { AccountId = admin.Id,
            Permission = NotificationPermissionPolicy.StudentVerificationReview, IsGranted = false });
        await db.SaveChangesAsync();
        var service = new PartnerVerificationService(db);
        var submitted = await service.SubmitAsync(partner.Id,
            new SubmitPartnerVerificationRequest("1234567890", "Representative",
                [new(VerificationDocumentType.BusinessRegistration, "registration.pdf", "https://example.test/registration.pdf")]), default);
        var staffNotice = await db.Set<VerificationNotification>().SingleAsync();
        Assert.Equal(admin.Id, staffNotice.RecipientUserId);
        Assert.Equal(NotificationPermissionPolicy.PartnerVerificationReview, staffNotice.RequiredPermission);
        Assert.Equal(submitted.Id, staffNotice.TargetId);
        Assert.Equal($"/admin/partner-verifications/{submitted.Id}", await new NotificationTargets(db, new NotificationService(db))
            .ResolveAsync(admin.Id, staffNotice.Id, default));
        Assert.Empty(await db.Set<VerificationNotification>().Where(n => n.RecipientUserId == moderator.Id).ToListAsync());
        if (approve) await service.ApproveAsync(admin.Id, submitted.Id!.Value, default);
        else await service.RejectAsync(admin.Id, submitted.Id!.Value, "Tài liệu không rõ", default);
        var ownerNotice = await db.Set<VerificationNotification>().SingleAsync(n => n.RecipientUserId == partner.Id);
        Assert.NotEqual(partner.Profile!.Id, ownerNotice.RecipientUserId);
        Assert.Equal("UserVerification", ownerNotice.TargetType);
        Assert.Equal(submitted.Id, ownerNotice.TargetId);
        Assert.Equal(approve ? "verification.partner.approved" : "verification.partner.rejected", ownerNotice.Type);
        if (!approve) Assert.Contains("Tài liệu không rõ", ownerNotice.Message);
    }
}
