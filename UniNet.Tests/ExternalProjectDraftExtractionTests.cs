using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ExternalProjectDraftExtractionTests
{
    private sealed class Content : IUploadedDocumentContentReader
    {
        public int Calls;
        public Task<byte[]> ReadAsync(UploadedFile file, CancellationToken ct = default)
        { Calls++; return Task.FromResult(Encoding.UTF8.GetBytes("UniNet uses C# and UnknownTool. Nguyễn An is Backend Developer.")); }
    }
    private sealed class Extractor : IProjectInformationExtractor
    {
        public string Model => "gemini-3.1-flash-lite";
        public int Calls;
        public ProjectExtractionException? Error;
        public bool Cancel;
        public bool InvalidCitation;
        public Task<ProjectInformationExtraction> ExtractAsync(IReadOnlyList<ProjectDocumentText> documents, CancellationToken ct = default)
        {
            Calls++;
            if (Cancel) throw new OperationCanceledException();
            if (Error != null) throw Error;
            var d = documents[0];
            SourcedProjectText Field(string value) => new(value, [new(InvalidCitation ? Guid.NewGuid() : d.FileId, 1, d.Segments[0].Text)]);
            return Task.FromResult(new ProjectInformationExtraction
            {
                Title = Field("UniNet"), Skills = [new(Field("C#")), new(Field("UnknownTool"))],
                Members = [new(Field("Nguyễn An"), Field("Backend Developer"))]
            });
        }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public UniNetDbContext Db { get; private set; } = null!;
        public Account Account { get; private set; } = null!;
        public ExternalProjectDraft Draft { get; private set; } = null!;
        public Content Content { get; } = new();
        public Extractor Extractor { get; } = new();
        public Guid SkillId { get; private set; }
        public ExternalProjectDraftExtractionService Service => new(Db, Content, new ProjectDocumentTextReader(), Extractor);

        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await f.connection.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(f.connection).Options);
            await f.Db.Database.EnsureCreatedAsync();
            f.Account = new() { Email = "owner@example.test", Role = AccountRole.Student,
                Profile = new UserProfile { FullName = "Owner", Nickname = "owner" } };
            var skill = new Skill { Name = "C#" }; f.SkillId = skill.Id;
            f.Db.Skills.AddRange(skill, new Skill { Name = "UnknownTool", IsActive = false });
            f.Draft = new()
            {
                OwnerUser = f.Account.Profile!, EditedDataJson = "{\"title\":\"User edit\"}",
                Files = [new() { UploadedFile = new()
                {
                    OwnerAccount = f.Account, FileName = "source.txt", Format = "txt", ResourceType = "raw",
                    PublicId = "source.txt", Url = "https://example.test/source.txt", SizeBytes = 60
                } }]
            };
            f.Db.ExternalProjectDrafts.Add(f.Draft);
            await f.Db.SaveChangesAsync();
            return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    [Fact]
    public async Task ExtractionPersistsSourcedDraftWithoutCreatingProjectsOrMembers()
    {
        await using var f = await Fixture.Create();
        var result = await f.Service.ExtractAsync(f.Draft.Id, f.Account.Id);
        Assert.Equal(f.SkillId, result.Information.Skills[0].SkillId);
        Assert.Null(result.Information.Skills[1].SkillId);
        Assert.Single(result.Information.Members);
        Assert.Equal(64, Assert.Single(result.Documents).Sha256.Length);
        Assert.Equal("gemini-3.1-flash-lite", result.Model);
        var draftId = f.Draft.Id;
        f.Db.ChangeTracker.Clear();
        var stored = await f.Db.ExternalProjectDrafts.SingleAsync(d => d.Id == draftId);
        Assert.Equal(ExternalProjectDraftStatus.Ready, stored.Status);
        Assert.Equal(2, stored.Version);
        Assert.Equal("{\"title\":\"User edit\"}", stored.EditedDataJson);
        var decoded = JsonSerializer.Deserialize<ProjectDraftExtractionResult>(stored.ExtractedDataJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(result.Information.Title!.Value, decoded.Information.Title!.Value);
        Assert.Equal(result.Information.Title.Sources, decoded.Information.Title.Sources);
        Assert.Empty(await f.Db.Projects.ToListAsync());
        Assert.Empty(await f.Db.ProjectMembers.ToListAsync());
    }

    [Fact]
    public async Task OtherAccountCannotReadDraftOrTriggerProviders()
    {
        await using var f = await Fixture.Create();
        var error = await Assert.ThrowsAsync<ProjectExtractionException>(() => f.Service.ExtractAsync(f.Draft.Id, Guid.NewGuid()));
        Assert.Equal("EXTRACTION_DRAFT_NOT_FOUND", error.Code);
        Assert.Equal(0, f.Content.Calls);
        Assert.Equal(0, f.Extractor.Calls);
    }

    [Fact]
    public async Task ForeignOwnedAttachmentFailsBeforeAnyDownload()
    {
        await using var f = await Fixture.Create();
        var other = new Account { Email = "other@example.test", Role = AccountRole.Student };
        f.Db.Accounts.Add(other);
        f.Draft.Files[0].UploadedFile.OwnerAccount = other;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ProjectExtractionException>(() => f.Service.ExtractAsync(f.Draft.Id, f.Account.Id));
        Assert.Equal("EXTRACTION_FILE_FORBIDDEN", error.Code);
        Assert.Equal(ExternalProjectDraftStatus.Failed, f.Draft.Status);
        Assert.Equal(0, f.Content.Calls);
        Assert.Equal(0, f.Extractor.Calls);
    }

    [Fact]
    public async Task ProviderFailureIsRecordedWithoutReplacingUserEdits()
    {
        await using var f = await Fixture.Create();
        f.Extractor.Error = new("EXTRACTION_AI_FAILED", "Fixture provider unavailable", true);
        await Assert.ThrowsAsync<ProjectExtractionException>(() => f.Service.ExtractAsync(f.Draft.Id, f.Account.Id));
        Assert.Equal(ExternalProjectDraftStatus.Failed, f.Draft.Status);
        Assert.Equal("EXTRACTION_AI_FAILED", f.Draft.ErrorCode);
        Assert.Null(f.Draft.ExtractedDataJson);
        Assert.Contains("User edit", f.Draft.EditedDataJson);
    }

    [Fact]
    public async Task InvalidAiCitationFailsBeforeSavingExtractedInformation()
    {
        await using var f = await Fixture.Create();
        f.Extractor.InvalidCitation = true;
        await Assert.ThrowsAsync<ProjectExtractionException>(() => f.Service.ExtractAsync(f.Draft.Id, f.Account.Id));
        Assert.Equal("EXTRACTION_INVALID_RESPONSE", f.Draft.ErrorCode);
        Assert.Null(f.Draft.ExtractedDataJson);
    }

    [Fact]
    public async Task UnsupportedAttachmentIsKeptAsWarningAlongsideReadableSource()
    {
        await using var f = await Fixture.Create();
        var image = new UploadedFile { OwnerAccount = f.Account, PublicId = "picture", Url = "https://example.test/picture",
            FileName = "picture.png", Format = "png", ResourceType = "image", SizeBytes = 50 };
        f.Db.UploadedFiles.Add(image);
        f.Draft.Files.Add(new() { UploadedFile = image });
        await f.Db.SaveChangesAsync();
        var result = await f.Service.ExtractAsync(f.Draft.Id, f.Account.Id);
        Assert.Equal(image.Id, Assert.Single(result.Warnings).FileId);
        Assert.Single(result.Documents);
        Assert.Equal(1, f.Content.Calls);
    }

    [Fact]
    public async Task ExpiredDraftDoesNotTriggerExtraction()
    {
        await using var f = await Fixture.Create();
        f.Draft.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<ProjectExtractionException>(() => f.Service.ExtractAsync(f.Draft.Id, f.Account.Id));
        Assert.Equal("EXTRACTION_DRAFT_EXPIRED", error.Code);
        Assert.Equal(ExternalProjectDraftStatus.Expired, f.Draft.Status);
        Assert.Equal(0, f.Content.Calls);
    }

    [Fact]
    public async Task CancellationDoesNotConvertDraftToFailed()
    {
        await using var f = await Fixture.Create();
        f.Extractor.Cancel = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ExtractAsync(f.Draft.Id, f.Account.Id));
        Assert.Equal(ExternalProjectDraftStatus.Processing, f.Draft.Status);
        Assert.Null(f.Draft.ErrorCode);
        Assert.Equal(1, f.Draft.Version);
    }
}
