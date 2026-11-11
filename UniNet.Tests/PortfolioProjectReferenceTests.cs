using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using UniNet.Domain.Enums;
using UniNet.Infrastructure.Data;
using Xunit;

namespace UniNet.Tests;

public sealed class PortfolioProjectReferenceTests
{
    [Theory]
    [InlineData(true, ProjectStatus.Completed, true)]
    [InlineData(false, ProjectStatus.Completed, true)]
    [InlineData(false, ProjectStatus.Active, false)]
    [InlineData(false, ProjectStatus.Pending, false)]
    public async Task CreatorAndMembersCanLinkOnlyCompletedProjects(bool creator, ProjectStatus status, bool allowed)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new UniNetDbContext(new DbContextOptionsBuilder<UniNetDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var owner = new Account { Email = "owner@example.com", Role = AccountRole.Student, Profile = new UserProfile { FullName = "Owner", Nickname = "Owner" } };
        var member = new Account { Email = "member@example.com", Role = AccountRole.Student, Profile = new UserProfile { FullName = "Member", Nickname = "Member" } };
        var outsider = new Account { Email = "outsider@example.com", Role = AccountRole.Student, Profile = new UserProfile { FullName = "Outsider", Nickname = "Outsider" } };
        db.Accounts.AddRange(owner, member, outsider);
        var project = new Project { Creator = owner.Profile!, Title = "Source title", Description = "Source description", ProjectField = "Software", Status = status, CompletedAt = DateTimeOffset.UtcNow };
        project.Members.Add(new ProjectMember { Id = Guid.NewGuid(), User = member.Profile!, Role = "Developer", Status = ProjectMemberStatus.Active });
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var service = new PortfolioService(db);
        var account = creator ? owner : member;
        var request = new PortfolioContent { IsPublic = true, Projects = [new() { ProjectId = project.Id, Title = "Spoofed title", PersonalContribution = "Built APIs", Featured = true }] };
        Assert.Equal(allowed ? 1 : 0, (await service.EligibleProjectList(account.Id, default)).Length);
        await Assert.ThrowsAsync<AuthException>(() => service.Save(outsider.Id, request, default));
        if (!allowed)
        {
            await Assert.ThrowsAsync<AuthException>(() => service.Save(account.Id, request, default));
            return;
        }
        var saved = await service.Save(account.Id, request, default);
        var item = Assert.Single(saved.Portfolio.Projects);
        Assert.Equal("Source title", item.Title);
        Assert.Equal("Built APIs", item.PersonalContribution);
        Assert.Null(item.Verification);
        Assert.DoesNotContain("Spoofed title", account.Profile!.CareerProfile!.ProjectsJson!);
        var visitor = await service.Read(account.Profile.Id, null, default);
        Assert.Equal(project.Id, Assert.Single(visitor.Portfolio.Projects).ProjectId);
        Assert.Single((await service.ApplicationSnapshot(account.Id, default)).Portfolio.Projects);
        await Assert.ThrowsAsync<AuthException>(() => service.Save(account.Id, request with { Projects = [request.Projects[0], request.Projects[0]] }, default));
        var package = new Package { Id = Guid.NewGuid(), Code = "VERIFY", Name = "Verification", Type = PackageType.Verification, Role = TargetRole.Student, IsActive = true, Price = 15000 };
        var verification = new ProjectVerification { Id = Guid.NewGuid(), Project = project, RequestByUser = owner.Profile!, Package = package, Status = ProjectVerificationStatus.Failed, FinalizedAt = DateTimeOffset.UtcNow };
        db.ProjectVerifications.Add(verification);
        await db.SaveChangesAsync();
        Assert.Equal("Failed", Assert.Single((await service.Me(account.Id, default)).Portfolio.Projects).Verification!.Status);
        verification.Status = ProjectVerificationStatus.Verified;
        await db.SaveChangesAsync();
        Assert.Equal("Verified", Assert.Single((await service.Me(account.Id, default)).Portfolio.Projects).Verification!.Status);
        if (!creator)
        {
            project.Members.Single().Status = ProjectMemberStatus.Kicked;
            await db.SaveChangesAsync();
            Assert.Empty(await service.EligibleProjectList(account.Id, default));
            await Assert.ThrowsAsync<AuthException>(() => service.Save(account.Id, request, default));
            project.Members.Single().Status = ProjectMemberStatus.Active;
            await db.SaveChangesAsync();
        }
        project.Title = "Updated source";
        await db.SaveChangesAsync();
        Assert.Equal("Updated source", Assert.Single((await service.Me(account.Id, default)).Portfolio.Projects).Title);
        project.Status = ProjectStatus.Active;
        await db.SaveChangesAsync();
        Assert.Empty((await service.Me(account.Id, default)).Portfolio.Projects);
    }
}