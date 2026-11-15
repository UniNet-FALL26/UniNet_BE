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

public sealed class ExternalProjectDraftService(UniNetDbContext db)
{
    public ExternalProjectDraftService(UniNetDbContext db, IConfiguration configuration) : this(db) { }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<UserProfile> Actor(Guid accountId, CancellationToken ct) =>
        await db.UserProfiles.Include(p => p.Account).SingleOrDefaultAsync(p => p.AccountId == accountId &&
            p.Account.Role == AccountRole.Student && p.Account.Status == AccountStatus.Active, ct)
        ?? throw new ProjectException("FORBIDDEN", "An active Student profile is required.", 403);

    private async Task<ExternalProjectDraft> Owned(Guid id, Guid accountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        return await db.ExternalProjectDrafts
            .SingleOrDefaultAsync(d => d.Id == id && d.OwnerUserId == actor.Id, ct)
            ?? throw new ProjectException("DRAFT_NOT_FOUND", "Draft not found.", 404);
    }

    public async Task<ExternalProjectDraftResponse> CreateAsync(Guid accountId, CancellationToken ct)
    {
        var actor = await Actor(accountId, ct);
        var now = DateTimeOffset.UtcNow;
        var draft = new ExternalProjectDraft
        {
            OwnerUser = actor, CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddDays(7)
        };
        db.ExternalProjectDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        return Response(draft);
    }

    public async Task<ExternalProjectDraftResponse> GetAsync(Guid id, Guid accountId, CancellationToken ct)
    {
        var draft = await Owned(id, accountId, ct);
        if (draft.Status is not (ExternalProjectDraftStatus.Confirmed or ExternalProjectDraftStatus.Expired) && draft.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            draft.Status = ExternalProjectDraftStatus.Expired;
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
        await SpecializationService.ValidateProjectAsync(db, data.SpecializationId, ct);
        await ValidateSkills(data, ct);
        draft.EditedDataJson = JsonSerializer.Serialize(data, Json);
        draft.Status = ExternalProjectDraftStatus.Ready;
        draft.UpdatedAt = DateTimeOffset.UtcNow; draft.Version++;
        await Save(ct);
        return Response(draft);
    }

    public async Task<ConfirmExternalProjectDraftResponse> ConfirmAsync(Guid id, Guid accountId, ConfirmExternalProjectDraftRequest request, CancellationToken ct)
    {
        try { return await ConfirmCore(id, accountId, request, ct); }
        catch (Exception ex) when (DatabaseWriteConflicts.IsWriteConflict(ex))
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
        if (!request.ConfirmSharingRights) throw new ProjectException("SHARING_RIGHTS_REQUIRED", "Confirm your right to share the project information for assessment.");
        var data = ExternalProjectDraftValidation.Validate(Data(draft));
        var missing = ExternalProjectDraftValidation.MissingFields(data);
        if (missing.Count > 0) throw new ProjectException("DRAFT_INCOMPLETE", "Missing fields: " + string.Join(", ", missing));
        await ValidateSkills(data, ct);
        var now = DateTimeOffset.UtcNow;
        var specialization = await SpecializationService.ValidateProjectAsync(db, data.SpecializationId, ct, required: true);
        var project = new Project
        {
            CreatorId = draft.OwnerUserId, Origin = ProjectOrigin.External,
            Title = data.Title!, Description = data.Description!, ProjectField = data.ProjectField!,
            SpecializationId = specialization!.Id, Specialization = specialization,
            ImplementationStartDate = data.StartDate, ImplementationEndDate = data.EndDate,
            SubmitterRole = data.SubmitterRole, SubmitterContribution = data.SubmitterContribution,
            SubmitterIsLeader = data.SubmitterIsLeader!.Value, ExternalLeaderName = data.SubmitterIsLeader.Value ? null : data.LeaderName,
            ExternalDetailsJson = JsonSerializer.Serialize(data, Json), SharingRightsConfirmedAt = now,
            Status = ProjectStatus.Completed, RecruitmentStatus = RecruitmentStatus.Closed, Visibility = ProjectVisibility.Private,
            MemberTarget = null, MinimumQuantity = null, RecruitmentDeadline = null, ExpectedOutput = null,
            CreatedAt = now, UpdatedAt = now, CompletedAt = now,
            ProjectSkills = data.SkillIds.Select(s => new ProjectSkill { SkillId = s }).ToList()
        };
        db.Projects.Add(project);
        draft.Status = ExternalProjectDraftStatus.Confirmed; draft.ConfirmedProject = project;
        draft.Version++; draft.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(draft.Id, project.Id, draft.Version);
    }

    private static Task<ConfirmExternalProjectDraftResponse> Confirmed(ExternalProjectDraft draft, CancellationToken ct) =>
        Task.FromResult(new ConfirmExternalProjectDraftResponse(draft.Id, draft.ConfirmedProjectId!.Value, draft.Version));

    private async Task ValidateSkills(ExternalProjectDraftData data, CancellationToken ct)
    {
        if (await db.Skills.CountAsync(s => data.SkillIds.Contains(s.Id) && s.IsActive, ct) != data.SkillIds.Count)
            throw new ProjectException("INVALID_SKILL_IDS", "Select active catalog skills.");
    }

    private static void Editable(ExternalProjectDraft draft, int version)
    {
        if (draft.Status == ExternalProjectDraftStatus.Expired || draft.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ProjectException("DRAFT_EXPIRED", "Draft has expired.", 410);
        if (draft.Status is not (ExternalProjectDraftStatus.Ready or ExternalProjectDraftStatus.Failed))
            throw new ProjectException("DRAFT_LOCKED", "This draft cannot be edited in its current state.", 409);
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
        if (d.EditedDataJson != null) return JsonSerializer.Deserialize<ExternalProjectDraftData>(d.EditedDataJson, Json)! with { EvidenceFileIds = [] };
        var extraction = Extraction(d)?.Information;
        return new()
        {
            Title = extraction?.Title?.Value, Description = extraction?.Description?.Value, ProjectField = extraction?.ProjectField?.Value,
            StartDate = extraction?.StartDate?.Value, EndDate = extraction?.EndDate?.Value,
            RepositoryUrl = extraction?.RepositoryUrl?.Value, DemoUrl = extraction?.DemoUrl?.Value,
            SkillIds = extraction?.Skills.Where(s => s.SkillId.HasValue).Select(s => s.SkillId!.Value).Distinct().ToList() ?? [],
            Features = extraction?.Features.Select(f => f.Value).ToList() ?? [],
            Members = extraction?.Members.Select(m => new DeclaredProjectMember(m.Name.Value, m.Role?.Value)).ToList() ?? []
        };
    }

    private static ExternalProjectDraftResponse Response(ExternalProjectDraft d)
    {
        var data = Data(d);
        return new(d.Id, d.Status, d.Version, d.CreatedAt, d.UpdatedAt, d.ExpiresAt, d.ErrorCode, d.ConfirmedProjectId,
            data, null, ExternalProjectDraftValidation.MissingFields(data), []);
    }
}
