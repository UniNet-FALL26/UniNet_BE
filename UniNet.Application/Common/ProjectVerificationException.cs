namespace UniNet.Application;

public sealed class ProjectVerificationException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
