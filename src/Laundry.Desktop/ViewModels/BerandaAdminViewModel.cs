using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Beranda admin: angka hari ini dari satu panggilan dashboard, kondisi sistem dari satu panggilan
/// konfigurasi, dan pesanan terakhir. Semua kartu hanya membaca — tidak ada perhitungan di sini.
/// </summary>
public partial class BerandaAdminViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    [ObservableProperty] private IReadOnlyList<Kartu> _kartu = [];
    [ObservableProperty] private IReadOnlyList<OrderListItem> _terbaru = [];
    [ObservableProperty] private ConfigStatusDto? _kondisi;
    [ObservableProperty] private AdminDashboardDto? _ringkas;

    public override string Judul => "Beranda";

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var d = await Konteks.Api.IsiAsync<AdminDashboardDto>("api/orders/dashboard/admin");
        Ringkas = d;
        Kartu = new[]
        {
            Baru("Pesanan baru", Angka(d.PesananBaru), "menunggu dikonfirmasi", "Aksen"),
            Baru("Dalam proses", Angka(d.DalamProses), "sedang dikerjakan", "Proses"),
            Baru("Siap diambil", Angka(d.SiapDiambil), "menunggu pelanggan", "Sukses"),
            Baru("Belum lunas", Angka(d.BelumLunas), "tagihan masih terbuka", d.BelumLunas > 0 ? "Bahaya" : "Sukses"),
            Baru("Omzet hari ini", Format.Rupiah(d.OmzetHariIni), "total tagihan jadi", "Aksen"),
            Baru("Kas masuk hari ini", Format.Rupiah(d.PenerimaanKasHariIni), "pembayaran terkonfirmasi", "Sukses"),
            Baru("Piutang", Format.Rupiah(d.Piutang), "seluruh tagihan belum lunas", "Bahaya"),
            Baru("Komplain terbuka", Angka(d.KomplainTerbuka), "butuh jawaban", d.KomplainTerbuka > 0 ? "Bahaya" : "Netral"),
            Baru("Penjemputan hari ini", Angka(d.PenjemputanHariIni), "slot antar-jemput", "Netral"),
            Baru("Pengantaran hari ini", Angka(d.PengantaranHariIni), "siap diantar", "Netral"),
        };
        Terbaru = await Konteks.Api.AmbilAsync<List<OrderListItem>>("api/orders?pageSize=8") ?? [];
        Kondisi = await Konteks.Api.AmbilAsync<ConfigStatusDto>("api/config/status");
    });

    private static Kartu Baru(string judul, string nilai, string keterangan, string nada) =>
        new(judul, nilai, keterangan, Tema.Kuas(Nada(nada)));

    private static string Nada(string nada) => nada switch
    {
        "Sukses" => "KuasSukses",
        "Proses" => "KuasProses",
        "Bahaya" => "KuasBahaya",
        "Aksen" => "KuasAksen",
        _ => "KuasNetral",
    };

    private static string Angka(int nilai) => Format.Angka(nilai);

    [RelayCommand]
    private void Buka(string judul) => Konteks.BukaMenu(judul);

    /// <summary>Backup manual dari beranda: sama seperti lewat halaman Sistem, hanya lebih dekat.</summary>
    [RelayCommand]
    private Task BackupAsync(string target) => JalankanAsync(async () =>
    {
        var hasil = await Konteks.Api.KirimAsync<BackupRunResult>("api/backup/jalan", new { target });
        if (hasil.Sukses)
            Beres($"Backup {hasil.Target} selesai: {hasil.NamaFile} " +
                  $"({Format.Angka(hasil.UkuranByte / 1024)} KB, {(hasil.Terverifikasi ? "terverifikasi" : "belum terverifikasi")})");
        else
            Gagal($"Backup {hasil.Target} gagal: {hasil.Error ?? "tanpa keterangan"}");
    }, catatHasil: true);
}
