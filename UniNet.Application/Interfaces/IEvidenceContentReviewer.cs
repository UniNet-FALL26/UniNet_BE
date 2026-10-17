using UniNet.Application.DTOs.Projects;

namespace UniNet.Application.Interfaces;

public sealed record EvidenceContentVerdict(bool Passed, string Reason, string Proof);
public interface IEvidenceContentReviewer
{
    Task<EvidenceContentVerdict> ReviewContentAsync(string text, EvidencePolicySnapshot policy, CancellationToken ct);
}

public sealed record EvidenceUrlVerdict(bool Passed, string Reason, string Proof);
public interface IEvidenceUrlChecker
{
    Task<EvidenceUrlVerdict> CheckAsync(string url, EvidencePolicySnapshot policy, CancellationToken ct);
}
