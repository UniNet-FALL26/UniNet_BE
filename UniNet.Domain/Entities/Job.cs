namespace UniNet.Domain.Entities;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyUserId { get; set; }
    public string Title { get; set; } = "";
    public short WorkType { get; set; }
    public decimal? MinSalaryVnd { get; set; }
    public decimal? MaxSalaryVnd { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? RequiredJson { get; set; }
    public string? BenefitsJson { get; set; }
    public short Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Company Company { get; set; } = null!;
    public List<JobSkill> JobSkills { get; set; } = [];
    public List<StudentSavedJob> SavedByStudents { get; set; } = [];
    public List<JobApplication> Applications { get; set; } = [];
}
