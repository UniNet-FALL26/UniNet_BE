using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    private Guid AccountId => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401));
    [HttpPost("register/student")]
    public Task<AuthResponse> RegisterStudent(RegisterStudentRequest request, CancellationToken ct) => auth.RegisterStudent(request, ct);
    [HttpPost("register/partner")]
    public Task<AuthResponse> RegisterPartner(RegisterPartnerRequest request, CancellationToken ct) => auth.RegisterPartner(request, ct);
    [HttpPost("login")]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => auth.Login(request, ct);
    [HttpPost("google")]
    public Task<AuthResponse> Google(GoogleLoginRequest request, CancellationToken ct) => auth.Google(request, ct);
    [HttpPost("refresh")]
    public Task<AuthResponse> Refresh(RefreshRequest request, CancellationToken ct) => auth.Refresh(request, ct);
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct) { await auth.Logout(AccountId, request.RefreshToken, ct); return NoContent(); }
    [Authorize, HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct) { await auth.LogoutAll(AccountId, ct); return NoContent(); }
    [Authorize, HttpGet("me")]
    public Task<AccountResponse> Me(CancellationToken ct) => auth.Me(AccountId, ct);
}
