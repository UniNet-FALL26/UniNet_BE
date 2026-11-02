namespace UniNet.Domain;

public sealed class StudentProfileData
{
    public string? UniversityName { get; set; }
    public string? Major { get; set; }
    public string? StudentCode { get; set; }
}

public sealed class PartnerProfileData
{
    public string? OrganizationName { get; set; }
    public PartnerType? PartnerType { get; set; }
    public string? Industry { get; set; }
    public string? Website { get; set; }
}

public sealed class AssessorProfileData
{
    public string? JobTitle { get; set; }
    public string? CompanyName { get; set; }
    public int YearsOfExperience { get; set; }
    public string[] Expertise { get; set; } = [];
    public string? Education { get; set; }
    public string? University { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? GitHubUrl { get; set; }
    public string? PortfolioUrl { get; set; }
}
