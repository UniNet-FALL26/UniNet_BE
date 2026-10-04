using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Infrastructure.Data;

namespace UniNet.Application.Services;

public sealed class PortfolioService(UniNetDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Basic(string? Location, string? Availability, string? ContactEmail, string? CvUrl, PortfolioArticle[]? Articles);
    private static T? Decode<T>(string? value)
    {
        if (value is null) return default;
        try { return JsonSerializer.Deserialize<T>(value, Json); }
        catch (JsonException) { return default; }
    }
    private static AuthException Missing() => new("PROFILE_NOT_FOUND", "Không tìm thấy hồ sơ công khai.", 404);
    private static void Text(string? value, int max = 2000, bool required = false)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > max)
            throw new AuthException("INVALID_PORTFOLIO", "Vui lòng nhập đủ thông tin và kiểm tra độ dài nội dung.");
    }
    private static void Url(string? value, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value) && !required) return;
        if (value?.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new AuthException("INVALID_PORTFOLIO_URL", "Liên kết phải là URL HTTP hoặc HTTPS hợp lệ.");
    }
    private static void Items<T>(T[]? values, int max, Action<T> validate)
    {
        if (values is null || values.Length > max) throw new AuthException("INVALID_PORTFOLIO", "Danh sách hồ sơ không hợp lệ hoặc có quá nhiều mục.");
        foreach (var item in values)
        {
            if (item is null) throw new AuthException("INVALID_PORTFOLIO", "Mục hồ sơ không được để trống.");
            validate(item);
        }
    }
    public static void Validate(PortfolioContent p)
    {
        Text(p.Headline, 255); Text(p.CareerObjective); Text(p.Location, 255); Text(p.Availability, 100); Url(p.CvUrl);
        if (!string.IsNullOrWhiteSpace(p.ContactEmail) && (!MailAddress.TryCreate(p.ContactEmail, out var email) || email.Address != p.ContactEmail || p.ContactEmail.Length > 255))
            throw new AuthException("INVALID_PORTFOLIO_EMAIL", "Email liên hệ không hợp lệ.");
        Items(p.Skills, 100, x => { if (x.SkillId is null) Text(x.Name, 100, true); if (x.Level is < 0 or > 3 || x.YearsOfExperience is < 0 or > 80 || (x.YearsOfExperience.HasValue && !double.IsFinite(x.YearsOfExperience.Value))) throw new AuthException("INVALID_SKILL", "Mức độ kỹ năng không hợp lệ."); });
        Items(p.Projects, 50, x => { Text(x.Title, 255, true); Text(x.Description); Text(x.Type, 100); Url(x.CoverUrl); Url(x.Url); Url(x.GithubUrl); Items(x.Technologies, 20, t => Text(t, 100, true)); });
        void Milestone(PortfolioMilestone x) { Text(x.Title, 255, true); Text(x.Organization, 255); Text(x.Period, 100); Text(x.Description); Text(x.StartDate, 20); Text(x.EndDate, 20); Text(x.Gpa, 20); }
        Items(p.Experience, 50, Milestone); Items(p.Education, 50, Milestone);
        Items(p.Certificates, 50, x => { Text(x.Title, 255, true); Text(x.Issuer, 255, true); Text(x.Year, 100); Url(x.LogoUrl); Url(x.Url); });
        Items(p.Articles, 50, x => { Text(x.Title, 255, true); Text(x.Date, 100); Url(x.CoverUrl); Url(x.Url, true); });
        Items(p.Activities ?? [], 50, x => { Text(x.Title, 255, true); Text(x.Organization, 255); Text(x.Description); Text(x.Date, 100); });
        Items(p.Languages ?? [], 20, x => { Text(x.Name, 100, true); Text(x.Level, 100, true); });
        Items(p.SocialLinks, 20, x => { Text(x.Label, 100, true); Url(x.Url, true); });
    }
    private static PortfolioResponse Map(UserProfile p, bool owner, PortfolioSkillCatalog[] catalog)
    {
        var c = p.CareerProfile;
        var b = Decode<Basic>(c?.BasicInfoJson);
        return new(new(p.Id, p.FullName, p.AvatarUrl, p.CoverUrl, p.Bio, p.UniversityName, p.Major, p.OrganizationName, p.IsVerified, p.Nickname), new()
        {
            Headline = c?.Headline, CareerObjective = c?.CareerObjective, Appearance = Decode<ProfileAppearance>(c?.AppearanceJson) ?? new(),
            Activities = Decode<PortfolioActivity[]>(c?.ActivitiesJson) ?? [], Languages = Decode<PortfolioLanguage[]>(c?.LanguagesJson) ?? [], Location = b?.Location, Availability = b?.Availability,
            ContactEmail = b?.ContactEmail, CvUrl = b?.CvUrl, IsPublic = c?.IsPublic ?? false,
            Skills = ResolveSkills(c?.SkillsJson, catalog), Projects = Decode<PortfolioProject[]>(c?.ProjectsJson) ?? [],
            Experience = Decode<PortfolioMilestone[]>(c?.ExperienceJson) ?? [], Education = Decode<PortfolioMilestone[]>(c?.EducationJson) ?? [],
            Certificates = Decode<PortfolioCertificate[]>(c?.CertificatesJson) ?? [], Articles = b?.Articles ?? [],
            SocialLinks = Decode<PortfolioSocial[]>(c?.SocialLinksJson) ?? []
        }, owner, catalog);
    }
    private async Task<UserProfile> Own(Guid accountId, CancellationToken ct) =>
        await db.UserProfiles.Include(x => x.CareerProfile).SingleOrDefaultAsync(x => x.AccountId == accountId && x.Account.Status == AccountStatus.Active, ct) ?? throw Missing();
    public async Task<PortfolioResponse> Me(Guid accountId, CancellationToken ct) => Map(await Own(accountId, ct), true, await Catalog(ct));
    public async Task<PortfolioResponse> Read(Guid profileId, Guid? viewerId, CancellationToken ct)
    {
        var p = await db.UserProfiles.AsNoTracking().Include(x => x.CareerProfile).SingleOrDefaultAsync(x => x.Id == profileId && x.Account.Status == AccountStatus.Active && (x.AccountId == viewerId || (x.CareerProfile != null && x.CareerProfile.IsPublic)), ct) ?? throw Missing();
        return Map(p, p.AccountId == viewerId, await Catalog(ct));
    }
    public async Task<PortfolioResponse> Save(Guid accountId, PortfolioContent request, CancellationToken ct)
    {
        Validate(request);
        var catalog = await Catalog(ct);
        var skills = ReferenceSkills(request.Skills, catalog);
        var p = await Own(accountId, ct);
        if (p.CareerProfile is null)
        {
            p.CareerProfile = new CareerProfile { UserId = p.Id };
            db.CareerProfiles.Add(p.CareerProfile);
        }
        var c = p.CareerProfile;
        c.Headline = request.Headline?.Trim(); c.CareerObjective = request.CareerObjective?.Trim(); c.IsPublic = request.IsPublic;
        // Preserve unrelated existing basic metadata and extracurricular activities.
        var basic = Decode<JsonObject>(c.BasicInfoJson) ?? new JsonObject();
        var changes = JsonSerializer.SerializeToNode(new Basic(request.Location, request.Availability, request.ContactEmail, request.CvUrl, request.Articles), Json)!.AsObject();
        foreach (var property in changes) basic[property.Key] = property.Value?.DeepClone();
        c.BasicInfoJson = basic.ToJsonString(Json);
        c.SkillsJson = JsonSerializer.Serialize(skills, Json); c.ProjectsJson = JsonSerializer.Serialize(request.Projects, Json);
        c.ExperienceJson = JsonSerializer.Serialize(request.Experience, Json); c.EducationJson = JsonSerializer.Serialize(request.Education, Json);
        c.CertificatesJson = JsonSerializer.Serialize(request.Certificates, Json);
        if (request.Activities is not null) c.ActivitiesJson = JsonSerializer.Serialize(request.Activities, Json);
        if (request.Languages is not null) c.LanguagesJson = JsonSerializer.Serialize(request.Languages, Json);
        c.SocialLinksJson = JsonSerializer.Serialize(request.SocialLinks, Json); c.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(p, true, catalog);
    }

    public async Task<PortfolioSkillCatalog[]> Catalog(CancellationToken ct) => await db.Skills.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).Select(s => new PortfolioSkillCatalog(s.Id, s.Name, s.Slug, s.IconUrl, s.Category.ToString())).ToArrayAsync(ct);
    private static PortfolioSkill[] ResolveSkills(string? json, PortfolioSkillCatalog[] catalog)
    {
        var items = Decode<PortfolioSkill[]>(json) ?? [];
        return items.Select(s => {
            var entry = catalog.FirstOrDefault(c => c.Id == s.SkillId || (s.SkillId is null && c.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)));
            return entry is null ? s : s with { SkillId = entry.Id, Name = entry.Name, Category = entry.Category, IconUrl = entry.IconUrl };
        }).Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToArray();
    }
    private static CareerSkill[] ReferenceSkills(PortfolioSkill[] skills, PortfolioSkillCatalog[] catalog) => skills.Select(s => {
        var entry = catalog.FirstOrDefault(c => s.SkillId.HasValue ? c.Id == s.SkillId : c.Name.Equals(s.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry is null) throw new AuthException("INVALID_SKILL_REFERENCE", "Vui lòng chọn kỹ năng từ danh mục.");
        return new CareerSkill(entry.Id, s.Level, s.YearsOfExperience);
    }).DistinctBy(s => s.SkillId).ToArray();
    public static void ValidateAppearance(ProfileAppearance appearance)
    {
        var p = appearance?.Profile;
        var keys = new ProfileAppearanceSettings().SectionOrder;
        if (p is null || p.Template != "developer-modern" || p.Theme is not ("blue" or "orange") || p.Font is not ("Inter" or "System") || p.SectionOrder is null || p.HiddenSections is null || p.SectionOrder.Length > keys.Length || p.HiddenSections.Length > keys.Length || p.SectionOrder.Concat(p.HiddenSections).Any(k => !keys.Contains(k)) || p.SectionOrder.Distinct().Count() != p.SectionOrder.Length || p.HiddenSections.Distinct().Count() != p.HiddenSections.Length)
            throw new AuthException("INVALID_APPEARANCE", "Mẫu hoặc màu hồ sơ không hợp lệ.");
    }
    public async Task<PortfolioResponse> SaveAppearance(Guid accountId, ProfileAppearance appearance, CancellationToken ct)
    {
        ValidateAppearance(appearance);
        var p = await Own(accountId, ct);
        if (p.CareerProfile is null) { p.CareerProfile = new CareerProfile { UserId = p.Id }; db.CareerProfiles.Add(p.CareerProfile); }
        var root = Decode<JsonObject>(p.CareerProfile.AppearanceJson) ?? new JsonObject();
        root["profile"] = JsonSerializer.SerializeToNode(appearance.Profile, Json);
        p.CareerProfile.AppearanceJson = root.ToJsonString(Json);
        p.CareerProfile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(p, true, await Catalog(ct));
    }
}
