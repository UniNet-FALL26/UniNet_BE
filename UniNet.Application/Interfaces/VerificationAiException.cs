namespace UniNet.Application.Interfaces;

public sealed class VerificationAiException(string message, bool transient, string code = "AI_PROVIDER_ERROR") : Exception(message)
{
    public bool Transient { get; } = transient;
    public string Code { get; } = code;
}
