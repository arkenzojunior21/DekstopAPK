using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Laundry.Desktop.Services;

/// <summary>
/// Bingkai judul (title bar) Windows mengikuti tema aplikasi. Cukup memanggil DWM;
/// tidak ada WindowChrome kustom supaya tombol min/max/close tetap bawaan sistem.
/// </summary>
public static class BingkaiJendela
{
    private const int GelapImersif = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr jendela, int atribut, ref int nilai, int ukuran);

    public static void Setel(Window jendela)
    {
        var sumber = new WindowInteropHelper(jendela).Handle;
        if (sumber == IntPtr.Zero) return;
        var gelap = Tema.SaatIni == Tema.Gelap ? 1 : 0;
        DwmSetWindowAttribute(sumber, GelapImersif, ref gelap, sizeof(int));
    }
}
