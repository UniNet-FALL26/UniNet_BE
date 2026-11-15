using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class SpecializationService(UniNetDbContext db)
{
    public Task<List<SpecializationResponse>> ListAsync(bool includeInactive = false, CancellationToken ct = default) =>
        db.Specializations.AsNoTracking().Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Name).ThenBy(s => s.Id)
            .Select(s => new SpecializationResponse(s.Id, s.Code, s.Name, s.IsActive)).ToListAsync(ct);

    public static async Task<Specialization?> ValidateProjectAsync(UniNetDbContext db, Guid? id,
        CancellationToken ct, bool required = false)
    {
        if (id == null && !required) return null;
        if (id == null || id == Guid.Empty)
            throw new ProjectException("SPECIALIZATION_REQUIRED", "Please select a project specialization.", 400);
        return await db.Specializations.SingleOrDefaultAsync(s => s.Id == id && s.IsActive, ct)
            ?? throw new ProjectException("INVALID_SPECIALIZATION", "Select an active specialization from the catalog.", 400);
    }

    private async Task RequireAdminAsync(Guid accountId, string permission, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.Role == AccountRole.Admin &&
            a.Status == AccountStatus.Active, ct))
            throw new ProjectException("FORBIDDEN", "An active Admin account is required.", 403);
        await NotificationPermissionPolicy.RequireAsync(db, accountId, permission, ct);
    }

    public async Task<List<SpecializationResponse>> AdminListAsync(Guid accountId, CancellationToken ct)
    {
        await RequireAdminAsync(accountId, NotificationPermissionPolicy.StaffManage, ct);
        return await ListAsync(true, ct);
    }

    public async Task<SpecializationResponse> SaveAsync(Guid accountId, Guid? id,
        SaveSpecializationRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(accountId, NotificationPermissionPolicy.StaffManage, ct);
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100 ||
            !System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9_]+$") ||
            string.IsNullOrWhiteSpace(name) || name.Length > 255)
            throw new ProjectException("INVALID_SPECIALIZATION", "Code must be 1–100 letters/digits/underscores; name must be 1–255 characters.", 400);
        var item = id.HasValue
            ? await db.Specializations.SingleOrDefaultAsync(s => s.Id == id, ct)
                ?? throw new ProjectException("SPECIALIZATION_NOT_FOUND", "Specialization not found.", 404)
            : new Specialization();
        if (await db.Specializations.AnyAsync(s => s.Code == code && s.Id != item.Id, ct))
            throw DuplicateCode();
        item.Code = code; item.Name = name; item.IsActive = request.IsActive;
        if (!id.HasValue) db.Specializations.Add(item);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Specializations_Code" })
        { throw DuplicateCode(); }
        return SpecializationMapping.ToResponse(item)!;
    }

    public async Task<IReadOnlyList<SpecializationResponse>> GetAssessorAsync(Guid accountId, Guid userId, CancellationToken ct)
    {
        await RequireAdminAsync(accountId, NotificationPermissionPolicy.StaffManage, ct);
        await RequireAssessorAsync(userId, ct);
        return await ReadAssessorAsync(userId, ct);
    }

    public async Task<IReadOnlyList<SpecializationResponse>> SetAssessorAsync(Guid accountId, Guid userId,
        SetAssessorSpecializationsRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(accountId, NotificationPermissionPolicy.StaffManage, ct);
        await RequireAssessorAsync(userId, ct);
        if (request.SpecializationIds == null || request.SpecializationIds.Count > 30 ||
            request.SpecializationIds.Any(id => id == Guid.Empty))
            throw new ProjectException("INVALID_SPECIALIZATION", "Provide at most 30 valid specialization IDs.", 400);
        var ids = request.SpecializationIds.Distinct().ToArray();
        if (await db.Specializations.CountAsync(s => ids.Contains(s.Id) && s.IsActive, ct) != ids.Length)
            throw new ProjectException("INVALID_SPECIALIZATION", "All specializations must exist and be active.", 400);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var current = await db.AssessorSpecializations.Where(s => s.AssessorUserId == userId).ToListAsync(ct);
            db.AssessorSpecializations.RemoveRange(current.Where(s => !ids.Contains(s.SpecializationId)));
            foreach (var id in ids.Where(id => current.All(s => s.SpecializationId != id)))
                db.AssessorSpecializations.Add(new() { AssessorUserId = userId, SpecializationId = id });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (DatabaseWriteConflicts.IsWriteConflict(ex))
        { throw new ProjectException("SPECIALIZATION_CONFLICT", "Specializations changed concurrently. Reload and retry.", 409); }
        return await ReadAssessorAsync(userId, ct);
    }

    private async Task RequireAssessorAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.UserProfiles.AnyAsync(p => p.Id == userId && p.Account.Role == AccountRole.Assessor, ct))
            throw new ProjectException("ASSESSOR_NOT_FOUND", "Assessor profile not found. Use the profile userId, not accountId.", 404);
    }

    private async Task<List<SpecializationResponse>> ReadAssessorAsync(Guid userId, CancellationToken ct) =>
        await db.AssessorSpecializations.AsNoTracking().Where(s => s.AssessorUserId == userId)
            .OrderBy(s => s.Specialization.Name).ThenBy(s => s.SpecializationId)
            .Select(s => new SpecializationResponse(s.SpecializationId, s.Specialization.Code,
                s.Specialization.Name, s.Specialization.IsActive)).ToListAsync(ct);

    public async Task<SpecializationResponse> SetProjectAsync(Guid accountId, Guid projectId,
        SetProjectSpecializationRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var actor = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == accountId && a.Status == AccountStatus.Active)
                .Select(a => new { a.Role, ProfileId = (Guid?)a.Profile!.Id })
                .SingleOrDefaultAsync(ct)
                ?? throw new ProjectException("FORBIDDEN", "An active account is required.", 403);
            if (actor.Role == AccountRole.Admin)
                await NotificationPermissionPolicy.RequireAsync(db, accountId, NotificationPermissionPolicy.EvidenceReview, ct);

            var project = await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId, ct)
                ?? throw new ProjectException("PROJECT_NOT_FOUND", "Project not found.", 404);
            if (actor.Role != AccountRole.Admin && project.CreatorId != actor.ProfileId)
                throw new ProjectException("FORBIDDEN", "Only the project creator or an authorized admin can update its specialization.", 403);

            // Allow backfilling unclassified legacy projects, but do not reclassify existing cases.
            if (project.SpecializationId.HasValue && project.SpecializationId != request.SpecializationId &&
                await db.ProjectVerifications.AnyAsync(v => v.ProjectId == projectId, ct))
                throw new ProjectException("SPECIALIZATION_LOCKED", "Specialization cannot change after a verification request exists.", 409);
            var item = await ValidateProjectAsync(db, request.SpecializationId, ct, required: true);
            project.SpecializationId = item!.Id; project.Specialization = item; project.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return SpecializationMapping.ToResponse(item)!;
        }
        catch (Exception ex) when (DatabaseWriteConflicts.IsWriteConflict(ex))
        { throw new ProjectException("SPECIALIZATION_CONFLICT", "Project changed concurrently. Reload and retry.", 409); }
    }

    public async Task<MatchingAssessorsResponse> MatchAsync(Guid accountId, Guid projectId,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        await RequireAdminAsync(accountId, NotificationPermissionPolicy.EvidenceReview, ct);
        if (page < 1 || pageSize is < 1 or > 100)
            throw new ProjectException("INVALID_PAGINATION", "Page must be positive; pageSize must be 1–100.", 400);
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new ProjectException("PROJECT_NOT_FOUND", "Project not found.", 404);
        if (project.Specialization == null)
            throw new ProjectException("SPECIALIZATION_REQUIRED", "The project does not have a specialization yet.", 409);
        if (!project.Specialization.IsActive)
            throw new ProjectException("SPECIALIZATION_INACTIVE", "The project specialization is inactive.", 409);
        var permitted = NotificationPermissionPolicy.Recipients(db, NotificationPermissionPolicy.EvidenceReview);
        var query = db.UserProfiles.AsNoTracking().Where(p =>
            p.Account.Role == AccountRole.Assessor && permitted.Any(a => a.Id == p.AccountId) &&
            p.Id != project.CreatorId &&
            !db.ProjectMembers.Any(m => m.ProjectId == projectId && m.UserId == p.Id &&
                m.Status == ProjectMemberStatus.Active && m.LeftAt == null) &&
            db.AssessorSpecializations.Any(s => s.AssessorUserId == p.Id && s.SpecializationId == project.SpecializationId));
        var total = await query.CountAsync(ct);
        var people = await query.OrderBy(p => p.FullName).ThenBy(p => p.Id)
            .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize).ToListAsync(ct);
        var specialization = SpecializationMapping.ToResponse(project.Specialization)!;
        return new(projectId, specialization, page, pageSize, total,
            (int)(((long)total + pageSize - 1) / pageSize), people.Select(p => new MatchingAssessorResponse(
                p.Id, p.AccountId, p.FullName, p.AvatarUrl, p.AssessorJson?.JobTitle,
                p.AssessorJson?.YearsOfExperience ?? 0, specialization)).ToList());
    }

    private static ProjectException DuplicateCode() => new("SPECIALIZATION_CODE_EXISTS", "Specialization code already exists.", 409);
}
