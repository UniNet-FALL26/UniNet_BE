namespace UniNet.API.Services;

public static class RuntimeWorkerRegistration
{
    // This gate controls persistent background work, not request authorization or session revocation.
    // Default true preserves existing deployments; shared-database local inspection must opt out.
    public static IServiceCollection AddRuntimeWorkers(this IServiceCollection services,
        IConfiguration configuration, bool isE2E)
    {
        var enabled = configuration.GetValue("BackgroundWorkers:Enabled", true);
        var unrelatedDisabled = isE2E && configuration.GetValue("GitHubE2E:DisableUnrelatedWorkers", false);
        if (enabled && !unrelatedDisabled)
        {
            services.AddHostedService<AssessorContractWorker>();
            services.AddHostedService<NotificationBackgroundWorker>();
            services.AddHostedService<ChatDeliveryWorker>();
            services.AddHostedService<VerificationBackgroundWorker>();
            services.AddHostedService<ExpiredTokenCleanup>();
            services.AddHostedService<GitHubOAuthFlowCleanupWorker>();
        }
        var adjustmentOnly=configuration.GetValue("GitHubSync:AdjustmentExecutionEnabled",false)&&!configuration.GetValue("GitHubSync:Enabled",false);
        if(enabled&&adjustmentOnly)
        {
            if(configuration.GetValue("GitHubSync:ScopedManual:Enabled",false)||isE2E&&!unrelatedDisabled)
                throw new InvalidOperationException("Adjustment execution requires exclusive scoped mode and E2E unrelated-worker isolation.");
            services.AddHostedService<GitContributionAdjustmentWorker>();
        }
        else if (enabled) services.AddHostedService<GitSyncWorker>();
        if (!unrelatedDisabled)
            services.AddHostedService(sp => sp.GetRequiredService<AccountSessionConnections>());
        if (enabled && !unrelatedDisabled) services.AddHostedService<PortraitCleanup>();
        return services;
    }
}
