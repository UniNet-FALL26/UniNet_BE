namespace UniNet.Application.Interfaces;

public record VerificationAiInput(string Title, string Description, List<VerificationEvidenceRequest> Evidence);
public record VerificationAiReview(string Summary, decimal Score, List<string> AdditionalEvidenceRequired);
public sealed class VerificationAiException(string message, bool transient, string code = "AI_PROVIDER_ERROR") : Exception(message)
{
    public bool Transient { get; } = transient;
    public string Code { get; } = code;
}
public interface IVerificationAiReviewer
{
    Task<VerificationAiReview> ReviewAsync(VerificationAiInput input, CancellationToken ct);
}
