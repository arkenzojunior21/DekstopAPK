using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Laundry.Desktop.Services;

/// <summary>
/// Pencari berkas aset di folder "Aset" di sebelah exe. Admin cukup menaruh/mengganti
/// berkas di situ (logo.png, Layanan/&lt;nama&gt;.png) tanpa perlu build ulang.
/// </summary>
public static class Aset
{
    public static string Folder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Aset");

    public static string? Jalur(string namaRelatif)
    {
        if (string.IsNullOrWhiteSpace(namaRelatif)) return null;
        var jalur = Path.GetFullPath(Path.Combine(Folder, namaRelatif));
        // Tolak keluar dari folder Aset (nama berkas dari data pelanggan bisa jadi aneh).
        if (!jalur.StartsWith(Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(jalur) ? jalur : null;
    }

    /// <summary>"Cuci Setrika (CONTOH)" → "cuci-setrika-contoh": nama berkas foto layanan.</summary>
    public static string NamaBerkasLayanan(string namaLayanan)
    {
        var karakter = namaLayanan.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var bersih = new string(karakter).Trim('-');
        while (bersih.Contains("--")) bersih = bersih.Replace("--", "-");
        return $"{bersih}.png";
    }
}

/// <summary>Parameter "logo.png" → gambar dari folder Aset; null bila berkas tidak ada.</summary>
public sealed class AsetGambarConverter : IValueConverter
{
    public object? Convert(object? value, Type _, object? parameter, CultureInfo __)
    {
        var nama = parameter as string ?? value as string;
        return Aset.Jalur(nama ?? "") is { } jalur ? new BitmapImage(new Uri(jalur)) : null;
    }

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}

/// <summary>Nama layanan → foto di Aset/Layanan/&lt;slug&gt;.png; null bila belum ada fotonya.</summary>
public sealed class FotoLayananConverter : IValueConverter
{
    public object? Convert(object? value, Type _, object? parameter, CultureInfo __) =>
        value is string nama && Aset.Jalur(Path.Combine("Layanan", Aset.NamaBerkasLayanan(nama))) is { } jalur
            ? new BitmapImage(new Uri(jalur))
            : null;

    public object ConvertBack(object? value, Type _, object? __, CultureInfo ___) =>
        throw new NotSupportedException();
}
