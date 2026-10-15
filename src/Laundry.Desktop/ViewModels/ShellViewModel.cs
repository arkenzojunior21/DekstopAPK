using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

public sealed record ButirMenu(string Judul, string Ikon, string Keterangan, Func<ViewModelDasar> Buat);

/// <summary>
/// Kerangka jendela utama setelah login. Menu yang muncul ditentukan peran yang dikembalikan server:
/// admin melihat operasional + laporan + sistem, pelanggan hanya melihat miliknya sendiri.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly Konteks _konteks;

    [ObservableProperty] private ViewModelDasar? _halaman;
    [ObservableProperty] private ButirMenu? _menuAktif;
    [ObservableProperty] private string? _statusSinkron;
    [ObservableProperty] private string? _toast;
    [ObservableProperty] private bool _sedangMuatStatus;
    [ObservableProperty] private string _namaTema = Tema.SaatIni;

    public ShellViewModel()
    {
        _konteks = App.Instance.Konteks;
        _konteks.Pesan = teks => Toast = teks;
        _konteks.Galat = teks => Toast = teks;
        _konteks.MuatUlang = async () =>
        {
            if (MenuAktif is { } butir) await MuatHalamanAsync(butir);
        };
        Menu = _konteks.Sesi.Admin ? MenuAdmin() : MenuPelanggan();
        _konteks.Navigasi = judul =>
        {
            var butir = Menu.FirstOrDefault(m => m.Judul == judul);
            if (butir is not null) MenuAktif = butir;
        };
        MenuAktif = Menu[0];
        _ = StatusAsync();
    }

    public Konteks Konteks => _konteks;

    public Services.Sesi Sesi => _konteks.Sesi;

    public IReadOnlyList<ButirMenu> Menu { get; }

    public string Versi => "v" + typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3);

    private static ButirMenu[] MenuAdmin() =>
    [
        new("Beranda", "\uE80F", "Ringkasan hari ini", () => new BerandaAdminViewModel(App.Instance.Konteks)),
        new("Pesanan", "\uE7B8", "Walk-in, online, status, pembayaran", () => new PesananViewModel(App.Instance.Konteks)),
        new("Buat Pesanan", "\uE710", "Transaksi baru di kasir", () => new PesanBaruViewModel(App.Instance.Konteks)),
        new("Katalog", "\uE8FD", "Layanan & tarif", () => new KatalogViewModel(App.Instance.Konteks)),
        new("Pelanggan", "\uE716", "Data pelanggan & saldo kredit", () => new PelangganViewModel(App.Instance.Konteks)),
        new("Slot", "\uE787", "Jadwal antar-jemput", () => new SlotViewModel(App.Instance.Konteks)),
        new("Komplain", "\uE8BD", "Tiket & balasan", () => new KomplainViewModel(App.Instance.Konteks)),
        new("Laporan", "\uE9D9", "Pendapatan, piutang, ekspor", () => new LaporanViewModel(App.Instance.Konteks)),
        new("Audit", "\uE7C3", "Jejak perubahan hanya-baca", () => new AuditViewModel(App.Instance.Konteks)),
        new("Sistem", "\uE770", "Sinkronisasi, backup, provider", () => new SistemViewModel(App.Instance.Konteks)),
    ];

    private static ButirMenu[] MenuPelanggan() =>
    [
        new("Beranda", "\uE80F", "Pesanan aktif & saldo", () => new BerandaPelangganViewModel(App.Instance.Konteks)),
        new("Pesan", "\uE710", "Buat pesanan online", () => new PesanBaruViewModel(App.Instance.Konteks)),
        new("Pesanan saya", "\uE7B8", "Status & tagihan", () => new PesananSayaViewModel(App.Instance.Konteks)),
        new("Komplain", "\uE8BD", "Ajukan & pantau tiket", () => new KomplainViewModel(App.Instance.Konteks)),
    ];

    /// <summary>
    /// Navigasi tunggal: halaman selalu ikut menu aktif, termasuk saat menu dipilih dari tombol
    /// di dalam halaman atau dari daftar menu pakai keyboard.
    /// </summary>
    partial void OnMenuAktifChanged(ButirMenu? value)
    {
        if (value is not null) _ = MuatHalamanAsync(value);
    }

    private async Task MuatHalamanAsync(ButirMenu butir)
    {
        var halaman = butir.Buat();
        Halaman = halaman;
        await halaman.MuatAsync();
    }

    /// <summary>Status sinkronisasi hanya dibaca admin; pelanggan tidak perlu tahu kondisi worker.</summary>
    public async Task StatusAsync()
    {
        if (!_konteks.Sesi.Admin || SedangMuatStatus) return;
        SedangMuatStatus = true;
        try
        {
            var status = await _konteks.Api.AmbilAsync<StatusSingkat>("api/sync/status");
            StatusSinkron = status is null
                ? "status tidak terbaca"
                : status.PekerjaAktif
                    ? $"Antrean {status.AntrianKeluarAdmin + status.AntrianKeluarCloud} · gagal " +
                      $"{status.AntrianGagalAdmin + status.AntrianGagalCloud} · detak {Format.Relatif(status.DetakTerakhirUtc)}"
                    : "pekerja sinkronisasi tidak berjalan";
        }
        catch (ApiException ex)
        {
            StatusSinkron = ex.Message;
        }
        finally
        {
            SedangMuatStatus = false;
        }
    }

    [RelayCommand]
    private void GantiTema()
    {
        App.Instance.GantiTema();
        NamaTema = Tema.SaatIni;
    }

    [RelayCommand]
    private void Keluar() => App.Instance.BukaLogin();

    private sealed record StatusSingkat(int AntrianKeluarAdmin, int AntrianKeluarCloud, int AntrianGagalAdmin,
        int AntrianGagalCloud, DateTime? DetakTerakhirUtc, bool PekerjaAktif);
}
