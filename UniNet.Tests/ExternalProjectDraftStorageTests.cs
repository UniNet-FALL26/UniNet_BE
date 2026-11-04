using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ExternalProjectDraftStorageTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public UniNetDbContext Db { get; private set; } = null!;
        public DbContextOptions<UniNetDbContext> Options { get; private set; } = null!;
        public ExternalProjectDraft Draft { get; private set; } = null!;
        public UploadedFile File { get; private set; } = null!;

        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f.connection.OpenAsync();
            f.Options = new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(f.connection).Options;
            f.Db = new(f.Options);
            await f.Db.Database.EnsureCreatedAsync();
            var account = new Account
            {
                Email = "draft-owner@example.test", Role = AccountRole.Student,
                Profile = new UserProfile { FullName = "Draft Owner", Nickname = "owner" }
            };
            f.File = new UploadedFile
            {
                OwnerAccount = account, PublicId = "uninet/evidence/owner/report.pdf",
                Url = "https://example.test/report.pdf", FileName = "report.pdf",
                Format = "pdf", ResourceType = "raw", SizeBytes = 128
            };
            f.Draft = new ExternalProjectDraft
            {
                OwnerUser = account.Profile!,
                ExtractedDataJson = "{\"title\":\"Imported project\"}",
                Files = [new() { UploadedFile = f.File }]
            };
            f.Db.ExternalProjectDrafts.Add(f.Draft);
            await f.Db.SaveChangesAsync();
            return f;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task DraftAndSourceFilesRoundTripWithoutCreatingProject()
    {
        await using var f = await Fixture.Create();
        var id = f.Draft.Id;
        f.Db.ChangeTracker.Clear();
        var draft = await f.Db.ExternalProjectDrafts.Include(x => x.Files)
            .ThenInclude(x => x.UploadedFile).SingleAsync(x => x.Id == id);
        Assert.Equal(ExternalProjectDraftStatus.Processing, draft.Status);
        Assert.Equal(1, draft.Version);
        Assert.Null(draft.ConfirmedProjectId);
        Assert.Null(draft.EditedDataJson);
        Assert.Contains("Imported project", draft.ExtractedDataJson);
        Assert.True(draft.ExpiresAt > draft.CreatedAt);
        Assert.Equal(f.File.Id, Assert.Single(draft.Files).UploadedFile.Id);
        Assert.Empty(await f.Db.Projects.ToListAsync());
    }

    [Fact]
    public async Task VersionRejectsStaleDraftUpdate()
    {
        await using var f = await Fixture.Create();
        await using var other = new UniNetDbContext(f.Options);
        var stale = await other.ExternalProjectDrafts.SingleAsync();
        f.Draft.Status = ExternalProjectDraftStatus.Ready;
        f.Draft.Version++;
        await f.Db.SaveChangesAsync();
        stale.EditedDataJson = "{\"title\":\"Stale edit\"}";
        stale.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task ReferencedUploadCannotBeDeletedButRemovingDraftPreservesUpload()
    {
        await using var f = await Fixture.Create();
        await using (var other = new UniNetDbContext(f.Options))
        {
            other.UploadedFiles.Remove(await other.UploadedFiles.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
        }
        f.Db.ExternalProjectDrafts.Remove(f.Draft);
        await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Db.ExternalProjectDraftFiles.ToListAsync());
        Assert.Single(await f.Db.UploadedFiles.ToListAsync());
    }

    [Fact]
    public async Task ConfirmedDraftRequiresProjectReference()
    {
        await using var f = await Fixture.Create();
        f.Draft.Status = ExternalProjectDraftStatus.Confirmed;
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Db.SaveChangesAsync());
    }
}
