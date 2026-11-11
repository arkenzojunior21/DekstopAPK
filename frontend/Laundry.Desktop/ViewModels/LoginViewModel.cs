using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Layar masuk, daftar, dan lupa sandi. Semuanya memakai jalur API yang sama dengan pengguna lain —
/// tidak ada akun lokal. Nomor telepon disimpan sebagai teks biasa karena itulah identitasnya di PRD.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private const string ModeMasuk = "Masuk";
    private const string ModeDaftar = "Daftar";
    private const string ModeLupa = "Lupa";

    [ObservableProperty] private string _nomor = "";
    [ObservableProperty] private string _nama = "";
    [ObservableProperty] private string _kodeOtp = "";
    [ObservableProperty] private string _mode = ModeMasuk;
    [ObservableProperty] private string? _galat;
    [ObservableProperty] private string? _info;
    [ObservableProperty] private string? _pesanUmum;
    [ObservableProperty] private string? _kodeDev;
    [ObservableProperty] private string _statusServer = "menyiapkan…";
    [ObservableProperty] private bool _sibuk;

    public string Sandi { get; set; } = "";

    public string SandiBaru { get; set; } = "";

    /// <summary>Alamat API. Bisa disunting di layar login karena inilah satu-satunya tempat
    /// pengguna bisa masuk saat server masih salah — tanpa sesi, layar Sistem tidak terjangkau.</summary>
    [ObservableProperty] private string _server = App.Instance.Api.BasisUrl;

    public bool LagiMasuk => Mode == ModeMasuk;

    public bool LagiDaftar => Mode == ModeDaftar;

    public bool LagiLupa => Mode == ModeLupa;

    public bool PunyaTantangan => TantanganId.HasValue;

    private Guid? TantanganId { get; set; }

    partial void OnModeChanged(string value)
    {
        OnPropertyChanged(nameof(LagiMasuk));
        OnPropertyChanged(nameof(LagiDaftar));
        OnPropertyChanged(nameof(LagiLupa));
    }

    [RelayCommand]
    private void PilihMode(string pilihan)
    {
        Mode = pilihan;
        Galat = null;
        Info = null;
        KodeDev = null;
    }

    [RelayCommand]
    private async Task CekServerAsync()
    {
        StatusServer = "memeriksa…";
        StatusServer = await App.Instance.Api.CekServerAsync();
    }

    /// <summary>Pindah alamat API dan simpan ke preferensi mesin ini, lalu langsung periksa hasilnya.</summary>
    [RelayCommand]
    private async Task SimpanServerAsync()
    {
        var tujuan = Server.Trim().TrimEnd('/');
        if (!Uri.TryCreate(tujuan, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            Galat = "Alamat server harus lengkap dan diawali http:// atau https://.";
            return;
        }

        Galat = null;
        Server = tujuan;
        App.Instance.Api.GantiServer(tujuan);
        App.Instance.Preferensi.Server = tujuan;
        App.Instance.Preferensi.Simpan();
        StatusServer = "memeriksa…";
        StatusServer = await App.Instance.Api.CekServerAsync();
    }

    [RelayCommand]
    private async Task MasukAsync()
    {
        if (string.IsNullOrWhiteSpace(Nomor) || string.IsNullOrEmpty(Sandi))
        {
            Galat = "Isi nomor dan kata sandi.";
            return;
        }

        await JalankanAsync(async () =>
        {
            var tokens = await App.Instance.Api.KirimAsync<TokenResponse>("api/auth/login",
                new { phone = Nomor.Trim(), password = Sandi });
            await SelesaiMasukAsync(tokens, Nomor.Trim());
        });
    }

    [RelayCommand]
    private async Task DaftarMulaiAsync()
    {
        if (string.IsNullOrWhiteSpace(Nomor))
        {
            Galat = "Isi nomor telepon dulu.";
            return;
        }

        await JalankanAsync(async () =>
        {
            var hasil = await App.Instance.Api.KirimAsync<KodeHasil>("api/auth/register/start",
                new { phone = Nomor.Trim() });
            TantanganId = hasil?.ChallengeId;
            KodeDev = hasil?.DevCode;
            Info = hasil?.DevCode is null
                ? "Kode verifikasi dikirim ke nomor ini."
                : "Lingkungan pengembangan: kode muncul di layar, tidak dikirim lewat WhatsApp.";
            OnPropertyChanged(nameof(PunyaTantangan));
        });
    }

    [RelayCommand]
    private async Task DaftarSelesaiAsync()
    {
        if (TantanganId is null || string.IsNullOrWhiteSpace(KodeOtp) || string.IsNullOrEmpty(SandiBaru) ||
            string.IsNullOrWhiteSpace(Nama))
        {
            Galat = "Lengkapi kode, nama, dan kata sandi baru.";
            return;
        }

        await JalankanAsync(async () =>
        {
            var tokens = await App.Instance.Api.KirimAsync<TokenResponse>("api/auth/register/complete",
                new { challengeId = TantanganId, otp = KodeOtp.Trim(), password = SandiBaru, nama = Nama.Trim() });
            await SelesaiMasukAsync(tokens, Nomor.Trim());
        });
    }

    [RelayCommand]
    private async Task LupaMulaiAsync()
    {
        if (string.IsNullOrWhiteSpace(Nomor))
        {
            Galat = "Isi nomor telepon akun kamu.";
            return;
        }

        await JalankanAsync(async () =>
        {
            var hasil = await App.Instance.Api.KirimAsync<ResetMulaiHasil>("api/auth/reset/start",
                new { phone = Nomor.Trim() });
            TantanganId = hasil?.ChallengeId;
            KodeDev = hasil?.DevCode;
            Info = hasil?.Sent == true
                ? "Kode reset dikirim. Masukkan kode dan kata sandi baru di bawah."
                : hasil?.Message ?? "Nomor itu belum terdaftar.";
            OnPropertyChanged(nameof(PunyaTantangan));
        });
    }

    [RelayCommand]
    private async Task LupaSelesaiAsync()
    {
        if (TantanganId is null || string.IsNullOrWhiteSpace(KodeOtp) || string.IsNullOrEmpty(SandiBaru))
        {
            Galat = "Lengkapi kode dan kata sandi baru.";
            return;
        }

        await JalankanAsync(async () =>
        {
            var hasil = await App.Instance.Api.KirimAsync<PesanHasil>("api/auth/reset/complete",
                new { challengeId = TantanganId, otp = KodeOtp.Trim(), newPassword = SandiBaru });
            Mode = ModeMasuk;
            Info = hasil?.Message ?? "Kata sandi diganti. Silakan masuk.";
        });
    }

    /// <summary>Hanya berguna di database kosong: membuat admin pertama. Setelah itu server menolak 409.</summary>
    [RelayCommand]
    private async Task BootstrapAsync()
    {
        if (string.IsNullOrWhiteSpace(Nomor) || string.IsNullOrEmpty(Sandi))
        {
            Galat = "Isi nomor dan kata sandi admin pertama.";
            return;
        }

        await JalankanAsync(async () =>
        {
            var tokens = await App.Instance.Api.KirimAsync<TokenResponse>("api/auth/bootstrap",
                new { phone = Nomor.Trim(), password = Sandi });
            Info = "Admin pertama dibuat.";
            await SelesaiMasukAsync(tokens, Nomor.Trim());
        });
    }

    private async Task SelesaiMasukAsync(TokenResponse? tokens, string nomor)
    {
        if (tokens is null)
        {
            Galat = "Server tidak mengirim token.";
            return;
        }

        App.Instance.Api.SimpanToken(tokens.AccessToken, tokens.RefreshToken);
        var saya = await App.Instance.Api.AmbilAsync<ProfilHasil>("api/auth/me");
        if (saya is null)
        {
            Galat = "Identitas tidak dapat dibaca dari server.";
            return;
        }

        var sesi = App.Instance.Sesi;
        sesi.Isi(tokens, nama: null, peran: saya.Role, nomor: nomor);
        App.Instance.Preferensi.NomorTerakhir = nomor;
        App.Instance.Preferensi.Simpan();
        App.Instance.BukaUtama();
    }

    private async Task JalankanAsync(Func<Task> aksi)
    {
        if (Sibuk) return;
        Sibuk = true;
        Galat = null;
        try
        {
            await aksi();
        }
        catch (ApiException ex)
        {
            Galat = ex.Message;
        }
        catch (Exception ex)
        {
            Galat = ex.Message;
        }
        finally
        {
            Sibuk = false;
            OnPropertyChanged(nameof(PunyaTantangan));
        }
    }

    private sealed record PesanHasil(string? Message);

    private sealed record KodeHasil(Guid? ChallengeId, string? DevCode);

    private sealed record ResetMulaiHasil(bool Sent, Guid? ChallengeId, string? DevCode, string? Message);

    private sealed record ProfilHasil(Guid? UserId, string? Role, bool IsAdmin);
}
