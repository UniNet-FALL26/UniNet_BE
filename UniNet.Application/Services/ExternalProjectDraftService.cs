using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application.DTOs.Projects;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class ExternalProjectDraftService(UniNetDbContext db, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<UserProfile> Actor(Guid accountId, CancellationToken ct) =>
        await db.UserProfiles.Include(p => p.Account).SingleOrDefaultAsync(p => p.AccountId == accountId &&
            p.Account.Role == AccountRole.Student && p.Account.Status == AccountStatus.Active, ct)
        ?? throw new ProjectException("FORBIDDEN", "An active Student profile is required.", 403);

    private async Task<ExternalProjectDraft> Owned(Guid id, Guid accountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        return await db.ExternalProjectDrafts.Include(d => d.Files).ThenInclude(f => f.UploadedFile)
            .SingleOrDefaultAsync(d => d.Id == id && d.OwnerUserId == actor.Id, ct)
            ?? throw new ProjectException("DRAFT_NOT_FOUND", "Draft not found.", 404);
    }

    public async Task<ExternalProjectDraftResponse> CreateAsync(Guid accountId, CreateExternalProjectDraftRequest request, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        if (request.FileIds == null || request.FileIds.Count is < 1 or > 10 || request.FileIds.Contains(Guid.Empty) ||
            request.FileIds.Distinct().Count() != request.FileIds.Count)
            throw new ProjectException("INVALID_FILE_IDS", "Provide 1 to 10 distinct uploaded file IDs.");
        var files = await db.UploadedFiles.Where(f => request.FileIds.Contains(f.Id) && f.OwnerAccountId == accountId).ToListAsync(ct);
        if (files.Count != request.FileIds.Count) throw new ProjectException("INVALID_FILE_IDS", "One or more uploaded files are unavailable.");
        if (!files.Any(f => f.ResourceType == "raw" && (f.Format == "pdf" || f.Format == "docx" || f.Format == "txt")))
            throw new ProjectException("DOCUMENT_REQUIRED", "Include at least one PDF, DOCX or TXT document.");
        var now = DateTimeOffset.UtcNow;
        var draft = new ExternalProjectDraft
        {
            OwnerUser = actor, CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddDays(7), NextAttemptAt = now,
            Files = files.Select(f => new ExternalProjectDraftFile { UploadedFile = f }).ToList()
        };
        // Processing + NextAttemptAt is a durable queue entry, committed with the draft and attachments.
        db.ExternalProjectDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        return Response(draft);
    }

    public async Task<ExternalProjectDraftResponse> GetAsync(Guid id, Guid accountId, CancellationToken ct)
    {
        var draft = await Owned(id, accountId, ct);
        if (draft.Status is not (ExternalProjectDraftStatus.Confirmed or ExternalProjectDraftStatus.Expired) && draft.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            draft.Status = ExternalProjectDraftStatus.Expired; draft.LeaseToken = null; draft.LeaseExpiresAt = null;
            draft.UpdatedAt = DateTimeOffset.UtcNow; draft.Version++;
            await Save(ct);
        }
        return Response(draft);
    }

    public async Task<ExternalProjectDraftResponse> UpdateAsync(Guid id, Guid accountId, UpdateExternalProjectDraftRequest request, CancellationToken ct)
    {
        var draft = await Owned(id, accountId, ct);
        Editable(draft, request.Version);
        if (request.Data == null) throw new ProjectException("INVALID_DRAFT_DATA", "Provide the editable data object.");
        var data = ExternalProjectDraftValidation.Validate(request.Data);
        await ValidateReferences(draft, data, accountId, ct);
        draft.EditedDataJson = JsonSerializer.Serialize(data, Json);
        draft.Status = ExternalProjectDraftStatus.Ready;
        draft.UpdatedAt = DateTimeOffset.UtcNow; draft.Version++;
        await Save(ct);
        return Response(draft);
    }

    public async Task<ConfirmExternalProjectDraftResponse> ConfirmAsync(Guid id, Guid accountId, ConfirmExternalProjectDraftRequest request, CancellationToken ct)
    {
        try { return await ConfirmCore(id, accountId, request, ct); }
        catch (Exception ex) when (EvidenceModerationSource.IsWriteConflict(ex))
        {
            db.ChangeTracker.Clear();
            var draft = await Owned(id, accountId, ct);
            if (draft.Status == ExternalProjectDraftStatus.Confirmed) return await Confirmed(draft, ct);
            throw new ProjectException("DRAFT_CONFLICT", "Draft changed concurrently. Reload before retrying.", 409);
        }
    }

    private async Task<ConfirmExternalProjectDraftResponse> ConfirmCore(Guid id, Guid accountId, ConfirmExternalProjectDraftRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var draft = await Owned(id, accountId, ct);
        if (draft.Status == ExternalProjectDraftStatus.Confirmed) return await Confirmed(draft, ct);
        Editable(draft, request.Version);
        if (!request.ConfirmSharingRights) throw new ProjectException("SHARING_RIGHTS_REQUIRED", "Confirm your right to share the project and evidence for assessment.");
        var data = ExternalProjectDraftValidation.Validate(Data(draft));
        var missing = ExternalProjectDraftValidation.MissingFields(data);
        if (missing.Count > 0) throw new ProjectException("DRAFT_INCOMPLETE", "Missing fields: " + string.Join(", ", missing));
        await ValidateReferences(draft, data, accountId, ct);
        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            CreatorId = draft.OwnerUserId, Origin = ProjectOrigin.External,
            Title = data.Title!, Description = data.Description!, ProjectField = data.ProjectField!,
            ImplementationStartDate = data.StartDate, ImplementationEndDate = data.EndDate,
            SubmitterRole = data.SubmitterRole, SubmitterContribution = data.SubmitterContribution,
            SubmitterIsLeader = data.SubmitterIsLeader!.Value, ExternalLeaderName = data.SubmitterIsLeader.Value ? null : data.LeaderName,
            ExternalDetailsJson = JsonSerializer.Serialize(data, Json), SharingRightsConfirmedAt = now,
            Status = ProjectStatus.Completed, RecruitmentStatus = RecruitmentStatus.Closed, Visibility = ProjectVisibility.Private,
            MemberTarget = null, MinimumQuantity = null, RecruitmentDeadline = null, ExpectedOutput = null,
            CreatedAt = now, UpdatedAt = now, CompletedAt = now,
            ProjectSkills = data.SkillIds.Select(s => new ProjectSkill { SkillId = s }).ToList(),
            EvidenceDocuments = data.EvidenceFileIds.Select(f => new ProjectEvidenceDocument { UploadedFileId = f, CreatedAt = now }).ToList()
        };
        db.Projects.Add(project);
        draft.Status = ExternalProjectDraftStatus.Confirmed; draft.ConfirmedProject = project;
        draft.Version++; draft.UpdatedAt = now; draft.LeaseToken = null; draft.LeaseExpiresAt = null;
        await db.SaveChangesAsync(ct);
        var moderation = await new EvidenceModerationSource(db, configuration).RefreshAsync(project.Id, now, ct, createInitial: true);
        await transaction.CommitAsync(ct);
        return new(draft.Id, project.Id, moderation!.Id, draft.Version);
    }

    private async Task<ConfirmExternalProjectDraftResponse> Confirmed(ExternalProjectDraft draft, CancellationToken ct)
    {
        var reviewId = await db.ProjectModerations.Where(m => m.ProjectId == draft.ConfirmedProjectId &&
            m.Purpose == ProjectModerationPurpose.ProjectEvidence).OrderBy(m => m.Version).Select(m => m.Id).FirstAsync(ct);
        return new(draft.Id, draft.ConfirmedProjectId!.Value, reviewId, draft.Version);
    }

    private async Task ValidateReferences(ExternalProjectDraft draft, ExternalProjectDraftData data, Guid accountId, CancellationToken ct)
    {
        if (data.EvidenceFileIds.Any(id => !draft.Files.Any(f => f.UploadedFileId == id && f.UploadedFile.OwnerAccountId == accountId)))
            throw new ProjectException("INVALID_EVIDENCE_FILES", "Evidence must be selected from this draft's owned uploads.");
        if (await db.Skills.CountAsync(s => data.SkillIds.Contains(s.Id) && s.IsActive, ct) != data.SkillIds.Count)
            throw new ProjectException("INVALID_SKILL_IDS", "Select active catalog skills.");
    }

    private static void Editable(ExternalProjectDraft draft, int version)
    {
        if (draft.Status == ExternalProjectDraftStatus.Expired || draft.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ProjectException("DRAFT_EXPIRED", "Draft has expired.", 410);
        if (draft.Status is not (ExternalProjectDraftStatus.Ready or ExternalProjectDraftStatus.Failed))
            throw new ProjectException("DRAFT_LOCKED", "Wait for extraction to finish; confirmed drafts cannot be edited.", 409);
        if (version < 1 || version != draft.Version)
            throw new ProjectException("DRAFT_VERSION_CONFLICT", "Draft version is stale. Reload before editing.", 409);
    }

    private async Task Save(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ProjectException("DRAFT_VERSION_CONFLICT", "Draft changed concurrently. Reload before editing.", 409); }
    }

    private static ProjectDraftExtractionResult? Extraction(ExternalProjectDraft d) => d.ExtractedDataJson == null ? null :
        JsonSerializer.Deserialize<ProjectDraftExtractionResult>(d.ExtractedDataJson, Json);

    private static ExternalProjectDraftData Data(ExternalProjectDraft d)
    {
        if (d.EditedDataJson != null) return JsonSerializer.Deserialize<ExternalProjectDraftData>(d.EditedDataJson, Json)!;
        var extraction = Extraction(d)?.Information;
        return new()
        {
            Title = extraction?.Title?.Value, Description = extraction?.Description?.Value, ProjectField = extraction?.ProjectField?.Value,
            StartDate = extraction?.StartDate?.Value, EndDate = extraction?.EndDate?.Value,
            RepositoryUrl = extraction?.RepositoryUrl?.Value, DemoUrl = extraction?.DemoUrl?.Value,
            SkillIds = extraction?.Skills.Where(s => s.SkillId.HasValue).Select(s => s.SkillId!.Value).Distinct().ToList() ?? [],
            EvidenceFileIds = d.Files.Select(f => f.UploadedFileId).ToList(),
            Features = extraction?.Features.Select(f => f.Value).ToList() ?? [],
            Members = extraction?.Members.Select(m => new DeclaredProjectMember(m.Name.Value, m.Role?.Value)).ToList() ?? []
        };
    }

    private static ExternalProjectDraftResponse Response(ExternalProjectDraft d)
    {
        var data = Data(d);
        return new(d.Id, d.Status, d.Version, d.CreatedAt, d.UpdatedAt, d.ExpiresAt, d.ErrorCode, d.ConfirmedProjectId,
            data, Extraction(d), ExternalProjectDraftValidation.MissingFields(data), d.Files.OrderBy(f => f.UploadedFileId)
                .Select(f => new DraftUploadedFileResponse(f.UploadedFileId, f.UploadedFile.FileName, f.UploadedFile.Url,
                    f.UploadedFile.Format, f.UploadedFile.ResourceType, f.UploadedFile.SizeBytes)).ToList());
    }
}
