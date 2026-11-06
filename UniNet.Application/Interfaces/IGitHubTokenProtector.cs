namespace UniNet.Application.Interfaces;

public interface IGitHubTokenProtector
{
    string Protect(string token);
    string Unprotect(string protectedToken);
}
