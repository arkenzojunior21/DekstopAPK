using System.Security.Cryptography;
using System.Text;
using Laundry.Application.Abstractions;
using Laundry.Application.Auth;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Services;

/// <summary>
/// Use case autentikasi penuh sisi server (PRD 6.1). Role dan kepemilikan tidak pernah
/// dipercaya dari client. OTP disimpan ter-hash, expiry 5 menit, cooldown resend 60 detik,
/// maks 5 percobaan. Login gagal ke-5 mengunci 15 menit.
/// exposeOtpInResponse HANYA untuk environment development (D-05).
/// </summary>
public sealed class AuthService(
    LaundryDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IOtpSender otpSender,
    EventWriter events,
    bool exposeOtpInResponse) : IAuthService
{
    private static string HashOtp(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string RandomOtp()
    {
        Span<char> chars = stackalloc char[OtpPolicy.CodeLength];
        var bytes = RandomNumberGenerator.GetBytes(OtpPolicy.CodeLength);
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)('0' + bytes[i] % 10);
        return new string(chars);
    }

    private async Task<(AuthTokens Tokens, Guid UserId)> IssueSessionAsync(Guid userId, string role, CancellationToken ct)
    {
        var (refresh, refreshHash) = tokens.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = refreshHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync(ct);
        return (new AuthTokens(tokens.CreateAccessToken(userId, role), refresh, JwtTokenService.AccessMinutes * 60), userId);
    }

    public async Task<AuthResult> BootstrapAdminAsync(string phone, string password, CancellationToken ct = default)
    {
        var normalized = PhoneNormalizer.Normalize(phone);
        if (normalized is null) return new AuthResult(false, "Nomor WhatsApp tidak valid.");
        if (!PhoneNormalizer.IsPasswordAcceptable(password))
            return new AuthResult(false, "Password minimal 8 karakter.");
        if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin, ct))
            return new AuthResult(false, "Admin sudah ada; bootstrap ditutup.");

        var admin = new User
        {
            Role = UserRole.Admin,
            NormalizedPhone = normalized,
            PasswordHash = hasher.Hash(password),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        var session = await IssueSessionAsync(admin.Id, nameof(UserRole.Admin), ct);
        return new AuthResult(true, null, session.Tokens, session.UserId);
    }

    public async Task<OtpChallengeResult> RegisterStartAsync(string phone, CancellationToken ct = default)
    {
        var normalized = PhoneNormalizer.Normalize(phone);
        if (normalized is null) return new OtpChallengeResult(false, "Nomor WhatsApp tidak valid.", null);
        if (await db.Users.AnyAsync(u => u.NormalizedPhone == normalized, ct))
            return new OtpChallengeResult(false, "Nomor sudah terdaftar.", null);

        var (challenge, code) = NewChallenge("registrasi", normalized);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await otpSender.SendOtpAsync(normalized, code, ct);
        return new OtpChallengeResult(true, null, challenge.Id, exposeOtpInResponse ? code : null);
    }

    public async Task<AuthResult> RegisterCompleteAsync(Guid challengeId, string otp, string password, string nama,
        CancellationToken ct = default)
    {
        var challenge = await db.OtpChallenges.FirstOrDefaultAsync(c => c.Id == challengeId, ct);
        var error = await ValidateOtpAsync(challenge, "registrasi", otp, ct);
        if (error is not null || challenge is null) return new AuthResult(false, error ?? "Kode OTP tidak valid.");

        if (!PhoneNormalizer.IsPasswordAcceptable(password))
            return new AuthResult(false, "Password minimal 8 karakter.");
        if (string.IsNullOrWhiteSpace(nama))
            return new AuthResult(false, "Nama wajib diisi.");
        if (await db.Users.AnyAsync(u => u.NormalizedPhone == challenge!.TargetPhone, ct))
            return new AuthResult(false, "Nomor sudah terdaftar.", null);

        var user = new User
        {
            Role = UserRole.Pelanggan,
            NormalizedPhone = challenge.TargetPhone,
            PasswordHash = hasher.Hash(password),
            CreatedAtUtc = DateTime.UtcNow,
        };
        user.Profile = new CustomerProfile { UserId = user.Id, Nama = nama.Trim() };
        db.Users.Add(user);
        challenge.IsUsed = true;
        await db.SaveChangesAsync(ct);
        events.EnqueueCustomer(user.Profile, user, OrderService.DeviceId);
        await db.SaveChangesAsync(ct);
        var session = await IssueSessionAsync(user.Id, nameof(UserRole.Pelanggan), ct);
        return new AuthResult(true, null, session.Tokens, session.UserId);
    }

    public async Task<AuthResult> LoginAsync(string phone, string password, CancellationToken ct = default)
    {
        var normalized = PhoneNormalizer.Normalize(phone);
        if (normalized is null) return new AuthResult(false, "Nomor WhatsApp tidak valid.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedPhone == normalized, ct);
        if (user is null) return new AuthResult(false, "Nomor atau password salah.");

        if (LockoutPolicy.IsLocked(user.LockoutUntilUtc, DateTime.UtcNow))
            return new AuthResult(false,
                $"Akun dikunci sementara hingga {user.LockoutUntilUtc:HH:mm} UTC karena 5 kali percobaan gagal.");

        if (!hasher.Verify(user.PasswordHash, password))
        {
            user.FailedLoginCount++;
            user.LockoutUntilUtc = LockoutPolicy.NewLockoutUntil(user.FailedLoginCount, DateTime.UtcNow);
            await db.SaveChangesAsync(ct);
            return new AuthResult(false, "Nomor atau password salah.");
        }

        if (!user.IsActive) return new AuthResult(false, "Akun dinonaktifkan.");
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        await db.SaveChangesAsync(ct);
        var session = await IssueSessionAsync(user.Id, user.Role.ToString(), ct);
        return new AuthResult(true, null, session.Tokens, session.UserId);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = JwtTokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == hash && r.RevokedAtUtc == null, ct);
        if (stored is null || stored.ExpiresAtUtc <= DateTime.UtcNow || stored.User is null || !stored.User.IsActive)
            return new AuthResult(false, "Sesi tidak valid; silakan login ulang.");

        // Rotasi: token lama dicabut dan diganti satu token baru (single-use).
        var (newToken, newHash) = tokens.CreateRefreshToken();
        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.ReplacedByHash = newHash;
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = newHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync(ct);
        var role = stored.User.Role.ToString();
        return new AuthResult(true, null,
            new AuthTokens(tokens.CreateAccessToken(stored.UserId, role), newToken, JwtTokenService.AccessMinutes * 60),
            stored.UserId);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = JwtTokenService.HashRefreshToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash && r.RevokedAtUtc == null, ct);
        if (stored is null) return;
        stored.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<OtpChallengeResult> ResetPasswordStartAsync(string phone, CancellationToken ct = default)
    {
        var normalized = PhoneNormalizer.Normalize(phone);
        if (normalized is null) return new OtpChallengeResult(false, "Nomor WhatsApp tidak valid.", null);
        if (!await db.Users.AnyAsync(u => u.NormalizedPhone == normalized, ct))
            // Jangan bocorkan keberadaan akun lewat alur reset (pola respons netral).
            return new OtpChallengeResult(false, "Jika nomor terdaftar, kode OTP telah dikirim.", null);

        var (challenge, code) = NewChallenge("reset_password", normalized);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);
        await otpSender.SendOtpAsync(normalized, code, ct);
        return new OtpChallengeResult(true, null, challenge.Id, exposeOtpInResponse ? code : null);
    }

    public async Task<AuthResult> ResetPasswordCompleteAsync(Guid challengeId, string otp, string newPassword,
        CancellationToken ct = default)
    {
        var challenge = await db.OtpChallenges.FirstOrDefaultAsync(c => c.Id == challengeId, ct);
        var error = await ValidateOtpAsync(challenge, "reset_password", otp, ct);
        if (error is not null || challenge is null) return new AuthResult(false, error ?? "Kode OTP tidak valid.");

        if (!PhoneNormalizer.IsPasswordAcceptable(newPassword))
            return new AuthResult(false, "Password minimal 8 karakter.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedPhone == challenge.TargetPhone, ct);
        if (user is null) return new AuthResult(false, "Akun tidak ditemukan.");

        user.PasswordHash = hasher.Hash(newPassword);
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        challenge.IsUsed = true;
        // Reset password mencabut semua sesi aktif.
        foreach (var t in await db.RefreshTokens
                     .Where(r => r.UserId == user.Id && r.RevokedAtUtc == null).ToListAsync(ct))
            t.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new AuthResult(true, null);
    }

    private (OtpChallenge Challenge, string Code) NewChallenge(string purpose, string phone)
    {
        var code = RandomOtp();
        var now = DateTime.UtcNow;
        return (new OtpChallenge
        {
            Purpose = purpose,
            TargetPhone = phone,
            OtpHash = HashOtp(code),
            ExpiresAtUtc = now.AddMinutes(OtpPolicy.ExpiryMinutes),
            CreatedAtUtc = now,
        }, code);
    }

    private async Task<string?> ValidateOtpAsync(OtpChallenge? challenge, string purpose, string otp, CancellationToken ct)
    {
        if (challenge is null || challenge.Purpose != purpose) return "Kode OTP tidak valid.";
        if (challenge.IsUsed) return "Kode OTP sudah dipakai.";
        if (OtpPolicy.IsExpired(challenge.ExpiresAtUtc, DateTime.UtcNow)) return "Kode OTP kedaluwarsa.";
        if (OtpPolicy.TooManyAttempts(challenge.Attempts)) return "Percobaan OTP habis; minta kode baru.";

        challenge.Attempts++;
        if (HashOtp(otp) != challenge.OtpHash)
        {
            await db.SaveChangesAsync(ct);
            return "Kode OTP salah.";
        }
        await db.SaveChangesAsync(ct);
        return null;
    }
}
