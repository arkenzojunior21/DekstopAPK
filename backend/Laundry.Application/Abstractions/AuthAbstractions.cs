namespace Laundry.Application.Abstractions;

/// <summary>Hash password PBKDF2-SHA256. Tidak pernah plaintext (PRD 6.1).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public record AuthTokens(string AccessToken, string RefreshToken, int ExpiresInSeconds);

public record AuthResult(bool Success, string? Error, AuthTokens? Tokens = null, Guid? UserId = null);

public record OtpChallengeResult(bool Success, string? Error, Guid? ChallengeId, string? DevCode = null);

/// <summary>Token akses JWT + refresh token acak yang bisa dicabut dan dirotasi.</summary>
public interface ITokenService
{
    string CreateAccessToken(Guid userId, string role);
    (string Token, string TokenHash) CreateRefreshToken();
}

/// <summary>Adapter provider OTP/notifikasi WhatsApp. Mock aktif sampai credential resmi ada (D-04).</summary>
public interface IOtpSender
{
    Task SendOtpAsync(string phone, string code, CancellationToken ct = default);
}

/// <summary>Identitas pemanggil request, hasil pembacaan JWT.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Role { get; }
    bool IsAdmin { get; }
}
