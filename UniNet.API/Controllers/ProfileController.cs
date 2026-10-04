using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniNet.Application;
using UniNet.Application.Services;

namespace UniNet.API.Controllers;

[ApiController, Authorize, Route("api/profile")]
public sealed class ProfileController(ProfileService profiles, AuthService auth, PortfolioService portfolio) : ControllerBase
{
    private Guid AccountId => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? throw new AuthException("UNAUTHORIZED", "Phiên đăng nhập không hợp lệ.", 401));
    [HttpGet("me")]
    public Task<AccountResponse> Me(CancellationToken ct) => auth.Me(AccountId, ct);
    [HttpGet("portfolio/me")]
    public Task<PortfolioResponse> PortfolioMe(CancellationToken ct) => portfolio.Me(AccountId, ct);
    [AllowAnonymous, HttpGet("portfolio/{profileId:guid}")]
    public Task<PortfolioResponse> Portfolio(Guid profileId, CancellationToken ct) => portfolio.Read(profileId, Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var viewer) ? viewer : null, ct);
    [HttpPut("portfolio/me"), RequestSizeLimit(524288)]
    public Task<PortfolioResponse> SavePortfolio(PortfolioContent request, CancellationToken ct) => portfolio.Save(AccountId, request, ct);
    [HttpGet("skills")]
    public Task<PortfolioSkillCatalog[]> Skills(CancellationToken ct) => portfolio.Catalog(ct);
    [HttpPut("appearance/me"), RequestSizeLimit(16384)]
    public Task<PortfolioResponse> Appearance(ProfileAppearance request, CancellationToken ct) => portfolio.SaveAppearance(AccountId, request, ct);
    [Authorize(Roles = "Student"), HttpPut("student")]
    public Task<AccountResponse> Student(StudentProfileRequest request, CancellationToken ct) => profiles.Student(AccountId, request, ct);
    [Authorize(Roles = "Partner"), HttpPut("partner")]
    public Task<AccountResponse> Partner(PartnerProfileRequest request, CancellationToken ct) => profiles.Partner(AccountId, request, ct);
}
