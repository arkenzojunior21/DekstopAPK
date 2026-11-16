using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Laundry.Desktop.Services;
using Laundry.Desktop.ViewModels;

namespace Laundry.Desktop.Views;

public partial class JendelaUtama : Window
{
    // Di bawah ambang ini sidebar menyempit jadi rel ikon; dipilih 1080 supaya laptop
    // 1366×768 (area kerja ~1300 lebar setelah taskbar) masih memakai sidebar penuh.
    private const double AmbangSempit = 1080;

    private readonly DispatcherTimer _penghapusToast = new() { Interval = TimeSpan.FromSeconds(6) };

    public JendelaUtama()
    {
        InitializeComponent();
        _penghapusToast.Tick += (_, _) =>
        {
            _penghapusToast.Stop();
            KartuToast.Visibility = Visibility.Collapsed;
        };
        Loaded += (_, _) =>
        {
            BatasiPadaAreaKerja();
            BingkaiJendela.Setel(this);
            Tema.Berubah += SetelBingkai;
            TerapkanModeSempit();
        };
        Closed += (_, _) => Tema.Berubah -= SetelBingkai;
        SizeChanged += (_, _) => TerapkanModeSempit();
    }

    /// <summary>Dipanggil setiap kali sesi baru dimulai, supaya menu dibangun ulang sesuai peran.</summary>
    public void MuatUlang()
    {
        if (DataContext is ShellViewModel lama) lama.PropertyChanged -= PadaPerubahan;
        DataContext = new ShellViewModel();
        ((ShellViewModel)DataContext).PropertyChanged += PadaPerubahan;
        TerapkanModeSempit();
    }

    private void SetelBingkai() => BingkaiJendela.Setel(this);

    /// <summary>Jendela dipulihkan dari ukuran tersimpan; jangan biarkan lebih besar dari area kerja.</summary>
    private void BatasiPadaAreaKerja()
    {
        var area = SystemParameters.WorkArea;
        if (WindowState != WindowState.Maximized && (Width > area.Width || Height > area.Height))
        {
            Width = Math.Min(Width, area.Width);
            Height = Math.Min(Height, area.Height);
        }
    }

    private void TerapkanModeSempit()
    {
        if (DataContext is not ShellViewModel model) return;
        model.Sempit = ActualWidth < AmbangSempit;
        KolomSisi.Width = new GridLength(model.Sempit ? 72 : 236);
    }

    private void PadaPerubahan(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.Toast) || sender is not ShellViewModel model) return;
        if (string.IsNullOrEmpty(model.Toast)) return;
        IsiToast.Text = model.Toast;
        KartuToast.Visibility = Visibility.Visible;
        _penghapusToast.Stop();
        _penghapusToast.Start();
        model.Toast = null;
    }
}
