using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace UniNet.Domain.Entities;

// Catalog identity and display snapshot embedded in Projects.Technologies.
[NotMapped]
public sealed class ProjectTechnology
{
    public Guid SkillId { get; set; }
    public string Name { get; set; } = "";
    public SkillCategory? Category { get; set; }
    public string? IconUrl { get; set; }
    [JsonIgnore] public Guid ProjectId { get; set; }
    [JsonIgnore] public Skill Skill
    {
        get => new() { Id = SkillId, Name = Name, Category = Category, IconUrl = IconUrl };
        set { SkillId = value.Id; Name = value.Name; Category = value.Category; IconUrl = value.IconUrl; }
    }
}
