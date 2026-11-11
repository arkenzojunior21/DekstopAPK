using Laundry.Application.Abstractions;

namespace Laundry.Application.Auth;

/// <summary>
/// Aturan murni autentikasi tanpa database — supaya bisa diuji sebagai unit test.
/// </summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;
    public const int LockoutDurationMinutes = 15;

    /// <summary>Lockout aktif bila masih ada kunci dan waktunya belum lewat.</summary>
    public static bool IsLocked(DateTime? lockoutUntilUtc, DateTime nowUtc) =>
        lockoutUntilUtc.HasValue && lockoutUntilUtc.Value > nowUtc;

    /// <summary>Hitungan gagal ke-N menghasilkan lockout bila N mencapai batas.</summary>
    public static DateTime? NewLockoutUntil(int failedAttemptsAfterThisTry, DateTime nowUtc) =>
        failedAttemptsAfterThisTry >= MaxFailedAttempts
            ? nowUtc.AddMinutes(LockoutDurationMinutes)
            : null;
}

public static class OtpPolicy
{
    public const int CodeLength = 6;
    public const int ExpiryMinutes = 5;
    public const int MaxAttempts = 5;
    public const int ResendCooldownSeconds = 60;

    public static bool IsExpired(DateTime expiresAtUtc, DateTime nowUtc) => nowUtc >= expiresAtUtc;

    public static bool TooManyAttempts(int attempts) => attempts >= MaxAttempts;

    public static bool ResendAllowed(DateTime? lastResendAtUtc, DateTime nowUtc) =>
        lastResendAtUtc is null || nowUtc >= lastResendAtUtc.Value.AddSeconds(ResendCooldownSeconds);
}

/// <summary>Nomor WhatsApp → format internasional E.164 tanpa '+' (62...). Rekomendasi: validasi 9–15 digit.</summary>
public static class PhoneNormalizer
{
    public static string? Normalize(string? input)
    {
        var digits = new string((input ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.StartsWith("0") && digits.Length is 10 or 11 or 12 or 13)
            digits = "62" + digits[1..];
        if (digits.Length < 9 || digits.Length > 15) return null;
        return digits;
    }

    /// <summary>Password pelanggan minimal 8 karakter (rekomendasi implementasi, PRD tidak menetapkan angka).</summary>
    public static bool IsPasswordAcceptable(string password) => password.Length >= 8;
}
