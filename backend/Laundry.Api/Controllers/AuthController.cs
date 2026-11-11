using Laundry.Application.Abstractions;
using Laundry.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Laundry.Api.Controllers;

public record PhoneRequest(string Phone);
public record RegisterCompleteRequest(Guid ChallengeId, string Otp, string Password, string Nama);
public record LoginRequest(string Phone, string Password);
public record RefreshRequest(string RefreshToken);
public record ResetCompleteRequest(Guid ChallengeId, string Otp, string NewPassword);
public record BootstrapRequest(string Phone, string Password);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    private IActionResult Result(AuthResult r) => r.Success
        ? Ok(new { accessToken = r.Tokens!.AccessToken, refreshToken = r.Tokens.RefreshToken,
                   expiresIn = r.Tokens.ExpiresInSeconds, userId = r.UserId })
        : Unauthorized(new { error = r.Error });

    /// <summary>Bootstrap admin pertama; otomatis tertutup setelah satu admin dibuat.</summary>
    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest req, CancellationToken ct)
    {
        var r = await auth.BootstrapAdminAsync(req.Phone, req.Password, ct);
        return r.Success ? Result(r) : Conflict(new { error = r.Error });
    }

    [HttpPost("register/start")]
    public async Task<IActionResult> RegisterStart([FromBody] PhoneRequest req, CancellationToken ct)
    {
        var r = await auth.RegisterStartAsync(req.Phone, ct);
        return r.Success
            ? Ok(new { challengeId = r.ChallengeId, devCode = r.DevCode })
            : BadRequest(new { error = r.Error });
    }

    [HttpPost("register/complete")]
    public async Task<IActionResult> RegisterComplete([FromBody] RegisterCompleteRequest req, CancellationToken ct)
        => Result(await auth.RegisterCompleteAsync(req.ChallengeId, req.Otp, req.Password, req.Nama, ct));

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
        => Result(await auth.LoginAsync(req.Phone, req.Password, ct));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
        => Result(await auth.RefreshAsync(req.RefreshToken, ct));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
    {
        await auth.LogoutAsync(req.RefreshToken, ct);
        return NoContent();
    }

    [HttpPost("reset/start")]
    public async Task<IActionResult> ResetStart([FromBody] PhoneRequest req, CancellationToken ct)
    {
        var r = await auth.ResetPasswordStartAsync(req.Phone, ct);
        return Ok(new { sent = r.Success, challengeId = r.ChallengeId, devCode = r.DevCode, message = r.Error });
    }

    [HttpPost("reset/complete")]
    public async Task<IActionResult> ResetComplete([FromBody] ResetCompleteRequest req, CancellationToken ct)
    {
        var r = await auth.ResetPasswordCompleteAsync(req.ChallengeId, req.Otp, req.NewPassword, ct);
        return r.Success ? Ok(new { message = "Password berhasil diganti." }) : Unauthorized(new { error = r.Error });
    }

    /// <summary>Contoh endpoint terproteksi role — dipakai test eskalasi role.</summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me([FromServices] ICurrentUser me) =>
        Ok(new { userId = me.UserId, role = me.Role, isAdmin = me.IsAdmin });

    [HttpGet("admin-only")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminOnly() => Ok(new { ok = true });
}
