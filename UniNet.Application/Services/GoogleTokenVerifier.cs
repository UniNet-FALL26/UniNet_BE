using Google.Apis.Auth;

namespace UniNet.Application.Services;

public interface IGoogleTokenVerifier
{
    Task<GoogleJsonWebSignature.Payload> Verify(string idToken, string[] clientIds);
}

public sealed class GoogleTokenVerifier : IGoogleTokenVerifier
{
    public Task<GoogleJsonWebSignature.Payload> Verify(string idToken, string[] clientIds) =>
        GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings { Audience = clientIds });
}
