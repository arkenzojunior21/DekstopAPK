using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Laundry.Desktop.Services;
using Xunit;

namespace Laundry.Tests.Desktop;

/// <summary>
/// Pesan "belum ada data" hanya berguna kalau ia benar-benar menghilang begitu baris pertama masuk.
/// Dicatat di sini supaya layar list tidak lagi memakai pengikatan yang macet di nilai awal.
/// </summary>
public class PesanKosongTest
{
    private sealed class Tandon
    {
        public ObservableCollection<string> Daftar { get; } = [];
    }

    private static (Visibility Awal, Visibility Sesudah) Ikuti(string path) => JalankanDiSta.Nilai(() =>
    {
        var teks = new TextBlock();
        var tandon = new Tandon();
        teks.SetBinding(UIElement.VisibilityProperty, new Binding(path)
        {
            Source = tandon,
            Converter = new KosongConverter(),
            ConverterParameter = "balik",
        });
        var awal = teks.Visibility;
        tandon.Daftar.Add("satu");
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        return (awal, teks.Visibility);
    });

    [Fact]
    public void BindingKeKoleksiTanpaNotifikasiMacet()
    {
        var hasil = Ikuti("Daftar");
        Assert.Equal(Visibility.Visible, hasil.Awal);
        Assert.Equal(Visibility.Visible, hasil.Sesudah);
    }

    [Fact]
    public void BindingKeCountMengikutiPerubahanDaftar()
    {
        var hasil = Ikuti("Daftar.Count");
        Assert.Equal(Visibility.Visible, hasil.Awal);
        Assert.Equal(Visibility.Collapsed, hasil.Sesudah);
    }
}
