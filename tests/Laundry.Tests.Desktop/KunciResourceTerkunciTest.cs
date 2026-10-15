using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace Laundry.Tests.Desktop;

/// <summary>
/// DynamicResource yang kuncinya salah tidak melempar galat: kontrolnya diam-diam kehilangan warna
/// atau style. Test ini membandingkan setiap kunci yang dipakai di seluruh XAML dengan kunci yang
/// benar-benar tersedia di Kontrol.xaml + Terang.xaml, jadi typo tertangkap saat build.
/// </summary>
public sealed class KunciResourceTerkunciTest
{
    private static readonly Regex Pakai = new(@"\{(?:Dynamic|Static)Resource\s+([A-Za-z_]\w*)\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex Punya = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void SemuaKunciYangDipakaiTersedia()
    {
        var akar = CariAkar();
        var tersedia = JalankanDiSta.Nilai(() =>
        {
            var kunci = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nama in new[] { "Kontrol", "Terang" })
            {
                var kamus = new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/Laundry.Desktop;component/Themes/{nama}.xaml"),
                };
                foreach (var entry in kamus.Keys.Cast<object>().Select(k => k.ToString()!))
                    kunci.Add(entry);
            }

            return kunci;
        });

        var folder = Path.Combine(akar, "src", "Laundry.Desktop");
        var hilang = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var berkas in Directory.EnumerateFiles(folder, "*.xaml", SearchOption.AllDirectories)
                     .Where(b => !b.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !b.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var teks = File.ReadAllText(berkas);
            var lokal = Punya.Matches(teks).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            foreach (var pakai in Pakai.Matches(teks).Select(m => m.Groups[1].Value).Distinct())
            {
                if (lokal.Contains(pakai) || tersedia.Contains(pakai)) continue;
                hilang.Add($"{Path.GetFileName(berkas)} → {pakai}");
            }
        }

        Assert.True(hilang.Count == 0,
            $"Kunci resource tidak ditemukan:{Environment.NewLine}{string.Join(Environment.NewLine, hilang)}");
    }

    private static string CariAkar()
    {
        var jalankan = new DirectoryInfo(AppContext.BaseDirectory);
        while (jalankan is not null && !File.Exists(Path.Combine(jalankan.FullName, "Laundry.sln")))
            jalankan = jalankan.Parent;
        return jalankan?.FullName ?? throw new InvalidOperationException("Laundry.sln tidak ditemukan.");
    }
}
