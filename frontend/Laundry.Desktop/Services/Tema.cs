using System.Windows;
using System.Windows.Media;

namespace Laundry.Desktop.Services;

/// <summary>
/// Pertukaran tema dilakukan dengan menukar satu dictionary di Application.Resources, bukan dengan
/// menulis ulang warna per kontrol. Semua kontrol membaca kuas lewat DynamicResource, jadi ganti
/// tema langsung berlaku ke seluruh jendela yang sedang terbuka.
/// </summary>
/// <remarks>
/// Application ditulis lengkap karena namespace Laundry.Application membuat identitas singkat
/// Application terbaca sebagai namespace, bukan kelas WPF.
/// </remarks>
public static class Tema
{
    public const string Terang = "Terang";
    public const string Gelap = "Gelap";

    public static string SaatIni { get; private set; } = Terang;

    public static event Action? Berubah;

    public static void Terapkan(string nama)
    {
        var dipilih = nama == Gelap ? Gelap : Terang;
        if (dipilih == SaatIni && AplikasiPunyaTema(dipilih)) return;
        SaatIni = dipilih;

        var kamus = System.Windows.Application.Current.Resources.MergedDictionaries;
        for (var i = kamus.Count - 1; i >= 0; i--)
        {
            var sumber = kamus[i].Source?.OriginalString;
            if (sumber is not null && (sumber.EndsWith($"/{Terang}.xaml", StringComparison.Ordinal) ||
                                       sumber.EndsWith($"/{Gelap}.xaml", StringComparison.Ordinal)))
                kamus.RemoveAt(i);
        }

        kamus.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Laundry.Desktop;component/Themes/{dipilih}.xaml"),
        });
        Berubah?.Invoke();
    }

    private static bool AplikasiPunyaTema(string nama) => System.Windows.Application.Current.Resources.MergedDictionaries
        .Any(k => k.Source?.OriginalString.EndsWith($"/{nama}.xaml", StringComparison.Ordinal) == true);

    /// <summary>Kuas semantik per kondisi; dipakai converter badge supaya warnanya ikut tema.</summary>
    public static Brush Kuas(string kunci) =>
        System.Windows.Application.Current.TryFindResource(kunci) as Brush ?? Brushes.Gray;
}
