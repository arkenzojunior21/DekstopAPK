using System.IdentityModel.Tokens.Jwt;
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
            ?? Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public string? Role =>
        Principal?.IsInRole(nameof(UserRole.PlatformAdmin)) == true ? nameof(UserRole.PlatformAdmin)
        : Principal?.IsInRole(nameof(UserRole.StoreAdmin)) == true ? nameof(UserRole.StoreAdmin)
        : Principal?.IsInRole(nameof(UserRole.Customer)) == true ? nameof(UserRole.Customer)
        : null;

    public Guid? TenantId =>
        Guid.TryParse(Principal?.FindFirst("tenant_id")?.Value, out var tid) ? tid : null;

    public string? TenantCode => Principal?.FindFirst("tenant_code")?.Value;
    public string? TenantNama => Principal?.FindFirst("tenant_nama")?.Value;

    public bool IsPlatformAdmin => Role == nameof(UserRole.PlatformAdmin);
    public bool IsStoreAdmin => Role == nameof(UserRole.StoreAdmin);
    public bool IsCustomer => Role == nameof(UserRole.Customer);
    public bool IsAdmin => IsPlatformAdmin || IsStoreAdmin;

    // MapInboundClaims aktif, tetapi jaga nama pendek bila mapping mati.
    private const string JwtRegisteredClaimNamesSub = "sub";
}
