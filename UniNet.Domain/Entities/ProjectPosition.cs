using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace UniNet.Domain.Entities;

// JSON value object stored in ProjectRecruitments.Positions, not a database entity.
[NotMapped]
public sealed class ProjectPosition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonIgnore] public Guid ProjectId { get; set; }
    [JsonPropertyName("name")] public string Role { get; set; } = "";
    public int Quantity { get; set; }
    public string[] Technologies { get; set; } = [];
    public string[] Skills { get; set; } = [];
    public string JobDescription { get; set; } = "";
    [JsonPropertyName("requirements")] public string[] RequirementItems { get; set; } = [];
    public string[] Benefits { get; set; } = [];
    [JsonIgnore] public string Requirements { get => string.Join("\n", RequirementItems); set => RequirementItems = [value]; }
    [JsonIgnore] public DateTimeOffset CreatedAt { get; set; }
    [JsonIgnore] public DateTimeOffset UpdatedAt { get; set; }
}
