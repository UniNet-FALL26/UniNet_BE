using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using UniNet.Application;
using UniNet.Infrastructure.Data;
using UniNet.Domain;

namespace UniNet.Application.Services;

public sealed class AuthService(UniNetDbContext db, IConfiguration configuration)
{
    private readonly PasswordHasher<Account> hasher = new();
    private static string Email(string value) => value.Trim().ToLowerInvariant();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidEmail(string value) => value.Length <= 255 && Regex.IsMatch(value, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");
    private static void Require(bool condition, string code, string message) { if (!condition) throw new AuthException(code, message); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static AuthException InvalidCredentials() => new("INVALID_CREDENTIALS", "Email hoặc mật khẩu không chính xác.", 401);
    private static void CheckStatus(Account account)
    {
        if (account.Status == AccountStatus.Pending) throw new AuthException("ACCOUNT_PENDING", "Tài khoản đang chờ kích hoạt.", 403);
        if (account.Status == AccountStatus.Suspended) throw new AuthException("ACCOUNT_SUSPENDED", "Tài khoản đã bị tạm khóa.", 403);
        if (account.Status == AccountStatus.Deleted) throw InvalidCredentials();
    }
    private static void ValidateCredentials(string email, string password, string confirmation, string name)
    {
        Require(!string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 255, "INVALID_NAME", "Tên phải có từ 1 đến 255 ký tự.");
        Require(ValidEmail(email), "INVALID_EMAIL", "Email không hợp lệ.");
        Require(password.Length is >= 8 and <= 1024 && password.Any(char.IsLetter) && password.Any(char.IsDigit), "INVALID_PASSWORD", "Mật khẩu cần từ 8 đến 1024 ký tự, có chữ và số.");
        Require(password == confirmation, "PASSWORD_MISMATCH", "Mật khẩu xác nhận không khớp.");
    }
    private async Task SaveRegistration(Account account, UserProfile profile, CancellationToken ct)
    {
        account.Profile = profile;
        db.Accounts.Add(account);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new AuthException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng.", 409); }
    }
    public async Task<AuthResponse> RegisterStudent(RegisterStudentRequest request, CancellationToken ct)
    {
        var email = Email(request.Email);
        ValidateCredentials(email, request.Password, request.ConfirmPassword, request.FullName);
        Require((request.UniversityName?.Length ?? 0) <= 255 && (request.Major?.Length ?? 0) <= 255, "INVALID_PROFILE", "Thông tin học tập quá dài.");
        if (await db.Accounts.AnyAsync(x => x.Email == email, ct)) throw new AuthException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng.", 409);
        var account = new Account { Email = email, Role = AccountRole.Student };
        account.PasswordHash = hasher.HashPassword(account, request.Password);
        await SaveRegistration(account, new UserProfile { DisplayName = request.FullName.Trim(), UniversityName = Optional(request.UniversityName), Major = Optional(request.Major) }, ct);
        return await Issue(account, null, null, ct);
    }
    public async Task<AuthResponse> RegisterPartner(RegisterPartnerRequest request, CancellationToken ct)
    {
        var email = Email(request.Email);
        ValidateCredentials(email, request.Password, request.ConfirmPassword, request.DisplayName);
        Require(request.PartnerType is { } partnerType && Enum.IsDefined(partnerType), "INVALID_PARTNER_TYPE", "Loại đối tác không hợp lệ.");
        if (await db.Accounts.AnyAsync(x => x.Email == email, ct)) throw new AuthException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng.", 409);
        var account = new Account { Email = email, Role = AccountRole.Partner };
        account.PasswordHash = hasher.HashPassword(account, request.Password);
        await SaveRegistration(account, new UserProfile { DisplayName = request.DisplayName.Trim(), PartnerType = request.PartnerType }, ct);
        return await Issue(account, null, null, ct);
    }
    public async Task<AuthResponse> Login(LoginRequest request, CancellationToken ct)
    {
        var email = Email(request.Email);
        if (request.Password.Length > 1024) throw InvalidCredentials();
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Email == email, ct);
        if (account?.PasswordHash is null || hasher.VerifyHashedPassword(account, account.PasswordHash, request.Password) == PasswordVerificationResult.Failed) throw InvalidCredentials();
        CheckStatus(account);
        account.LastLoginAt = DateTimeOffset.UtcNow;
        account.UpdatedAt = account.LastLoginAt.Value;
        return await Issue(account, request.DeviceId, request.DeviceName, ct);
    }
    public async Task<AuthResponse> Google(GoogleLoginRequest request, CancellationToken ct)
    {
        var clientIds = configuration.GetSection("Google:ClientIds").Get<string[]>() ?? [];
        if (clientIds.Length == 0) throw new AuthException("GOOGLE_NOT_CONFIGURED", "Google Sign-In chưa được cấu hình.", 503);
        GoogleJsonWebSignature.Payload payload;
        try { payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, new GoogleJsonWebSignature.ValidationSettings { Audience = clientIds }); }
        catch { throw new AuthException("INVALID_GOOGLE_TOKEN", "Google token không hợp lệ.", 401); }
        Require(payload.EmailVerified && ValidEmail(payload.Email), "INVALID_GOOGLE_TOKEN", "Google token không hợp lệ.");
        var email = Email(payload.Email);
        var displayName = Optional(payload.Name) ?? email;
        if (displayName.Length > 255) displayName = displayName[..255];
        var account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.GoogleId == payload.Subject, ct);
        if (account is null)
        {
            account = await db.Accounts.Include(x => x.Profile).SingleOrDefaultAsync(x => x.Email == email, ct);
            if (account is null)
            {
                Require(request.Role is AccountRole.Student or AccountRole.Partner, "FORBIDDEN_ROLE", "Vai trò không hợp lệ.");
                Require(request.Role != AccountRole.Partner || request.PartnerType is null || Enum.IsDefined(request.PartnerType.Value), "INVALID_PARTNER_TYPE", "Loại đối tác không hợp lệ.");
                account = new Account { Email = email, GoogleId = payload.Subject, GoogleEmail = email, EmailVerified = true, Role = request.Role };
                await SaveRegistration(account, new UserProfile { DisplayName = displayName, AvatarUrl = Optional(payload.Picture), PartnerType = request.Role == AccountRole.Partner ? request.PartnerType : null }, ct);
            }
            else
            {
                if (account.GoogleId is not null && account.GoogleId != payload.Subject) throw new AuthException("INVALID_GOOGLE_TOKEN", "Google token không hợp lệ.", 401);
                CheckStatus(account);
                account.GoogleId = payload.Subject; account.GoogleEmail = email; account.EmailVerified = true;
                if (account.Profile is null) account.Profile = new UserProfile { DisplayName = displayName };
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                { throw new AuthException("EMAIL_ALREADY_EXISTS", "Email này đã được sử dụng.", 409); }
            }
        }
        CheckStatus(account);
        account.LastLoginAt = DateTimeOffset.UtcNow;
        return await Issue(account, null, null, ct);
    }
    public async Task<AuthResponse> Refresh(RefreshRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 256) throw new AuthException("INVALID_REFRESH_TOKEN", "Phiên đăng nhập không hợp lệ.", 401);
        var hash = Hash(request.RefreshToken);
        var token = await db.RefreshTokens.Include(x => x.Account).ThenInclude(x => x.Profile).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is null || token.RevokedAt is not null) throw new AuthException("INVALID_REFRESH_TOKEN", "Phiên đăng nhập không hợp lệ.", 401);
        if (token.ExpiresAt <= DateTimeOffset.UtcNow) throw new AuthException("REFRESH_TOKEN_EXPIRED", "Phiên đăng nhập đã hết hạn.", 401);
        CheckStatus(token.Account);
        var revokedAt = DateTimeOffset.UtcNow;
        var revoked = await db.RefreshTokens.Where(x => x.Id == token.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, revokedAt), ct);
        if (revoked != 1) throw new AuthException("INVALID_REFRESH_TOKEN", "Phiên đăng nhập không hợp lệ.", 401);
        return await Issue(token.Account, token.DeviceId, token.DeviceName, ct);
    }
    public async Task Logout(Guid accountId, string rawToken, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.AccountId == accountId && x.TokenHash == hash, ct);
        if (token is not null && token.RevokedAt is null) { token.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); }
    }
    public async Task LogoutAll(Guid accountId, CancellationToken ct)
    {
        var revokedAt = DateTimeOffset.UtcNow;
        await db.RefreshTokens.Where(x => x.AccountId == accountId && x.RevokedAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, revokedAt), ct);
    }
    public async Task<AccountResponse> Me(Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().Include(x => x.Profile).SingleOrDefaultAsync(x => x.Id == accountId, ct)
            ?? throw new AuthException("ACCOUNT_NOT_FOUND", "Tài khoản không tồn tại.", 404);
        CheckStatus(account);
        return AuthMapping.ToResponse(account);
    }
    private async Task<AuthResponse> Issue(Account account, string? deviceId, string? deviceName, CancellationToken ct)
    {
        var secret = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");
        var now = DateTimeOffset.UtcNow;
        Require((deviceId?.Length ?? 0) <= 255 && (deviceName?.Length ?? 0) <= 255, "INVALID_DEVICE", "Thông tin thiết bị quá dài.");
        var minutes = Math.Clamp(configuration.GetValue("Jwt:AccessMinutes", 30), 15, 60);
        var expires = now.AddMinutes(minutes);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, account.Id.ToString()), new Claim(JwtRegisteredClaimNames.Email, account.Email), new Claim(ClaimTypes.Role, account.Role.ToString()), new Claim("token_type", "access") };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims, now.UtcDateTime, expires.UtcDateTime, credentials);
        var rawRefresh = NewRefreshToken();
        db.RefreshTokens.Add(new RefreshToken { AccountId = account.Id, TokenHash = Hash(rawRefresh), ExpiresAt = now.AddDays(30), DeviceId = Optional(deviceId), DeviceName = Optional(deviceName) });
        await db.SaveChangesAsync(ct);
        var response = AuthMapping.ToResponse(account);
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), rawRefresh, expires, response, !response.ProfileComplete);
    }
}
