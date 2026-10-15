using Laundry.Application.Abstractions;

namespace Laundry.Application.Auth;

public interface IAuthService
{
    /// <summary>Bootstrap admin pertama; hanya boleh ada satu admin (PRD 5).</summary>
    Task<AuthResult> BootstrapAdminAsync(string phone, string password, CancellationToken ct = default);

    Task<OtpChallengeResult> RegisterStartAsync(string phone, CancellationToken ct = default);

    Task<AuthResult> RegisterCompleteAsync(Guid challengeId, string otp, string password, string nama,
        CancellationToken ct = default);

    Task<AuthResult> LoginAsync(string phone, string password, CancellationToken ct = default);

    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task<OtpChallengeResult> ResetPasswordStartAsync(string phone, CancellationToken ct = default);

    Task<AuthResult> ResetPasswordCompleteAsync(Guid challengeId, string otp, string newPassword,
        CancellationToken ct = default);
}
