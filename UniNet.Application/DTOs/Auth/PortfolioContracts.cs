namespace UniNet.Application;

public sealed record PortfolioSkill
{
    public Guid? SkillId { get; init; }
    public int? Level { get; init; }
    public double? YearsOfExperience { get; init; }
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string? IconUrl { get; init; }
}
public sealed record PortfolioProject
{
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public string? Type { get; init; }
    public string? CoverUrl { get; init; }
    public string[] Technologies { get; init; } = [];
    public string? Url { get; init; }
    public string? GithubUrl { get; init; }
    public bool Featured { get; init; }
}
public sealed record PortfolioMilestone
{
    public string? StartDate { get; init; }
    public string? EndDate { get; init; }
    public string? Gpa { get; init; }
    public string Title { get; init; } = "";
    public string? Organization { get; init; }
    public string? Period { get; init; }
    public string? Description { get; init; }
}
public sealed record PortfolioCertificate
{
    public string Title { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string? Year { get; init; }
    public string? LogoUrl { get; init; }
    public string? Url { get; init; }
}
public sealed record PortfolioArticle
{
    public string Title { get; init; } = "";
    public string? Date { get; init; }
    public string? CoverUrl { get; init; }
    public string Url { get; init; } = "";
}
public sealed record PortfolioSocial
{
    public string Label { get; init; } = "";
    public string Url { get; init; } = "";
}
public sealed record PortfolioContent
{
    public string? Headline { get; init; }
    public string? CareerObjective { get; init; }
    public ProfileAppearance Appearance { get; init; } = new();
    public PortfolioActivity[]? Activities { get; init; }
    public PortfolioLanguage[]? Languages { get; init; }
    public string? Location { get; init; }
    public string? Availability { get; init; }
    public string? ContactEmail { get; init; }
    public string? CvUrl { get; init; }
    public bool IsPublic { get; init; }
    public PortfolioSkill[] Skills { get; init; } = [];
    public PortfolioProject[] Projects { get; init; } = [];
    public PortfolioMilestone[] Experience { get; init; } = [];
    public PortfolioMilestone[] Education { get; init; } = [];
    public PortfolioCertificate[] Certificates { get; init; } = [];
    public PortfolioArticle[] Articles { get; init; } = [];
    public PortfolioSocial[] SocialLinks { get; init; } = [];
}
// Only profile fields intended for portfolio presentation; no private account data.
public sealed record PortfolioIdentity(Guid Id, string FullName, string? AvatarUrl, string? CoverUrl, string? Bio, string? UniversityName, string? Major, string? OrganizationName, bool IsVerified, string? Nickname = null)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Nickname) ? FullName : Nickname;
}
public sealed record PortfolioResponse(PortfolioIdentity Profile, PortfolioContent Portfolio, bool IsOwner, PortfolioSkillCatalog[]? SkillCatalog = null);

public sealed record CareerSkill(Guid SkillId, int? Level = null, double? YearsOfExperience = null);
public sealed record PortfolioSkillCatalog(Guid Id, string Name, string Slug, string? IconUrl, string Category);
public sealed record PortfolioActivity(string Title, string? Organization = null, string? Description = null, string? Date = null);
public sealed record PortfolioLanguage(string Name, string Level);
public sealed record ProfileAppearance { public ProfileAppearanceSettings Profile { get; init; } = new(); }
public sealed record ProfileAppearanceSettings
{
    public string Template { get; init; } = "developer-modern";
    public string Theme { get; init; } = "blue";
    public string Font { get; init; } = "Inter";
    public string[] SectionOrder { get; init; } = ["featured-projects", "stats", "skills", "journey", "other-projects", "certificates", "activities", "languages", "articles"];
    public string[] HiddenSections { get; init; } = [];
}
