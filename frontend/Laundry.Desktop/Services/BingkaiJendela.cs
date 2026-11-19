using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Laundry.Desktop.Services;

/// <summary>
/// Bingkai judul (title bar) Windows mengikuti tema aplikasi. Cukup memanggil DWM;
/// tidak ada WindowChrome kustom supaya tombol min/max/close tetap bawaan sistem.
/// </summary>
public static class BingkaiJendela
{
    private const int GelapImersif = 20;
    private const int WarnaJudul = 35;
    private const int WarnaTeksJudul = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr jendela, int atribut, ref int nilai, int ukuran);

    public static void Setel(Window jendela)
    {
        var sumber = new WindowInteropHelper(jendela).Handle;
        if (sumber == IntPtr.Zero) return;
        var gelap = Tema.SaatIni == Tema.Gelap ? 1 : 0;
        DwmSetWindowAttribute(sumber, GelapImersif, ref gelap, sizeof(int));

        // Tanpa ini Windows memakai warna aksen sistem di bilah judul, sehingga
        // jendela tidak menyatu dengan latar aplikasi.
        if (Kuas("KuasLatar") is { } latar)
        {
            var warna = WarnaRef(latar);
            DwmSetWindowAttribute(sumber, WarnaJudul, ref warna, sizeof(int));
        }

        if (Kuas("KuasTeks") is { } teks)
        {
            var warna = WarnaRef(teks);
            DwmSetWindowAttribute(sumber, WarnaTeksJudul, ref warna, sizeof(int));
        }
    }

    private static int WarnaRef(Color warna) => warna.R | (warna.G << 8) | (warna.B << 16);

    private static Color? Kuas(string kunci) =>
        System.Windows.Application.Current?.TryFindResource(kunci) is SolidColorBrush kuas ? kuas.Color : null;
}
