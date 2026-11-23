using System.Security.Claims;
using Laundry.Application.Abstractions;
using Laundry.Domain;

namespace Laundry.Api.Infrastructure;

/// <summary>
/// Implementasi ITenantContext yang mengambil tenant dari JWT claims.
/// Dipakai oleh LaundryDbContext untuk Global Query Filter.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    private Guid? _tenantId;
    private string? _tenantCode;
    private string? _tenantNama;

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? TenantId
    {
        get
        {
            if (_tenantId.HasValue) return _tenantId;
            var claim = _accessor.HttpContext?.User.FindFirst("tenant_id");
            if (claim != null && Guid.TryParse(claim.Value, out var id))
            {
                _tenantId = id;
                return _tenantId;
            }
            return null;
        }
    }

    public string? TenantCode
    {
        get
        {
            if (_tenantCode != null) return _tenantCode;
            var claim = _accessor.HttpContext?.User.FindFirst("tenant_code");
            if (claim != null)
            {
                _tenantCode = claim.Value;
                return _tenantCode;
            }
            return null;
        }
    }

    public string? TenantNama
    {
        get
        {
            if (_tenantNama != null) return _tenantNama;
            var claim = _accessor.HttpContext?.User.FindFirst("tenant_nama");
            if (claim != null)
            {
                _tenantNama = claim.Value;
                return _tenantNama;
            }
            return null;
        }
    }

    public bool IsPlatformAdmin => _accessor.HttpContext?.User.IsInRole(nameof(UserRole.PlatformAdmin)) == true;
    public bool IsStoreAdmin => _accessor.HttpContext?.User.IsInRole(nameof(UserRole.StoreAdmin)) == true;
    public bool IsCustomer => _accessor.HttpContext?.User.IsInRole(nameof(UserRole.Customer)) == true;

    public void SetTenant(Guid tenantId, string tenantCode, string tenantNama)
    {
        _tenantId = tenantId;
        _tenantCode = tenantCode;
        _tenantNama = tenantNama;
    }

    public void ClearTenant()
    {
        _tenantId = null;
        _tenantCode = null;
        _tenantNama = null;
    }
}