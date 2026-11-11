using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Laundry.Application.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Laundry.Infrastructure.Security;

/// <summary>
/// JWT HMAC-SHA256 access token berumur pendek.
/// Refresh token: 256 bit acak, disimpan hanya sebagai SHA-256 hash, dirotasi tiap pemakaian (PRD 6.1).
/// Kunci dari konfigurasi (JWT_KEY di .env / environment); tanpa fallback rahasia bawaan di production.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    public const string Issuer = "laundry-app";
    public const string Audience = "laundry-client";
    public const int AccessMinutes = 30;

    private readonly SymmetricSecurityKey _key;

    public JwtTokenService(string signingKey)
    {
        var bytes = Encoding.UTF8.GetBytes(signingKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("JWT_KEY terlalu pendek (minimal 32 byte).");
        _key = new SymmetricSecurityKey(bytes);
    }

    public string CreateAccessToken(Guid userId, string role)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, role),
            }),
            Issuer = Issuer,
            Audience = Audience,
            NotBefore = now,
            Expires = now.AddMinutes(AccessMinutes),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public (string Token, string TokenHash) CreateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return (token, HashRefreshToken(token));
    }

    public static string HashRefreshToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}
