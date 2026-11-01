using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class AdminReviewConcurrencyTests
{
    [Fact]
    public async Task CompetingPartnerDecisionsHaveOneWinnerOneNotificationAndNoDuplicateAccount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"uninet-partner-admin-{Guid.NewGuid():N}.sqlite");
        UniNetDbContext Open() => new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite($"Data Source={path};Default Timeout=10")
            .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        try {
            Guid adminId, partnerId, verificationId;
            await using (var db = Open()) {
                await db.Database.EnsureCreatedAsync();
                var admin = new Account { Email = "admin-concurrent@test.test", Role = AccountRole.Admin, Profile = new() { FullName = "Admin", Nickname = "Admin" } };
                var partner = new Account { Email = "partner-concurrent@test.test", Role = AccountRole.Partner, Profile = new() { FullName = "Training Center", Nickname = "Center", PartnerType = PartnerType.TrainingCenter } };
                db.Accounts.AddRange(admin, partner); await db.SaveChangesAsync();
                var row = new UserVerification { UserId = partner.Profile!.Id, VerificationType = VerificationType.Partner, VerificationData = "{}", DocumentUrls = "[]" };
                db.UserVerifications.Add(row); await db.SaveChangesAsync();
                adminId = admin.Id; partnerId = partner.Id; verificationId = row.Id;
            }
            async Task<bool> Decide(bool approve) {
                await using var db = Open();
                try { if (approve) await new PartnerVerificationService(db).ApproveAsync(adminId, verificationId, default); else await new PartnerVerificationService(db).RejectAsync(adminId, verificationId, "Invalid document", default); return true; }
                catch (AuthException error) when (error.Status == 409) { return false; }
            }
            var results = await Task.WhenAll(Task.Run(() => Decide(true)), Task.Run(() => Decide(false)));
            Assert.Single(results, won => won); Assert.Single(results, won => !won);
            await using var check = Open();
            var record = await check.UserVerifications.Include(v => v.User).SingleAsync();
            Assert.Equal(record.Status == VerificationStatus.Approved, record.User.IsVerified);
            Assert.Equal(adminId, record.ReviewedBy); Assert.NotNull(record.ReviewedAt);
            Assert.Equal(2, await check.Accounts.CountAsync());
            var notification = Assert.Single(await check.Set<VerificationNotification>().ToListAsync());
            Assert.Equal(partnerId, notification.RecipientUserId); Assert.Equal(verificationId, notification.TargetId);
            Assert.Single(await check.Set<NotificationDelivery>().ToListAsync());
        } finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task PermissionBatchRollsBackCompletelyWhenAnyGrantIsInvalid()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        var admin = new Account { Email = "admin-permission@test.test", Role = AccountRole.Admin };
        var staff = new Account { Email = "staff-permission@test.test", Role = AccountRole.Moderator };
        db.Accounts.AddRange(admin, staff); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(() => new AdminPermissionService(db).SetManyAsync(admin.Id, staff.Id,
            [new(NotificationPermissionPolicy.UsersView, true), new(NotificationPermissionPolicy.PermissionsManage, true)], default));
        Assert.Empty(await db.Set<NotificationPermissionOverride>().AsNoTracking().ToListAsync());
    }
}
