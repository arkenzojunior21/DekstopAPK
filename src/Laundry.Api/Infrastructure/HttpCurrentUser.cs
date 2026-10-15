using System.Security.Claims;
using Laundry.Application.Abstractions;
using Laundry.Domain;

namespace Laundry.Api.Infrastructure;

/// <summary>Role dibaca dari token, bukan dari input client (PRD 5: UI bukan mekanisme otorisasi).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Principal?.FindFirst(JwtRegisteredClaimNameSub)?.Value, out var id) ? id : null;

    public string? Role => Principal?.IsInRole(nameof(UserRole.Admin)) == true
        ? nameof(UserRole.Admin)
        : Principal?.IsInRole(nameof(UserRole.Pelanggan)) == true ? nameof(UserRole.Pelanggan) : null;

    public bool IsAdmin => Role == nameof(UserRole.Admin);

    // MapInboundClaims aktif, tetapi jaga nama pendek bila mapping mati.
    private const string JwtRegisteredClaimNameSub = "sub";
}
