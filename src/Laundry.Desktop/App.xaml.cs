using System.Windows;
using System.Windows.Threading;
using Laundry.Desktop.Services;
using Laundry.Desktop.ViewModels;
using Laundry.Desktop.Views;

namespace Laundry.Desktop;

/// <summary>
/// Titik komposisi aplikasi desktop. Hanya ada satu KlienApi dan satu Sesi untuk seluruh jendela:
/// token tidak pernah ditulis ke disk, jadi membuka aplikasi kembali selalu berarti masuk kembali.
/// </summary>
/// <remarks>
/// Nama kelas dasar ditulis lengkap karena namespace Laundry.Application membuat identitas singkat
/// Application terbaca sebagai namespace, bukan kelas WPF.
/// </remarks>
public partial class App : System.Windows.Application
{
    private JendelaLogin? _login;
    private JendelaUtama? _utama;

    public static App Instance { get; private set; } = null!;

    public Preferensi Preferensi { get; private set; } = new();

    public KlienApi Api { get; private set; } = null!;

    public Sesi Sesi { get; private set; } = new();

    public Konteks Konteks { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Instance = this;
        DispatcherUnhandledException += PadaGalatMendadak;

        if (Environment.GetEnvironmentVariable("WPF_PERANGKAT_LUNAK") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        Preferensi = Preferensi.Baca();
        var server = Environment.GetEnvironmentVariable("API_URL") ?? Preferensi.Server;
        Api = new KlienApi(server);
        Api.SesiBerakhir += PadaSesiBerakhir;
        Konteks = new Konteks { Api = Api, Sesi = Sesi, Preferensi = Preferensi };
        Tema.Terapkan(Preferensi.Tema);

        BukaLogin("Sesi berakhir di server. Silakan masuk lagi.");
    }

    private void PadaGalatMendadak(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(this.MainWindow,
            $"Aksi terhenti: {e.Exception.Message}\n\nData yang sudah tersimpan di server tidak diubah.",
            "Laundry", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private void PadaSesiBerakhir() => BukaLogin("Sesi berakhir. Silakan masuk lagi.");

    /// <summary>Kembali ke layar login, menutup jendela utama bila masih terbuka.</summary>
    public void BukaLogin(string? pesan = null)
    {
        _utama?.Close();
        _utama = null;
        Sesi.Bersihkan();
        Api.KosongkanToken();

        _login ??= new JendelaLogin();
        if (_login.DataContext is LoginViewModel model) model.PesanUmum = pesan;
        _login.Show();
        ActivateWindow(_login);
    }

    /// <summary>Masuk shell. Peran dari server menentukan menu yang ditampilkan, bukan pilihan pengguna.</summary>
    public void BukaUtama()
    {
        _login?.Hide();
        _utama ??= new JendelaUtama();
        _utama.MuatUlang();
        _utama.Show();
        ActivateWindow(_utama);
    }

    private void ActivateWindow(Window jendela)
    {
        MainWindow = jendela;
        jendela.Activate();
    }

    public void GantiTema()
    {
        Preferensi.Tema = Tema.SaatIni == Tema.Terang ? Tema.Gelap : Tema.Terang;
        Preferensi.Simpan();
        Tema.Terapkan(Preferensi.Tema);
    }
}
