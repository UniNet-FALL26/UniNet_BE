using UniNet.Domain.Entities;

namespace UniNet.Domain;

public sealed class Skill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? IconUrl { get; set; }
    public SkillCategory Category { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectSkill> ProjectSkills { get; set; } = [];
    public List<UserSkill> UserSkills { get; set; } = [];
}
