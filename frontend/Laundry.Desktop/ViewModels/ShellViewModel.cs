using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

public sealed record ButirMenu(string Judul, string Ikon, string Keterangan, Func<ViewModelDasar> Buat);

/// <summary>
/// Kerangka jendela utama setelah login. Menu yang muncul ditentukan peran dari server:
/// - PlatformAdmin: kelola platform multi-tenant (tenant, keuangan, analytics, pengguna, audit, sistem, sinkronisasi)
/// - StoreAdmin: operasional toko (pesanan, katalog, pelanggan, slot, komplain, laporan, pengaturan, sinkronisasi)
/// - Customer: data sendiri (beranda, pesan, pesanan, komplain) — prioritas pakai Web PWA
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
    /// <summary>Mode sempit: lebar jendela di bawah ambang, sidebar jadi rel ikon agar konten tetap muat.</summary>
    [ObservableProperty] private bool _sempit;

    public ShellViewModel()
    {
        _konteks = App.Instance.Konteks;
        _konteks.Pesan = teks => Toast = teks;
        _konteks.Galat = teks => Toast = teks;
        _konteks.MuatUlang = async () =>
        {
            if (MenuAktif is { } butir) await MuatHalamanAsync(butir);
        };
        Menu = BangunMenu(_konteks.Sesi);
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

    private static IReadOnlyList<ButirMenu> BangunMenu(Services.Sesi sesi)
    {
        if (sesi.PlatformAdmin) return MenuPlatformAdmin();
        if (sesi.StoreAdmin) return MenuStoreAdmin();
        return MenuCustomer(); // fallback, Customer seharusnya pakai Web PWA
    }

    private static ButirMenu[] MenuPlatformAdmin() =>
    [
        new("Dashboard Platform", "\uE80F", "Ringkasan platform: GMV, tenant, komisi", () => new BerandaAdminViewModel(App.Instance.Konteks)),
        new("Kelola Tenant", "\uE716", "CRUD tenant, verifikasi, suspend, reset password", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
        new("Keuangan Platform", "\uE7B8", "Invoice komisi, pencatatan pembayaran", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
        new("Analytics", "\uE9D9", "GMV trend, top tenant, retensi, churn", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
        new("Pengguna Platform", "\uE7C3", "CRUD PlatformAdmin, role, 2FA", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
        new("Audit Trail", "\uE8BD", "Jejak perubahan seluruh platform", () => new AuditViewModel(App.Instance.Konteks)),
        new("Pengaturan Sistem", "\uE713", "Default komisi, fee, SLA, sync, backup", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
        new("Sinkronisasi", "\uE895", "Monitor & trigger sync semua tenant", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
    ];

    private static ButirMenu[] MenuStoreAdmin() =>
    [
        new("Beranda", "\uE80F", "KPI harian: pesanan baru, diproses, siap, omzet, piutang", () => new BerandaAdminViewModel(App.Instance.Konteks)),
        new("Pesanan", "\uE7B8", "Walk-in, online, status flow, pembayaran", () => new PesananViewModel(App.Instance.Konteks)),
        new("Buat Pesanan", "\uE710", "Transaksi walk-in baru di kasir", () => new PesanBaruViewModel(App.Instance.Konteks)),
        new("Katalog", "\uE8FD", "Layanan, harga berlaku, versi harga, express", () => new KatalogViewModel(App.Instance.Konteks)),
        new("Slot", "\uE787", "Jadwal antar-jemput per hari, kuota", () => new SlotViewModel(App.Instance.Konteks)),
        new("Pelanggan", "\uE716", "Data pelanggan, saldo kredit, riwayat", () => new PelangganViewModel(App.Instance.Konteks)),
        new("Pembayaran", "\uE7B7", "Tunai, QRIS, kredit, refund kasir", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
        new("Komplain", "\uE8BD", "Tiket masuk, balas, ubah status", () => new KomplainViewModel(App.Instance.Konteks)),
        new("Laporan", "\uE9D9", "7 jenis laporan + ekspor PDF/Excel/CSV", () => new LaporanViewModel(App.Instance.Konteks)),
        new("Pengaturan Toko", "\uE713", "Profil toko, jam, radius, delivery fee, printer, template", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
        new("Sinkronisasi", "\uE895", "Status sync tenant, trigger manual, coba ulang gagal", () => new BerandaAdminViewModel(App.Instance.Konteks)), // placeholder
    ];

    private static ButirMenu[] MenuCustomer() =>
    [
        new("Beranda", "\uE80F", "Pesanan aktif, saldo kredit, promo", () => new BerandaPelangganViewModel(App.Instance.Konteks)),
        new("Pesan", "\uE710", "Buat pesanan online (prioritas pakai Web PWA)", () => new PesanBaruViewModel(App.Instance.Konteks)),
        new("Pesanan Saya", "\uE7B8", "Status, tagihan, struk, batalkan", () => new PesananSayaViewModel(App.Instance.Konteks)),
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

    /// <summary>Status sinkronisasi dibaca PlatformAdmin & StoreAdmin; Customer tidak perlu tahu.</summary>
    public async Task StatusAsync()
    {
        if ((!_konteks.Sesi.PlatformAdmin && !_konteks.Sesi.StoreAdmin) || SedangMuatStatus) return;
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
