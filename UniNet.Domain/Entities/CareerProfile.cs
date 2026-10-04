namespace UniNet.Domain;

public sealed class CareerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public UserProfile User { get; set; } = null!;
    public string? Headline { get; set; }
    public string? CareerObjective { get; set; }
    public string? BasicInfoJson { get; set; }
    public string? EducationJson { get; set; }
    public string? SkillsJson { get; set; }
    public string? ExperienceJson { get; set; }
    public string? ProjectsJson { get; set; }
    public string? CertificatesJson { get; set; }
    public string? ActivitiesJson { get; set; }
    public string? LanguagesJson { get; set; }
    public string? SocialLinksJson { get; set; }
    public string? AppearanceJson { get; set; }
    public bool IsPublic { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
