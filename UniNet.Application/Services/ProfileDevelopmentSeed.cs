using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniNet.Domain;
using UniNet.Infrastructure.Data;
namespace UniNet.Application.Services;

// Explicit CLI only. It never runs on normal startup and never creates an Account/User.
public sealed class ProfileDevelopmentSeed(UniNetDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task<object> Inspect(string email, CancellationToken ct)
    {
        var a = await db.Accounts.AsNoTracking().Include(a => a.Profile).ThenInclude(p => p!.CareerProfile).SingleOrDefaultAsync(a => a.Email.ToLower() == email.Trim().ToLowerInvariant(), ct);
        return new { accountFound = a != null, userFound = a?.Profile != null, careerProfileExists = a?.Profile?.CareerProfile != null, careerProfileCount = a?.Profile is null ? 0 : await db.CareerProfiles.CountAsync(c => c.UserId == a.Profile.Id, ct), pendingMigrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray() };
    }
    private static bool Empty(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim() is "[]" or "{}" or "null";
    public async Task<object> Run(string email, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var a = await db.Accounts.Include(a => a.Profile).ThenInclude(p => p!.CareerProfile).SingleOrDefaultAsync(a => a.Email.ToLower() == email.Trim().ToLowerInvariant(), ct)
            ?? throw new InvalidOperationException("Target account was not found. No data was changed.");
        var user = a.Profile ?? throw new InvalidOperationException("Target User was not found. No data was changed.");
        var changes = new List<string>();
        string? Fill(string? old, string value, string field) { if (!string.IsNullOrWhiteSpace(old)) return old; changes.Add(field); return value; }
        user.FullName = Fill(user.FullName, "Trịnh Trọng Quyền", "user.fullName")!;
        user.Nickname = Fill(user.Nickname, "Quyền", "user.nickname")!;
        user.Bio = Fill(user.Bio, "Build real products. Solve real problems.", "user.bio");
        user.UniversityName = Fill(user.UniversityName, "FPT University", "user.universityName");
        user.Major = Fill(user.Major, "Software Engineering", "user.major");
        user.Address = Fill(user.Address, "Ho Chi Minh City, Vietnam", "user.address");
        var created = user.CareerProfile is null;
        if (created) { user.CareerProfile = new CareerProfile { UserId = user.Id }; db.CareerProfiles.Add(user.CareerProfile); }
        var c = user.CareerProfile!;
        c.Headline = Fill(c.Headline, "Software Developer", "career.headline");
        c.CareerObjective = Fill(c.CareerObjective, "Không ngừng học hỏi, xây dựng những sản phẩm có giá trị thực tế và phát triển theo định hướng Fullstack Developer.", "career.objective");
        string? Section<T>(string? old, T value, string field) { if (!Empty(old)) return old; changes.Add(field); return JsonSerializer.Serialize(value, Json); }
        var skillSpecs = new (string Name, string Slug, SkillCategory Category, string Icon)[] {
            ("ASP.NET Core", "aspnet-core", SkillCategory.Backend, "dotnet"), ("React", "react", SkillCategory.Frontend, "react"),
            ("React Native", "react-native", SkillCategory.Mobile, "react"), ("TypeScript", "typescript", SkillCategory.Frontend, "typescript"),
            ("PostgreSQL", "postgresql", SkillCategory.Database, "postgresql"), ("SQL Server", "sql-server", SkillCategory.Database, ""),
            ("Firebase", "firebase", SkillCategory.Backend, "firebase"), ("Git", "git", SkillCategory.Tools, "git"),
            ("Docker", "docker", SkillCategory.DevOps, "docker"), ("Figma", "figma", SkillCategory.Design, "figma") };
        if (Empty(c.SkillsJson)) {
            var refs = new List<CareerSkill>();
            foreach (var spec in skillSpecs) {
                var skill = await db.Skills.SingleOrDefaultAsync(s => s.Slug == spec.Slug, ct) ?? await db.Skills.FirstOrDefaultAsync(s => s.Name.ToLower() == spec.Name.ToLower(), ct);
                if (skill is null) { skill = new Skill { Name = spec.Name, Slug = spec.Slug, Category = spec.Category, IconUrl = spec.Icon.Length > 0 ? "https://cdn.simpleicons.org/" + spec.Icon : null }; db.Skills.Add(skill); changes.Add("catalog." + spec.Slug); }
                refs.Add(new(skill.Id, 1, 1));
            }
            c.SkillsJson = Section(c.SkillsJson, refs, "career.skills");
        }
        c.ProjectsJson = Section(c.ProjectsJson, new PortfolioProject[] {
            new() { Title="Viora Social App", Type="Mobile App", Description="Mạng xã hội với bài viết, video ngắn và nhắn tin.", Technologies=["React Native","ASP.NET Core","PostgreSQL"], Featured=true },
            new() { Title="Digital Creative", Type="Web Platform", Description="Kết nối freelancer và khách hàng sáng tạo.", Technologies=["Next.js","ASP.NET Core","PostgreSQL"], Featured=true },
            new() { Title="ANKT Social App", Type="Mobile App", Description="Mạng xã hội tích hợp AI và mini-app.", Technologies=["Expo","Firebase","TypeScript"], Featured=true },
            new() { Title="UniNet", Type="Web/Mobile Platform", Technologies=["Web","Mobile"] },
            new() { Title="AntiFake", Type="Web/API", Technologies=["Web","API"] },
            new() { Title="Game 2D", Type="Game", Technologies=["Unity","C#"] } }, "career.projects");
        c.EducationJson = Section(c.EducationJson, new PortfolioMilestone[] { new() { Title="Software Engineering", Organization="FPT University", StartDate="2022-01", Period="2022 – Hiện tại" } }, "career.education");
        c.ExperienceJson = Section(c.ExperienceJson, new PortfolioMilestone[] {
            new() { Title="Freelance Developer", StartDate="2023-01", Period="2023" },
            new() { Title="Teaching Assistant", Organization="IoT Teens Connect", StartDate="2024-01", Period="2024" },
            new() { Title="OJT – React Native", Organization="FPT University", StartDate="2026-02", EndDate="2026-08", Period="02/2026 – 08/2026" } }, "career.experience");
        c.CertificatesJson = Section(c.CertificatesJson, new PortfolioCertificate[] {
            new() { Title="Microsoft Certified Azure Fundamentals", Issuer="Microsoft" }, new() { Title="EF Standard Certificate", Issuer="FPT University" },
            new() { Title="English Certificate (TOEIC)", Issuer="IIG Vietnam" }, new() { Title="React Native Certificate", Issuer="Udemy" } }, "career.certificates");
        c.AppearanceJson = Section(c.AppearanceJson, new ProfileAppearance(), "career.appearance");
        // Verification, student code, documents, contact privacy and IsPublic are untouched.
        if (changes.Count > 0) { c.UpdatedAt = DateTimeOffset.UtcNow; user.UpdatedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { accountFound=true, userFound=true, careerProfileCreated=created, careerProfileCount=await db.CareerProfiles.CountAsync(x => x.UserId == user.Id, ct), added=changes };
    }
}
