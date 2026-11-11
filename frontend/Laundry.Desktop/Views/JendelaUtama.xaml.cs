using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Laundry.Desktop.ViewModels;

namespace Laundry.Desktop.Views;

public partial class JendelaUtama : Window
{
    private readonly DispatcherTimer _penghapusToast = new() { Interval = TimeSpan.FromSeconds(6) };

    public JendelaUtama()
    {
        InitializeComponent();
        _penghapusToast.Tick += (_, _) =>
        {
            _penghapusToast.Stop();
            KartuToast.Visibility = Visibility.Collapsed;
        };
    }

    /// <summary>Dipanggil setiap kali sesi baru dimulai, supaya menu dibangun ulang sesuai peran.</summary>
    public void MuatUlang()
    {
        if (DataContext is ShellViewModel lama) lama.PropertyChanged -= PadaPerubahan;
        DataContext = new ShellViewModel();
        ((ShellViewModel)DataContext).PropertyChanged += PadaPerubahan;
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
