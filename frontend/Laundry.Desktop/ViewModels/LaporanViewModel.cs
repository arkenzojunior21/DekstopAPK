using System.IO;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;
using Laundry.Domain;
using Microsoft.Win32;

namespace Laundry.Desktop.ViewModels;

/// <summary>Satu hari pada grafik batang; tinggi sudah dihitung di sini supaya XAML hanya menggambar.</summary>
public sealed record BarisHari(string Label, int Pesanan, decimal Omzet, decimal Penerimaan, double Tinggi)
{
    public string OmzetTeks => Format.Rupiah(Omzet);
    public string PenerimaanTeks => Format.Rupiah(Penerimaan);
}

/// <summary>
/// Laporan + ekspor. Semua angka datang dari API (perhitungan ada di Domain), layar ini hanya
/// menampilkan dan mengunduh. Enam angka uang sengaja tidak digabung karena artinya berbeda:
/// omzet dan penerimaan adalah arus, piutang dan saldo kredit adalah posisi.
/// </summary>
public partial class LaporanViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    private const double TinggiGrafik = 132;

    [ObservableProperty] private IReadOnlyList<BarisHari> _deret = [];
    [ObservableProperty] private TabelLaporan? _tabel;
    [ObservableProperty] private JenisLaporan _jenis = JenisLaporan.Ringkasan;
    [ObservableProperty] private DateTime? _dari = DateTime.Today.AddDays(-29);
    [ObservableProperty] private DateTime? _sampai = DateTime.Today;
    [ObservableProperty] private string _rentang = "";
    [ObservableProperty] private string _omzet = "Rp0";
    [ObservableProperty] private string _penerimaan = "Rp0";
    [ObservableProperty] private string _dp = "Rp0";
    [ObservableProperty] private string _pelunasan = "Rp0";
    [ObservableProperty] private string _refund = "Rp0";
    [ObservableProperty] private string _piutang = "Rp0";
    [ObservableProperty] private string _saldoKredit = "Rp0";
    [ObservableProperty] private string _jumlahPesanan = "0";
    [ObservableProperty] private string _tagihanTerbuka = "0";
    [ObservableProperty] private string _catatan = "";

    public override string Judul => "Laporan";

    public IReadOnlyList<JenisLaporan> SemuaJenis { get; } = (JenisLaporan[])Enum.GetValues(typeof(JenisLaporan));

    partial void OnJenisChanged(JenisLaporan value) => _ = MuatAsync();

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var query = $"api/reports/laporan?{Filter()}";
        var laporan = await Konteks.Api.IsiAsync<LaporanDto>(query);
        var r = laporan.Ringkasan;

        Rentang = $"{Format.Tanggal(r.MulaiUtc)} – {Format.Tanggal(r.BatasAkhirUtc.AddDays(-1))} " +
                  $"({laporan.Rentang.JumlahHari} hari, dihitung dalam UTC)";
        Omzet = Format.Rupiah(r.Omzet);
        Penerimaan = Format.Rupiah(r.Penerimaan);
        Dp = Format.Rupiah(r.Dp);
        Pelunasan = Format.Rupiah(r.Pelunasan);
        Refund = Format.Rupiah(r.Refund);
        Piutang = Format.Rupiah(r.Piutang);
        SaldoKredit = Format.Rupiah(r.SaldoKreditPosisi);
        JumlahPesanan = Format.Angka(r.JumlahPesanan);
        TagihanTerbuka = Format.Angka(r.JumlahTagihanTerbuka);
        Catatan = r.JumlahPesanan == 0 && r.Penerimaan == 0m
            ? "Rentang ini benar-benar kosong: tidak ada pesanan dan tidak ada uang masuk. " +
              "Angka nol di sini hasil perhitungan, bukan data contoh."
            : $"Dibatalkan {r.JumlahDibatalkan} · kredit masuk {Format.Rupiah(r.KreditMasuk)} · " +
              $"kredit dipakai {Format.Rupiah(r.KreditDipakai)}";

        var tertinggi = laporan.Harian.Count == 0 ? 0m : laporan.Harian.Max(h => h.Omzet);
        Deret = [.. laporan.Harian.Select(h => new BarisHari(
            h.Tanggal.ToString("dd/MM", CultureInfo.GetCultureInfo("id-ID")),
            h.JumlahPesanan, h.Omzet, h.Penerimaan,
            tertinggi <= 0m ? 0 : (double)(h.Omzet / tertinggi) * TinggiGrafik))];

        Tabel = await Konteks.Api.IsiAsync<TabelLaporan>($"api/reports/tabel?jenis={Jenis}&{Filter()}");
    });

    [RelayCommand]
    private Task UnduhAsync(string? format) => JalankanAsync(async () =>
    {
        var dipili = (format ?? "pdf").ToLowerInvariant();
        var label = dipili switch
        {
            "pdf" => "PDF",
            "xlsx" => "Excel",
            _ => "CSV",
        };
        var dialog = new SaveFileDialog
        {
            Title = "Simpan hasil ekspor",
            FileName = $"laporan-{Jenis.ToString().ToLowerInvariant()}.{dipili}",
            Filter = $"Berkas {label}|*.{dipili}",
        };
        if (dialog.ShowDialog() != true) return;

        var hasil = await Konteks.Api.UnduhAsync($"api/reports/export?jenis={Jenis}&format={dipili}&{Filter()}");
        await File.WriteAllBytesAsync(dialog.FileName, hasil.Isi);
        Beres($"{hasil.Isi.Length:N0} byte disimpan di {dialog.FileName}");
    });

    private string Filter()
    {
        var dari = (Dari ?? DateTime.Today.AddDays(-29)).ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sampai = (Sampai ?? DateTime.Today).ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"dari={dari}&sampai={sampai}";
    }
}
