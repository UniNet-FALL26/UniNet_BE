using Microsoft.EntityFrameworkCore;
using UniNet.Domain.Entities;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectRefactorSchemaTests
{
    [Fact]
    public void OptionalProjectRecordsAndMembershipHistoryHaveExplicitKeys()
    {
        using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only").Options);
        var entities = db.Model.GetEntityTypes().ToList();
        foreach (var table in new[] { "ProjectDetails", "ProjectRecruitments", "ProjectPersonalReviews" })
            Assert.Contains(entities, e => e.GetTableName() == table);
        var details = entities.Single(e => e.GetTableName() == "ProjectDetails");
        Assert.Equal("ProjectId", Assert.Single(details.FindPrimaryKey()!.Properties).Name);
        Assert.Equal("date", details.FindProperty("StartDate")!.GetColumnType());
        Assert.Equal("jsonb", details.FindProperty("Links")!.GetColumnType());
        var recruitment = entities.Single(e => e.GetTableName() == "ProjectRecruitments");
        Assert.Contains(recruitment.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ProjectId" }));
        var review = entities.Single(e => e.GetTableName() == "ProjectPersonalReviews");
        Assert.Contains(review.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ProjectId", "UserId" }));
        var member = db.Model.FindEntityType(typeof(ProjectMember))!;
        Assert.True(member.FindProperty("JoinRequestId")!.IsNullable);
        Assert.Contains(member.GetForeignKeys(), f => f.Properties.Single().Name == "JoinRequestId" && f.DeleteBehavior == DeleteBehavior.Restrict);
        foreach (var forbidden in new[] { "Contributions", "ContributionPercent", "ContributionConfirmed", "ShowInPortfolio" })
            Assert.Null(member.FindProperty(forbidden));
        foreach (var removed in new[] { "ProjectRoleRequirements", "ProjectSkills", "ProjectInvitations",
            "WorkspaceChannels", "WorkspaceMessages", "ProjectTasks", "ProjectTaskAssignments",
            "TaskReports", "TaskEvidence", "TaskReportReviews", "ExternalProjectDrafts" })
            Assert.DoesNotContain(entities, e => e.GetTableName() == removed);
    }
}
