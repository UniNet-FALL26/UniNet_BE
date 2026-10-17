using System;

namespace UniNet.Application.Common;

public sealed class CommunityException(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}