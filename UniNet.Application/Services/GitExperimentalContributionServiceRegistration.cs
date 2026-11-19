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
        services.AddScoped<GitExperimentalContributionService>();
        services.AddScoped<IGitExperimentalContributionService>(sp => sp.GetRequiredService<GitExperimentalContributionService>());
        services.AddScoped<ExperimentalPeerCohortService>();
        services.AddScoped(sp => new OfficialContributionService(sp.GetRequiredService<UniNet.Infrastructure.Data.UniNetDbContext>(),
            sp.GetRequiredService<GitExperimentalContributionService>(), sp.GetRequiredService<ProjectGitEvidenceSnapshotService>(),
            sp.GetRequiredService<ProjectGitEvidenceSnapshotReader>(), sp.GetRequiredService<ExperimentalPeerCohortService>()));
        return services;
    }
}
