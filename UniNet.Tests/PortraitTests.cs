using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.API.Services;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PortraitTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
    private sealed class Processor : IPortraitProcessor
    {
        public int Calls;
        public Task<byte[]> Process(byte[] source, CancellationToken ct) { Calls++; return Task.FromResult(Png); }
    }
    private sealed class Cloud : IPortraitCloudStorage
    {
        public int Calls; public bool Fail; public List<string> Ids = []; public Action? AfterUpload;
        public Task<string> Upload(byte[] png, string id, CancellationToken ct)
        {
            Calls++; Ids.Add(id); Assert.Equal(Png, png);
            if (Fail) throw new AuthException("PORTRAIT_UPLOAD_FAILED", "Fixture failed", 502);
            AfterUpload?.Invoke();
            return Task.FromResult($"https://res.cloudinary.com/fixture/image/upload/v1/{id}.png");
        }
        public Task Destroy(string id, CancellationToken ct) => Task.CompletedTask;
    }
    private static FormFile File() => new(new MemoryStream(Png), 0, Png.Length, "file", "portrait.png");
    private static async Task<(SqliteConnection, UniNetDbContext, Account)> Setup()
    {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = "portrait-fixture@example.org", Role = AccountRole.Student, Profile = new() { FullName = "Owner", Nickname = "Owner", AvatarUrl = "https://example.org/old.png" } };
        db.Accounts.Add(owner); await db.SaveChangesAsync(); return (connection, db, owner);
    }
    [Fact]
    public async Task ProcessingNeverUploadsAndConfirmationDoesNotChangeProfileUntilSave()
    {
        var (connection, db, owner) = await Setup(); await using var c = connection; await using var d = db;
        var processor = new Processor(); var cloud = new Cloud(); var sessions = new PortraitSessions(processor);
        var preview = await sessions.Process(owner.Id, File(), default);
        Assert.Equal(1, processor.Calls); Assert.Equal(0, cloud.Calls); Assert.Empty(await db.PortraitAssets.ToListAsync());
        Assert.StartsWith("data:image/png;base64,", preview.PreviewUrl); Assert.True(preview.HasTransparentBackground);
        var uploaded = await sessions.Confirm(owner.Id, Guid.Parse(preview.SessionId), db, cloud, default);
        Assert.Equal(1, cloud.Calls); Assert.True(uploaded.HasTransparentBackground);
        db.ChangeTracker.Clear(); Assert.Equal("https://example.org/old.png", (await db.UserProfiles.SingleAsync()).AvatarUrl);
        Assert.Equal(0, (await db.PortraitAssets.SingleAsync()).State);
        var portfolio = new PortfolioService(db); var draft = await portfolio.Me(owner.Id, default);
        await portfolio.SaveEditor(owner.Id, new(new("Owner", "Owner", uploaded.ImageUrl, null, null), draft.Portfolio), default);
        db.ChangeTracker.Clear();
        Assert.Equal(uploaded.ImageUrl, (await portfolio.Me(owner.Id, default)).Profile.AvatarUrl);
        Assert.Equal(1, (await db.PortraitAssets.SingleAsync()).State);
        var restart = new PortraitSessions(processor);
        Assert.Equal(uploaded, await restart.Confirm(owner.Id, Guid.Parse(preview.SessionId), db, cloud, default)); Assert.Equal(1, cloud.Calls);
    }
    [Fact]
    public async Task SessionsAndAssetsAreBoundToOwnerAndCancellationChangesNothing()
    {
        var (connection, db, owner) = await Setup(); await using var c = connection; await using var d = db;
        var sessions = new PortraitSessions(new Processor()); var cloud = new Cloud();
        var preview = await sessions.Process(owner.Id, File(), default); var id = Guid.Parse(preview.SessionId);
        await Assert.ThrowsAsync<AuthException>(() => sessions.Confirm(Guid.NewGuid(), id, db, cloud, default)); Assert.Equal(0, cloud.Calls);
        sessions.Release(Guid.NewGuid(), id); // Cannot cancel another account's result.
        var uploaded = await sessions.Confirm(owner.Id, id, db, cloud, default);
        await Assert.ThrowsAsync<AuthException>(() => PortraitAssetPolicy.Retain(db, Guid.NewGuid(), uploaded.ImageUrl, null, default));
        var cancelled = await sessions.Process(owner.Id, File(), default); sessions.Release(owner.Id, Guid.Parse(cancelled.SessionId));
        await Assert.ThrowsAsync<AuthException>(() => sessions.Confirm(owner.Id, Guid.Parse(cancelled.SessionId), db, cloud, default));
        Assert.Equal("https://example.org/old.png", (await db.UserProfiles.SingleAsync()).AvatarUrl);
    }
    [Fact]
    public async Task FailedUploadRetriesSamePublicIdAndFailedProfileSaveDoesNotRetainAsset()
    {
        var (connection, db, owner) = await Setup(); await using var c = connection; await using var d = db;
        var sessions = new PortraitSessions(new Processor()); var cloud = new Cloud { Fail = true };
        var preview = await sessions.Process(owner.Id, File(), default); var id = Guid.Parse(preview.SessionId);
        await Assert.ThrowsAsync<AuthException>(() => sessions.Confirm(owner.Id, id, db, cloud, default)); Assert.Equal(3, (await db.PortraitAssets.SingleAsync()).State);
        cloud.Fail = false; var uploaded = await sessions.Confirm(owner.Id, id, db, cloud, default);
        Assert.Equal(cloud.Ids[0], cloud.Ids[1]); Assert.Equal(uploaded, await sessions.Confirm(owner.Id, id, db, cloud, default)); Assert.Equal(2, cloud.Calls);
        var service = new PortfolioService(db); var draft = await service.Me(owner.Id, default);
        await Assert.ThrowsAsync<AuthException>(() => service.SaveEditor(owner.Id, new(new("Owner", "Owner", uploaded.ImageUrl, null, null), draft.Portfolio with { Appearance = new() { Profile = new() { Theme = "invalid" } } }), default));
        db.ChangeTracker.Clear(); Assert.Equal(0, (await db.PortraitAssets.SingleAsync()).State); Assert.Equal("https://example.org/old.png", (await db.UserProfiles.SingleAsync()).AvatarUrl);
    }
    [Fact]
    public async Task UnknownAndCleanupPendingCloudinaryUrlsAreRejectedAtomically()
    {
        var (connection, db, owner) = await Setup(); await using var c = connection; await using var d = db;
        var service = new PortfolioService(db); var draft = await service.Me(owner.Id, default);
        var forged = "https://res.cloudinary.com/fixture/image/upload/v1/uninet/portfolio/portraits/forged.png";
        await Assert.ThrowsAsync<AuthException>(() => service.SaveEditor(owner.Id, new(new("Must not save", "Owner", forged, null, null), draft.Portfolio), default));
        await Assert.ThrowsAsync<AuthException>(() => service.SaveEditor(owner.Id, new(new("Must not save", "Owner", null, forged, null), draft.Portfolio), default));
        Assert.Equal("Owner", (await db.UserProfiles.SingleAsync()).FullName);
        db.PortraitAssets.Add(new() { Id = Guid.NewGuid(), AccountId = owner.Id, PublicId = "pending", ImageUrl = forged, State = 2 }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AuthException>(() => service.SaveEditor(owner.Id, new(new("Owner", "Owner", forged, null, null), draft.Portfolio), default));
    }
    [Fact]
    public async Task InvalidImageIsRejectedBeforeModel()
    {
        var processor = new Processor(); var sessions = new PortraitSessions(processor);
        var bytes = "not an image"u8.ToArray(); var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "photo.png");
        await Assert.ThrowsAsync<AuthException>(() => sessions.Process(Guid.NewGuid(), file, default)); Assert.Equal(0, processor.Calls);
    }

    [Fact]
    public async Task ConcurrentCleanupAndSaveCannotBothWin()
    {
        var (connection, db, owner) = await Setup(); await using var c = connection; await using var d = db;
        var sessions = new PortraitSessions(new Processor()); var preview = await sessions.Process(owner.Id, File(), default);
        var uploaded = await sessions.Confirm(owner.Id, Guid.Parse(preview.SessionId), db, new Cloud(), default);
        await using var cleanupDb = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        var cleanupAsset = await cleanupDb.PortraitAssets.SingleAsync();
        await PortraitAssetPolicy.Retain(db, owner.Id, uploaded.ImageUrl, null, default); await db.SaveChangesAsync();
        cleanupAsset.State = 2;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => cleanupDb.SaveChangesAsync());
        db.ChangeTracker.Clear(); Assert.Equal(1, (await db.PortraitAssets.SingleAsync()).State);
    }

    [Fact]
    public async Task RemoteSuccessFollowedByCancellationLeavesDurableCleanupIntent()
    {
        var (connection, db, owner) = await Setup(); await using var c = connection; await using var d = db;
        var sessions = new PortraitSessions(new Processor()); var preview = await sessions.Process(owner.Id, File(), default);
        using var cancellation = new CancellationTokenSource(); var cloud = new Cloud { AfterUpload = () => cancellation.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sessions.Confirm(owner.Id, Guid.Parse(preview.SessionId), db, cloud, cancellation.Token));
        db.ChangeTracker.Clear(); var intent = await db.PortraitAssets.SingleAsync();
        Assert.Equal(3, intent.State); Assert.Equal(cloud.Ids.Single(), intent.PublicId);
        Assert.Equal("https://example.org/old.png", (await db.UserProfiles.SingleAsync()).AvatarUrl);
        var restart = new PortraitSessions(new Processor());
        await Assert.ThrowsAsync<AuthException>(() => restart.Confirm(owner.Id, intent.Id, db, cloud, default));
        Assert.Equal(3, (await db.PortraitAssets.SingleAsync()).State); // Cleanup can destroy public ID after restart.
    }
}
