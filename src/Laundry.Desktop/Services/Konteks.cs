using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Laundry.Desktop.Services;

/// <summary>
/// Barang yang dibutuhkan semua layar: koneksi ke API, identitas sesi, preferensi mesin, dan dua
/// jalur pesan (kartu hijau untuk hasil, kartu merah untuk kegagalan). Tanpa ini tiap ViewModel
/// harus membuat HttpClient sendiri dan sesi tidak akan pernah konsisten.
/// </summary>
public sealed class Konteks
{
    public required KlienApi Api { get; init; }

    public required Sesi Sesi { get; init; }

    public required Preferensi Preferensi { get; init; }

    public Action<string>? Pesan { get; set; }

    public Action<string>? Galat { get; set; }

    /// <summary>Diminta ketika aksi berhasil dan layar saat ini sebaiknya dimuat ulang.</summary>
    public Func<Task>? MuatUlang { get; set; }

    /// <summary>Lompat ke menu lain lewat judulnya; dipakai kartu aksi cepat di beranda.</summary>
    public Action<string>? Navigasi { get; set; }

    public void BeriHasil(string teks) => Pesan?.Invoke(teks);

    public void BeriGalat(string teks) => Galat?.Invoke(teks);

    public void BukaMenu(string judul) => Navigasi?.Invoke(judul);
}

/// <summary>
/// Dasar semua ViewModel: satu bendera sibuk, satu pesan galat, dan pembungkus perintah yang
/// tidak pernah melempar exception ke UI thread. Semua layar memakai pola yang sama supaya
/// state kosong/sedang memuat/gagal tidak perlu ditulis ulang per fitur.
/// </summary>
public abstract partial class ViewModelDasar(Konteks konteks) : ObservableObject
{
    [ObservableProperty] private bool _sibuk;

    [ObservableProperty] private string? _galat;

    [ObservableProperty] private string? _hasil;

    protected Konteks Konteks { get; } = konteks;

    public virtual string Judul => "";

    public bool AdaGalat => !string.IsNullOrEmpty(Galat);

    partial void OnGalatChanged(string? value) => OnPropertyChanged(nameof(AdaGalat));

    /// <summary>
    /// Jalankan aksi async dengan proteksi penuh: tombol disabled, pesan galat terisi, tidak ada crash.
    /// Aksi bersarang (muat ulang setelah simpan, detail dari seleksi baris) tetap jalan — yang
    /// diblokir hanya aksi paralel dari klik ganda.
    /// </summary>
    protected async Task JalankanAsync(Func<Task> aksi, bool catatHasil = false)
    {
        if (Sibuk && !SedangDalamAksi) return;
        var palingLuar = !SedangDalamAksi;
        if (palingLuar)
        {
            SedangDalamAksi = true;
            Sibuk = true;
            Galat = null;
            if (!catatHasil) Hasil = null;
        }
        try
        {
            await aksi();
        }
        catch (ApiException ex)
        {
            Galat = ex.Message;
        }
        catch (OperationCanceledException)
        {
            // Pengguna menutup layar saat permintaan masih berjalan: bukan kesalahan.
        }
        catch (Exception ex)
        {
            Galat = $"Terjadi kesalahan tak terduga: {ex.Message}";
        }
        finally
        {
            if (palingLuar)
            {
                SedangDalamAksi = false;
                Sibuk = false;
            }
        }
    }

    private bool SedangDalamAksi;

    protected void Beres(string pesan)
    {
        Hasil = pesan;
        Konteks.Pesan?.Invoke(pesan);
    }

    protected void Gagal(string pesan)
    {
        Galat = pesan;
        Konteks.Galat?.Invoke(pesan);
    }

    [RelayCommand]
    private Task SegarkanAsync() => MuatAsync();

    /// <summary>Dimuat saat layar pertama dibuka dan setiap kali pengguna menekan Muat ulang.</summary>
    public virtual Task MuatAsync() => Task.CompletedTask;
}
