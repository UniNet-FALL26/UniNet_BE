namespace UniNet.Application.Interfaces;

// Records completed units of work, not an independent liveness timer.
public interface IGitHubWorkerProgress
{
    void Completed(string loop);
}
