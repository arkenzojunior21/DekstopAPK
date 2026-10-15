using System.Windows;
using System.Windows.Media;

namespace Laundry.Tests.Desktop;

/// <summary>
/// Tema ditukar dengan mengganti satu dictionary pada runtime dan semua kontrol membaca
/// DynamicResource. Kalau satu kunci hilang di tema gelap tidak ada galat yang muncul, kontrolnya
/// sekadar kehilangan warna. Test ini menjaga kedua tema punya kunci yang sama persis.
/// </summary>
public sealed class TemaSamaKunciTest
{
    [Theory]
    [InlineData("Terang")]
    [InlineData("Gelap")]
    public void SetiapTemaPunyaKunciYangSama(string nama) => JalankanDiSta.Aksi(() =>
    {
        var kunciLain = BacaKunci(nama);
        var kunciAcuan = BacaKunci("Terang");
        Assert.True(kunciLain.SetEquals(kunciAcuan),
            $"Tema {nama} berbeda dari Terang: kurang {kunciAcuan.Except(kunciLain)}, " +
            $"lebih {kunciLain.Except(kunciAcuan)}.");
    });

    [Fact]
    public void TemaGelapMenyediakanKuasUtama() => JalankanDiSta.Aksi(() =>
    {
        var kamus = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Laundry.Desktop;component/Themes/Gelap.xaml"),
        };
        foreach (var kunci in new[] { "KuasLatar", "KuasPermukaan", "KuasTeks", "KuasAksen", "KuasSisi" })
        {
            Assert.IsAssignableFrom<Brush>(kamus[kunci]);
        }
    });

    private static IReadOnlySet<string> BacaKunci(string nama) => new ResourceDictionary
    {
        Source = new Uri($"pack://application:,,,/Laundry.Desktop;component/Themes/{nama}.xaml"),
    }.Keys.Cast<string>().ToHashSet();
}
