using CommunityToolkit.Mvvm.ComponentModel;

namespace Laundry.Desktop.Services;

/// <summary>
/// Identitas sesi yang sedang aktif. Peran diambil dari jawaban server (`/api/auth/me`), bukan dari
/// ketikan pengguna, karena seluruh hak akses ditegakkan di API dan UI hanya menyesuaikan menu.
/// Token sengaja tidak pernah ditulis ke disk: menutup aplikasi benar-benar mengakhiri sesi.
/// </summary>
public partial class Sesi : ObservableObject
{
    [ObservableProperty] private string? _nama;
    [ObservableProperty] private string? _peran;
    [ObservableProperty] private string? _nomor;
    [ObservableProperty] private Guid? _userId;
    [ObservableProperty] private Guid? _tenantId;
    [ObservableProperty] private string? _tenantCode;
    [ObservableProperty] private string? _tenantNama;

    public bool Ada => UserId.HasValue;

    /// <summary>PlatformAdmin: akses penuh multi-tenant.</summary>
    public bool PlatformAdmin => string.Equals(Peran, "PlatformAdmin", StringComparison.OrdinalIgnoreCase);

    /// <summary>StoreAdmin: akses terbatas pada tenant sendiri.</summary>
    public bool StoreAdmin => string.Equals(Peran, "StoreAdmin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Customer: akses hanya data sendiri (pakai web, desktop hanya fallback).</summary>
    public bool Customer => string.Equals(Peran, "Customer", StringComparison.OrdinalIgnoreCase);

    /// <summary>Legacy: Admin lama = PlatformAdmin.</summary>
    public bool Admin => PlatformAdmin;

    public bool HasTenant => TenantId.HasValue && TenantId != Guid.Empty;

    public string Sapaan => string.IsNullOrWhiteSpace(Nama) ? Nomor ?? "pengguna" : Nama!;

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public void Isi(TokenResponse tokens, string? nama, string? peran, string? nomor, Guid? tenantId = null, string? tenantCode = null, string? tenantNama = null)
    {
        AccessToken = tokens.AccessToken;
        RefreshToken = tokens.RefreshToken;
        UserId = tokens.UserId;
        Nama = nama;
        Peran = peran;
        Nomor = nomor;
        TenantId = tenantId;
        TenantCode = tenantCode;
        TenantNama = tenantNama;
        BeriTahu();
    }

    public void Bersihkan()
    {
        AccessToken = null;
        RefreshToken = null;
        UserId = null;
        Nama = null;
        Peran = null;
        Nomor = null;
        TenantId = null;
        TenantCode = null;
        TenantNama = null;
        BeriTahu();
    }

    private void BeriTahu()
    {
        OnPropertyChanged(nameof(Ada));
        OnPropertyChanged(nameof(Admin));
        OnPropertyChanged(nameof(PlatformAdmin));
        OnPropertyChanged(nameof(StoreAdmin));
        OnPropertyChanged(nameof(Customer));
        OnPropertyChanged(nameof(HasTenant));
        OnPropertyChanged(nameof(Sapaan));
    }
}
