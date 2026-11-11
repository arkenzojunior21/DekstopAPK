using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Laundry.Desktop.Services;

/// <summary>Format uang: "Rp20.000". Parameter "kosong" menampilkan "-" untuk nilai null.</summary>
public sealed class RupiahConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? parameter, CultureInfo __) => value switch
    {
        null => parameter as string == "kosong" ? "-" : "Rp0",
        decimal desimal => Format.Rupiah(desimal),
        double g => Format.Rupiah((decimal)g),
        int i => Format.Rupiah(i),
        _ => value.ToString() ?? "-",
    };

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Format tanggal/jam/waktu relatif. Parameter: "tgl" (bawaan), "jam", "lengkap", "relatif".</summary>
public sealed class TanggalConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? parameter, CultureInfo __) =>
        (parameter as string) switch
        {
            "jam" => Format.Jam(value),
            "lengkap" => Format.TanggalJam(value),
            "relatif" => Format.Relatif(value),
            _ => Format.Tanggal(value),
        };

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Enum status → label Indonesia yang bisa dibaca kasir ("SiapDiambil" → "Siap Diambil").</summary>
public sealed class LabelConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? __, CultureInfo ___) => Format.Label(value);

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Kuantitas (kg/item) dengan desimal yang tidak dipaksa jadi rupiah.</summary>
public sealed class KuantitasConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? __, CultureInfo ___) => value switch
    {
        null => "-",
        decimal desimal => Format.Kuantitas(desimal),
        _ => value.ToString() ?? "-",
    };

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>
/// Warna badge dari nilai status. Empat makna saja (netral / proses / siap / masalah) supaya
/// kasir cukup membaca warna tanpa menghafal 14 nama status pesanan.
/// </summary>
public sealed class KuasStatusConverter : IValueConverter
{
    private static readonly string[] Siap =
        ["Selesai", "Lunas", "Terkirim", "RefundPenuh", "SudahDibayar", "Cocok", "sudah_terkini", "versi_sudah_terkini"];

    private static readonly string[] Proses =
        ["Dikonfirmasi", "MenungguPenjemputan", "DiterimaDiLaundry", "DitimbangDanDiverifikasi", "MenungguProses",
            "SedangDiproses", "Pengeringan", "Penyetrikaan", "QualityCheck", "SiapDiambil", "DalamPengantaran",
            "Ditinjau", "Ditangani", "SebagianDibayar", "DpSebagian", "MenungguKonfirmasi", "versi_baru_tersedia",
            "TerkirimSebagian", "tersedia"];

    private static readonly string[] Masalah =
        ["Dibatalkan", "Gagal", "GagalAtauKedaluwarsa", "RefundSebagian", "Ditolak", "MenungguInformasi",
            "DitundaPerluKoneksi", "belum_dikonfigurasi", "manifest_tidak_sah", "gagal_diperiksa", "tidak_cocok",
            "nama_berkas_ditolak", "berkas_tidak_ada"];

    public object Convert(object? value, Type _, object? __, CultureInfo ___)
    {
        var teks = value as string ?? value?.ToString() ?? "";
        if (Siap.Contains(teks)) return Tema.Kuas("KuasSukses");
        if (Proses.Contains(teks)) return Tema.Kuas("KuasProses");
        if (Masalah.Contains(teks)) return Tema.Kuas("KuasBahaya");
        return Tema.Kuas("KuasNetral");
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Teks/daftar kosong → elemen disembunyikan, dipakai untuk state kosong dan error.</summary>
public sealed class KosongConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? parameter, CultureInfo __)
    {
        var kosong = value switch
        {
            null => true,
            string teks => string.IsNullOrWhiteSpace(teks),
            int jumlah => jumlah == 0,
            System.Collections.IEnumerable daftar => !daftar.Cast<object?>().Any(),
            _ => false,
        };

        var balik = parameter as string == "balik";
        return kosong ^ balik ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Bool → Visibility. Parameter "balik" membalik maknanya.</summary>
public sealed class BoolKasusConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? parameter, CultureInfo __)
    {
        var nilai = value is true;
        if (parameter as string == "balik") nilai = !nilai;
        return nilai ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Balik bool, untuk checkbox "nonaktif" yang melawan properti "aktif".</summary>
public sealed class NegasiConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? __, CultureInfo ___) => value is not true;

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) => value is not true;
}

/// <summary>
/// Bool → teks Indonesia, supaya kolom status tidak menampilkan "True"/"False" di depan kasir.
/// Parameter opsional "Benar|Salah", misalnya "Aktif|Nonaktif" atau "Berjalan|Tidak berjalan".
/// </summary>
public sealed class YaTidakConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? parameter, CultureInfo __)
    {
        var (benar, salah) = (parameter as string) switch
        {
            { } teks when teks.Split('|') is [var b, var s] => (b, s),
            _ => ("Ya", "Tidak"),
        };
        return value switch
        {
            bool flag => flag ? benar : salah,
            string teks => bool.TryParse(teks, out var parsed) && parsed ? benar : salah,
            _ => "-",
        };
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Penggabungan: true hanya bila kedua sisi true (dipakai tombol yang butuh seleksi + izin).</summary>
public sealed class DanConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type _, object? __, CultureInfo ___) =>
        values.All(v => v is true);

    public object[] ConvertBack(object value, Type[] _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>
/// Angka + bilangan tetap. Halaman internal nol-based, tapi pengguna membaca "Halaman 1" untuk
/// halaman pertama.
/// </summary>
public sealed class TambahConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? parameter, CultureInfo __)
    {
        var tambah = parameter is string teks && int.TryParse(teks, out var n) ? n : 1;
        return value is int angka ? angka + tambah : value?.ToString() ?? "-";
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Isolasi agar satu baris grid bisa mengisi lebar sesuai jumlah kolom dinamis laporan.</summary>
public sealed class LebarKolomConverter : IValueConverter
{
    public object Convert(object? value, Type _, object? parameter, CultureInfo __)
    {
        var total = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var bagian = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
        return total <= 0 ? 1d : Math.Max(bagian / total, 0.02);
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Mengubah byte hasil ekspor menjadi gambar di layar (pratinjau struk/QR tidak dipakai di sini).</summary>
public static class Ikon
{
    /// <summary>Glyph tunggal dari font simbol bawaan Windows; aman tanpa aset tambahan.</summary>
    public static string Glyph(string nama) => nama switch
    {
        "beranda" => "\uE80F",
        "pesanan" => "\uE7B8",
        "layanan" => "\uE8FD",
        "pelanggan" => "\uE716",
        "slot" => "\uE787",
        "komplain" => "\uE8BD",
        "laporan" => "\uE9D9",
        "sistem" => "\uE770",
        "tema" => "\uE706",
        "keluar" => "\uE7E8",
        "segar" => "\uE72C",
        "cari" => "\uE721",
        "tambah" => "\uE710",
        "unduh" => "\uE896",
        "cek" => "\uE73E",
        "peringatan" => "\uE7BA",
        _ => "\uE700",
    };
}
