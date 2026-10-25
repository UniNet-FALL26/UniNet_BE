using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed record AdminSkillQuery(string? Search = null, SkillCategory? Category = null, bool? IsActive = null, int Page = 1, int PageSize = 20);
public sealed record SaveAdminSkill(string Name, SkillCategory? Category, bool IsActive);
public sealed record AdminSkillItem(Guid Id, string Name, SkillCategory? Category, bool IsActive, int StudentCount);

public sealed class AdminSkillsService(UniNetDbContext db)
{
    private async Task RequireAdmin(Guid actor, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == actor && a.Role == AccountRole.Admin && a.Status == AccountStatus.Active, ct))
            throw new AuthException("FORBIDDEN", "Chỉ quản trị viên đang hoạt động được quản lý kỹ năng.", 403);
    }

    private static void ValidateCategory(SkillCategory? category)
    {
        if (category.HasValue && !Enum.IsDefined(category.Value)) throw new AuthException("INVALID_CATEGORY", "Nhóm kỹ năng không hợp lệ.");
    }

    private IQueryable<AdminSkillItem> Items(IQueryable<Skill> skills) => skills.Select(s => new AdminSkillItem(s.Id, s.Name, s.Category, s.IsActive,
        s.UserSkills.Where(u => u.User.Account.Role == AccountRole.Student && u.User.Account.Status != AccountStatus.Deleted).Select(u => u.UserId).Distinct().Count()));

    public async Task<AdminPage<AdminSkillItem>> ListAsync(Guid actor, AdminSkillQuery request, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        AdminUsersService.ValidatePage(request.Page, request.PageSize);
        ValidateCategory(request.Category);
        if (request.Search?.Length > 100) throw new AuthException("INVALID_SEARCH", "Từ khóa tối đa 100 ký tự.");
        var query = db.Set<Skill>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search)) { var search = request.Search.Trim().ToLowerInvariant(); query = query.Where(s => s.Name.ToLower().Contains(search)); }
        if (request.Category.HasValue) query = query.Where(s => s.Category == request.Category);
        if (request.IsActive.HasValue) query = query.Where(s => s.IsActive == request.IsActive);
        var total = await query.CountAsync(ct);
        var rows = await Items(query.OrderBy(s => s.Name).ThenBy(s => s.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)).ToListAsync(ct);
        return new(rows, request.Page, request.PageSize, total);
    }

    public async Task<AdminSkillItem> SaveAsync(Guid actor, Guid? id, SaveAdminSkill request, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100) throw new AuthException("INVALID_NAME", "Tên kỹ năng bắt buộc và tối đa 100 ký tự.");
        ValidateCategory(request.Category);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"admin-skills-catalog"}, 0))", ct);
        var row = id.HasValue ? await db.Set<Skill>().SingleOrDefaultAsync(s => s.Id == id.Value, ct)
            ?? throw new AuthException("SKILL_NOT_FOUND", "Không tìm thấy kỹ năng.", 404) : new Skill();
        var normalized = name.ToLowerInvariant();
        if (await db.Set<Skill>().AnyAsync(s => s.Id != row.Id && s.Name.Trim().ToLower() == normalized, ct))
            throw new AuthException("DUPLICATE_SKILL", "Tên kỹ năng đã tồn tại.", 409);
        row.Name = name; row.Category = request.Category; row.IsActive = request.IsActive; row.UpdatedAt = DateTimeOffset.UtcNow;
        if (!id.HasValue) db.Set<Skill>().Add(row);
        await db.SaveChangesAsync(ct);
        var result = await Items(db.Set<Skill>().Where(s => s.Id == row.Id)).SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }
}
