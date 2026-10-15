using System.Windows;
using System.Windows.Controls;

namespace Laundry.Tests.Desktop;

/// <summary>
/// XAML hanya terbukti benar saat benar-benar dimuat: StaticResource dengan kunci salah baru
/// melempar galat ketika jendela dibuka. Test ini memuat seluruh layar di kedua tema supaya typo
/// resource atau template rusak ketahuan saat build, bukan saat aplikasi dijalankan.
/// </summary>
public sealed class TampilanTerbacaTest
{
    private static readonly string[] NamaLayar =
    [
        "AuditView", "BerandaAdminView", "BerandaPelangganView", "KatalogView", "KomplainView",
        "LaporanView", "PelangganView", "PesanBaruView", "PesananSayaView", "PesananView",
        "SistemView", "SlotView",
    ];

    [Theory]
    [InlineData("Terang")]
    [InlineData("Gelap")]
    public void SemuaLayarTerbacaDiKeduaTema(string tema) => JalankanDiSta.Aksi(() =>
    {
        PasangTema(tema);
        var assembly = typeof(Laundry.Desktop.Views.AuditView).Assembly;
        foreach (var nama in NamaLayar)
        {
            var tipe = assembly.GetType($"Laundry.Desktop.Views.{nama}")
                       ?? throw new InvalidOperationException($"{nama} tidak terdaftar.");
            var layar = (UserControl)Activator.CreateInstance(tipe)!;
            Assert.True(layar.Content is not null, $"{nama} pada tema {tema} terbaca kosong.");
        }
    });

    private static void PasangTema(string tema)
    {
        var sumber = System.Windows.Application.Current!.Resources;
        sumber.MergedDictionaries.Clear();
        foreach (var nama in new[] { "Kontrol", tema })
        {
            sumber.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Laundry.Desktop;component/Themes/{nama}.xaml"),
            });
        }
    }
}
