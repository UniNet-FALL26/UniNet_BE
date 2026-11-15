using Microsoft.Extensions.DependencyInjection;
using UniNet.Application.Interfaces;

namespace UniNet.Application.Services;

public static class GitExperimentalContributionServiceRegistration
{
    public static IServiceCollection AddGitExperimentalContributionServices(this IServiceCollection services)
    {
        services.AddScoped<ProjectGitEvidenceSnapshotService>();
        services.AddScoped<ProjectGitEvidenceSnapshotReader>();
        services.AddScoped<IGitContributionSnapshotAccess, ProjectGitContributionSnapshotAccess>();
        services.AddScoped<IGitExperimentalContributionService, GitExperimentalContributionService>();
        return services;
    }
}
