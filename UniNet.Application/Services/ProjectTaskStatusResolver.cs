using UniNet.Domain.Entities;
using UniNet.Domain.Enums;

namespace UniNet.Application.Services;

public static class ProjectTaskStatusResolver
{
    public static ProjectTaskStatus Resolve(ProjectTask task, DateTimeOffset now)
    {
        if (task.Status == ProjectTaskStatus.Overdue)
            return ProjectTaskStatus.Overdue;

        if (task.Status is ProjectTaskStatus.Completed or ProjectTaskStatus.Cancelled)
            return task.Status;

        return task.Deadline <= now ? ProjectTaskStatus.Overdue : task.Status;
    }
}
