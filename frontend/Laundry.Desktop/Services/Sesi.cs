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

    public bool Ada => UserId.HasValue;

    public bool Admin => string.Equals(Peran, "Admin", StringComparison.OrdinalIgnoreCase);

    public string Sapaan => string.IsNullOrWhiteSpace(Nama) ? Nomor ?? "pengguna" : Nama!;

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public void Isi(TokenResponse tokens, string? nama, string? peran, string? nomor)
    {
        AccessToken = tokens.AccessToken;
        RefreshToken = tokens.RefreshToken;
        UserId = tokens.UserId;
        Nama = nama;
        Peran = peran;
        Nomor = nomor;
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
        BeriTahu();
    }

    private void BeriTahu()
    {
        OnPropertyChanged(nameof(Ada));
        OnPropertyChanged(nameof(Admin));
        OnPropertyChanged(nameof(Sapaan));
    }
}
