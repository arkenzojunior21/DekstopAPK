using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Threading;
using Laundry.Desktop.ViewModels;

namespace Laundry.Desktop.Views;

public partial class LaporanView : UserControl
{
    private INotifyPropertyChanged? _deretDipantau;

    public LaporanView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PasangDeret();
    }

    // Omzet biasanya baru ada di hari-hari terakhir; tanpa digulir grafiknya terlihat kosong.
    private void PasangDeret()
    {
        if (_deretDipantau is not null) _deretDipantau.PropertyChanged -= SaatDeretBerubah;
        _deretDipantau = DataContext as INotifyPropertyChanged;
        if (_deretDipantau is null) return;
        _deretDipantau.PropertyChanged += SaatDeretBerubah;
    }

    private void SaatDeretBerubah(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LaporanViewModel.Deret)) return;
        Dispatcher.BeginInvoke(
            () => Grafik.ScrollToHorizontalOffset(Grafik.ScrollableWidth), DispatcherPriority.Loaded);
    }
}
