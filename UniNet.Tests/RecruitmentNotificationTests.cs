using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class RecruitmentNotificationTests
{
    [Theory]
    [InlineData(JobApplicationStatus.Accepted)]
    [InlineData(JobApplicationStatus.Rejected)]
    public async Task ApplicationAndDecisionNotifyOnlyTheOwningAccounts(JobApplicationStatus decision)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, UtcSqliteModelCustomizer>().Options);
        await db.Database.EnsureCreatedAsync();
        Account Account(string name, AccountRole role) => new() { Email = $"{name}@notice.test", Role = role,
            Profile = new UserProfile { FullName = name, Nickname = name } };
        var student = Account("student", AccountRole.Student);
        var partner = Account("partner", AccountRole.Partner);
        var stranger = Account("stranger", AccountRole.Student);
        db.Accounts.AddRange(student, partner, stranger);
        var job = new Job { Partner = partner.Profile!, Title = "Developer", Status = (short)PartnerJobStatus.Published };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        var opportunities = new StudentOpportunityService(db, new PortfolioService(db));
        await opportunities.ApplyAsync(student.Id, job.Id, "Interested", default);
        var application = await db.JobApplications.SingleAsync();
        var submitted = await db.Set<VerificationNotification>().SingleAsync();
        Assert.Equal(partner.Id, submitted.RecipientUserId);
        Assert.Equal(application.Id, submitted.TargetId);
        Assert.Equal("JobApplication", submitted.TargetType);
        var targets = new NotificationTargets(db, new NotificationService(db));
        Assert.Equal($"/(company)/applicants?jobId={job.Id}", await targets.ResolveAsync(partner.Id, submitted.Id, default));
        await new PartnerService(db).UpdateApplicantStatusAsync(partner.Id, job.Id, application.Id, decision, default);
        var reviewed = await db.Set<VerificationNotification>().SingleAsync(n => n.RecipientUserId == student.Id);
        Assert.Equal(application.Id, reviewed.TargetId);
        Assert.Equal(partner.Id, reviewed.SenderUserId);
        Assert.Equal("recruitment.application.status", reviewed.Type);
        Assert.Equal($"/opportunities?tab=applications&jobId={job.Id}", await targets.ResolveAsync(student.Id, reviewed.Id, default));
        Assert.Empty(await db.Set<VerificationNotification>().Where(n => n.RecipientUserId == stranger.Id).ToListAsync());
    }
}
