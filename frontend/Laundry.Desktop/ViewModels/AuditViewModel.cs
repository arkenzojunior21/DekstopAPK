using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;
using Microsoft.Win32;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Jejak audit transaksi: hanya dibaca. Tidak ada satu pun tombol ubah/hapus di layar ini karena
/// barisnya memang tidak bisa diubah dari aplikasi — kalau isinya salah, itu fakta yang harus
/// diselidiki, bukan diedit.
/// </summary>
public partial class AuditViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    private const int UkuranHalaman = 50;

    public ObservableCollection<AuditBaris> Daftar { get; } = [];

    [ObservableProperty] private AuditBaris? _terpilih;
    [ObservableProperty] private string _cari = "";
    [ObservableProperty] private string _entitas = "";
    [ObservableProperty] private DateTime? _dari = DateTime.Today.AddDays(-29);
    [ObservableProperty] private DateTime? _sampai = DateTime.Today;
    [ObservableProperty] private int _halaman;
    [ObservableProperty] private string _total = "";
    [ObservableProperty] private string _rentang = "";

    public override string Judul => "Audit";

    public bool AdaDetail => Terpilih is not null;

    public bool AdaHalamanBerikut => (Halaman + 1) * UkuranHalaman < TotalTerhitung;

    private int TotalTerhitung { get; set; }

    partial void OnTerpilihChanged(AuditBaris? value) => OnPropertyChanged(nameof(AdaDetail));

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var hasil = await Konteks.Api.IsiAsync<AuditHasil>($"api/audit?{Filter()}");
        TotalTerhitung = hasil.Total;
        Daftar.Clear();
        foreach (var baris in hasil.Baris) Daftar.Add(baris);
        Total = $"{Format.Angka(hasil.Total)} baris · halaman {hasil.Halaman + 1} · {hasil.Baris.Count} tampil";
        Rentang = $"{Format.Tanggal(hasil.Rentang.MulaiUtc)} – " +
                  $"{Format.Tanggal(hasil.Rentang.BatasAkhirUtc.AddDays(-1))} (UTC)";
        OnPropertyChanged(nameof(AdaHalamanBerikut));
    });

    [RelayCommand]
    private Task CariAsync()
    {
        Halaman = 0;
        return MuatAsync();
    }

    [RelayCommand]
    private Task HalamanSebelumnyaAsync()
    {
        if (Halaman == 0) return Task.CompletedTask;
        Halaman--;
        OnPropertyChanged(nameof(AdaHalamanBerikut));
        return MuatAsync();
    }

    [RelayCommand]
    private Task HalamanBerikutnyaAsync()
    {
        if (!AdaHalamanBerikut) return Task.CompletedTask;
        Halaman++;
        OnPropertyChanged(nameof(AdaHalamanBerikut));
        return MuatAsync();
    }

    /// <summary>Ekspor memakai mesin yang sama dengan laporan lain, jadi angkanya tidak bisa berbeda format.</summary>
    [RelayCommand]
    private Task EksporAsync(string? format) => JalankanAsync(async () =>
    {
        var dipili = (format ?? "csv").ToLowerInvariant();
        var dialog = new SaveFileDialog
        {
            Title = "Simpan jejak audit",
            FileName = $"audit-{DateTime.Now:yyyyMMdd-HHmm}.{dipili}",
            Filter = dipili switch
            {
                "pdf" => "Berkas PDF|*.pdf",
                "xlsx" => "Berkas Excel|*.xlsx",
                _ => "Berkas CSV|*.csv",
            },
        };
        if (dialog.ShowDialog() != true) return;

        var hasil = await Konteks.Api.UnduhAsync($"api/reports/export?jenis=audit&format={dipili}&{Filter()}");
        await File.WriteAllBytesAsync(dialog.FileName, hasil.Isi);
        Beres($"{hasil.Isi.Length:N0} byte disimpan di {dialog.FileName}");
    });

    private string Filter()
    {
        var dari = (Dari ?? DateTime.Today).ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sampai = (Sampai ?? DateTime.Today).ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"dari={dari}&sampai={sampai}&halaman={Halaman}&ukuran={UkuranHalaman}";
        if (!string.IsNullOrWhiteSpace(Cari)) query += $"&kata={Uri.EscapeDataString(Cari.Trim())}";
        if (!string.IsNullOrWhiteSpace(Entitas)) query += $"&entitas={Uri.EscapeDataString(Entitas.Trim())}";
        return query;
    }
}
