using UniNet.Domain.Enums;

namespace UniNet.Domain.Entities;

public sealed class TaskReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectTaskId { get; set; }
    public Guid SubmittedById { get; set; }
    public Guid ReportGroupId { get; set; }
    public Guid? ParentReportId { get; set; }
    public int Version { get; set; }
    public string Content { get; set; } = null!;
    public TaskReportStatus Status { get; set; } = TaskReportStatus.Submitted;
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedById { get; set; }

    public ProjectTask ProjectTask { get; set; } = null!;
    public UserProfile SubmittedBy { get; set; } = null!;
    public TaskReport? ParentReport { get; set; }
    public List<TaskReport> ChildReports { get; set; } = [];
    public UserProfile? DeletedBy { get; set; }
    public List<TaskEvidence> Evidence { get; set; } = [];
    public List<TaskReportReview> Reviews { get; set; } = [];
}
