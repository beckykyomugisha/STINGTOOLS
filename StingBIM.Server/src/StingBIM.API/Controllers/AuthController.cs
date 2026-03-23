using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using StingBIM.Core.DTOs;
using StingBIM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StingBIM.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly StingBimDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(StingBimDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthLoginResponse>> Login([FromBody] AuthLoginRequest req)
    {
        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);

        if (user == null || !BCryptVerify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password" });

        var token = GenerateJwt(user);
        var refreshToken = Guid.NewGuid().ToString("N");
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new AuthLoginResponse
        {
            AccessToken = token,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            UserName = user.DisplayName,
            Role = user.Role.ToString(),
            Tier = user.Tenant?.Tier.ToString() ?? "Starter",
            MimEnabled = user.Tenant?.MimEnabled ?? false
        });
    }

    [HttpPost("license/activate")]
    public async Task<ActionResult<LicenseActivationResponse>> ActivateLicense([FromBody] LicenseActivationRequest req)
    {
        var key = await _db.LicenseKeys
            .Include(k => k.Tenant)
            .FirstOrDefaultAsync(k => k.Key == req.LicenseKey && k.IsActive);

        if (key == null)
            return Ok(new LicenseActivationResponse { Valid = false, Message = "Invalid license key" });

        if (key.ExpiresAt.HasValue && key.ExpiresAt < DateTime.UtcNow)
            return Ok(new LicenseActivationResponse { Valid = false, Message = "License key has expired" });

        if (key.CurrentActivations >= key.MaxActivations)
            return Ok(new LicenseActivationResponse { Valid = false, Message = $"Maximum activations ({key.MaxActivations}) reached" });

        key.CurrentActivations++;
        key.LastActivatedBy = req.UserName;
        key.LastActivatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new LicenseActivationResponse
        {
            Valid = true,
            Tier = key.Tier.ToString(),
            MimEnabled = key.MimEnabled,
            ServerUrl = $"https://{key.Tenant?.Slug}.stingbim.io",
            ExpiresAt = key.ExpiresAt
        });
    }

    private string GenerateJwt(Core.Entities.AppUser user)
    {
        var jwtKey = _config["Jwt:Key"] ?? "StingBIM-Dev-Secret-Key-Min32Chars!!";
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("tenant_id", user.TenantId.ToString()),
            new Claim("role", user.Role.ToString()),
            new Claim("iso_role", user.Iso19650Role),
            new Claim("display_name", user.DisplayName)
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "StingBIM",
            audience: _config["Jwt:Audience"] ?? "StingBIM.Client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static bool BCryptVerify(string password, string hash)
    {
        // Simplified — use BCrypt.Net-Next in production
        return hash == Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }
}
