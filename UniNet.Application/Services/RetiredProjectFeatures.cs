namespace UniNet.Application.Services;

public static class RetiredProjectFeatures
{
    public static ProjectException Gone() => new("PROJECT_FEATURE_RETIRED",
        "Project workspace, tasks and external drafts have been retired.", 410);
}
