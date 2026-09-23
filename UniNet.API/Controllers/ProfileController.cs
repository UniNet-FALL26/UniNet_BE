using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/profile")]
public sealed class ProfileController(ProfileService profiles, AuthService auth) : ControllerBase
{
    private Guid AccountId => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401));
    [HttpGet("me")]
    public Task<AccountResponse> Me(CancellationToken ct) => auth.Me(AccountId, ct);
    [Authorize(Roles = "Student"), HttpPut("student")]
    public Task<AccountResponse> Student(StudentProfileRequest request, CancellationToken ct) => profiles.Student(AccountId, request, ct);
    [Authorize(Roles = "Partner"), HttpPut("partner")]
    public Task<AccountResponse> Partner(PartnerProfileRequest request, CancellationToken ct) => profiles.Partner(AccountId, request, ct);
}
