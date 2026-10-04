namespace UniNet.Application;

public sealed record PortfolioSkill
{
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
public sealed record PortfolioIdentity(Guid Id, string FullName, string? AvatarUrl, string? CoverUrl, string? Bio, string? UniversityName, string? Major, string? OrganizationName, bool IsVerified);
public sealed record PortfolioResponse(PortfolioIdentity Profile, PortfolioContent Portfolio, bool IsOwner);
