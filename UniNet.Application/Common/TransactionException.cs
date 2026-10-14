namespace UniNet.Application;

public sealed class TransactionException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

// Messages are safe diagnostics, never raw provider payloads or credentials.
public sealed class PaymentProviderException(string code, string message, bool definitivelyRejected = false)
    : Exception(message)
{
    public string Code { get; } = code;
    public bool DefinitivelyRejected { get; } = definitivelyRejected;
}
