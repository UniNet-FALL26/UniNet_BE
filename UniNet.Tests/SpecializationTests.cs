using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using UniNet.Application;
using UniNet.Application.Services;
using UniNet.Domain;
using UniNet.Domain.Entities;
using Xunit;

namespace UniNet.Tests;

public sealed partial class ProjectVerificationTests
{
    [Fact]
    public async Task CatalogAndMatchingUseSharedIdsAndExcludeInactiveUnrelatedAndRevokedAssessors()
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        var people = Enumerable.Range(0, 5).Select(i => Fixture.Account($"Assessor{i}", AccountRole.Assessor)).ToArray();
        people[2].Status = AccountStatus.Suspended;
        f.Db.Accounts.AddRange(people);
        await f.Db.SaveChangesAsync();
        var service = new SpecializationService(f.Db);
        var catalog = await service.ListAsync();
        Assert.Equal(3, catalog.Count);
        var software = catalog.Single(s => s.Code == "SOFTWARE_ENGINEERING");
        var ai = catalog.Single(s => s.Code == "ARTIFICIAL_INTELLIGENCE");
        await service.SetProjectAsync(f.Other.Id, f.Project.Id, new(software.Id), default);
        foreach (var person in people.Where((_, i) => i != 1))
            await service.SetAssessorAsync(f.Other.Id, person.Profile!.Id, new([software.Id, software.Id]), default);
        await service.SetAssessorAsync(f.Other.Id, people[1].Profile!.Id, new([ai.Id]), default);
        f.Db.Set<NotificationPermissionOverride>().Add(new()
        {
            AccountId = people[3].Id, Permission = NotificationPermissionPolicy.EvidenceReview, IsGranted = false
        });
        f.Db.ProjectMembers.Add(new()
        {
            ProjectId = f.Project.Id, UserId = people[4].Profile!.Id,
            Role = "Developer", Status = Domain.Enums.ProjectMemberStatus.Active
        });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var matches = await service.MatchAsync(f.Other.Id, f.Project.Id);
        var match = Assert.Single(matches.Assessors);
        Assert.Equal(people[0].Profile!.Id, match.UserId);
        Assert.Equal(people[0].Id, match.AccountId);
        Assert.Equal(software.Id, match.Specialization.Id);
        Assert.Equal(1, matches.TotalItems);
        Assert.Empty((await service.MatchAsync(f.Other.Id, f.Project.Id, page: 2)).Assessors);
        Assert.Equal(software.Id, Assert.Single(await service.GetAssessorAsync(f.Other.Id, match.UserId, default)).Id);
        await service.SetAssessorAsync(f.Other.Id, match.UserId, new([]), default);
        Assert.Empty((await service.MatchAsync(f.Other.Id, f.Project.Id)).Assessors);
    }

    [Fact]
    public async Task CatalogValidationAndAdminPermissionsAreEnforced()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new SpecializationService(f.Db);
        await AssertProjectError("FORBIDDEN", () => service.MatchAsync(f.Owner.Id, f.Project.Id));
        await AssertProjectError("FORBIDDEN", () => service.SetAssessorAsync(f.Owner.Id, f.Other.Profile!.Id, new([]), default));
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        await AssertProjectError("SPECIALIZATION_REQUIRED", () => service.MatchAsync(f.Other.Id, f.Project.Id));
        await AssertProjectError("INVALID_PAGINATION", () => service.MatchAsync(f.Other.Id, f.Project.Id, 0));
        await AssertProjectError("PROJECT_NOT_FOUND", () => service.MatchAsync(f.Other.Id, Guid.NewGuid()));
        await AssertProjectError("ASSESSOR_NOT_FOUND", () => service.SetAssessorAsync(f.Other.Id, f.Owner.Profile!.Id, new([]), default));
        await AssertProjectError("INVALID_SPECIALIZATION", () => service.SetProjectAsync(f.Other.Id, f.Project.Id, new(Guid.NewGuid()), default));
        await AssertProjectError("SPECIALIZATION_REQUIRED", () => SpecializationService.ValidateProjectAsync(f.Db, null, default, required: true));
        await AssertProjectError("INVALID_SPECIALIZATION", () => service.SaveAsync(f.Other.Id, null, new("bad-code!", "Bad"), default));
        var created = await service.SaveAsync(f.Other.Id, null, new(" data_science ", "Khoa học dữ liệu"), default);
        Assert.Equal("DATA_SCIENCE", created.Code);
        await AssertProjectError("SPECIALIZATION_CODE_EXISTS", () => service.SaveAsync(f.Other.Id, null, new("data_science", "Duplicate"), default));
        await service.SetProjectAsync(f.Other.Id, f.Project.Id, new(created.Id), default);
        await service.SaveAsync(f.Other.Id, created.Id, new(created.Code, created.Name, false), default);
        Assert.DoesNotContain(await service.ListAsync(), s => s.Id == created.Id);
        Assert.Contains(await service.AdminListAsync(f.Other.Id, default), s => s.Id == created.Id);
        await AssertProjectError("INVALID_SPECIALIZATION", () => SpecializationService.ValidateProjectAsync(f.Db, created.Id, default));
        await AssertProjectError("SPECIALIZATION_INACTIVE", () => service.MatchAsync(f.Other.Id, f.Project.Id));
        f.Other.Status = AccountStatus.Suspended;
        await f.Db.SaveChangesAsync();
        await AssertProjectError("FORBIDDEN", () => service.AdminListAsync(f.Other.Id, default));
    }

    [Fact]
    public async Task ProjectSpecializationRoundTripsInAllProjectViewsWithoutChangingField()
    {
        await using var f = await Fixture.CreateAsync();
        f.Other.Role = AccountRole.Admin;
        await f.Db.SaveChangesAsync();
        var service = new SpecializationService(f.Db);
        var specialization = (await service.ListAsync()).First();
        await service.SetProjectAsync(f.Other.Id, f.Project.Id, new(specialization.Id), default);
        f.Db.ChangeTracker.Clear();
        var project = await f.Db.Projects.Include(p => p.Creator).SingleAsync(p => p.Id == f.Project.Id);
        Assert.Equal(specialization.Id, ProjectMapping.ToDetailResponse(project, null).Specialization!.Id);
        Assert.Equal(specialization.Id, ProjectMapping.ToListItemResponse(project).Specialization!.Id);
        Assert.Equal(specialization.Id, ProjectMapping.ToDiscoveryResponse(project).Specialization!.Id);
        Assert.Equal(f.Project.ProjectField, ProjectMapping.ToDetailResponse(project, null).ProjectField);
        var id = (await f.CreateVerificationAsync()).Verification.Id;
        Assert.Equal(specialization.Id, (await f.Service.GetDetailAsync(id, f.Owner.Id)).Project.Specialization!.Id);
        var anotherId = (await service.ListAsync()).Last().Id;
        await AssertProjectError("SPECIALIZATION_LOCKED", () => service.SetProjectAsync(f.Other.Id, f.Project.Id,
            new(anotherId), default));
    }

    [Fact]
    public async Task NewInternalProjectRequiresValidSpecializationBeforeWriting()
    {
        await using var f = await Fixture.CreateAsync();
        var service = new ProjectService(f.Db, null!, new ConfigurationBuilder().Build());
        var request = new CreateProjectRequest("New project", "IT", "Description", 3, 2,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(2), Domain.Enums.ProjectVisibility.Public);
        await AssertProjectError("SPECIALIZATION_REQUIRED", () => service.CreateProjectAsync(f.Owner.Id, request, default));
        await AssertProjectError("INVALID_SPECIALIZATION", () => service.CreateProjectAsync(f.Owner.Id,
            request with { SpecializationId = Guid.NewGuid() }, default));
    }

    private static async Task AssertProjectError(string code, Func<Task> action) =>
        Assert.Equal(code, (await Assert.ThrowsAsync<ProjectException>(action)).Code);
}
