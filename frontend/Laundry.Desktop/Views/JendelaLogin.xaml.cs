using System.Windows;
using Laundry.Desktop.Services;
using Laundry.Desktop.ViewModels;

namespace Laundry.Desktop.Views;

public partial class JendelaLogin : Window
{
    public JendelaLogin()
    {
        InitializeComponent();
        DataContext = new LoginViewModel();
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            if (Width > area.Width || Height > area.Height)
            {
                Width = Math.Min(Width, area.Width);
                Height = Math.Min(Height, area.Height);
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = area.Left + (area.Width - Width) / 2;
                Top = area.Top + (area.Height - Height) / 2;
            }
            BingkaiJendela.Setel(this);
            Tema.Berubah += SetelBingkai;
        };
        Closed += (_, _) => Tema.Berubah -= SetelBingkai;
    }

    private void SetelBingkai() => BingkaiJendela.Setel(this);

    // PasswordBox sengaja tidak bisa di-bind: sandi tidak pernah tinggal di memori binding.
    private void KotakSandi_Changed(object sender, RoutedEventArgs e) =>
        ((LoginViewModel)DataContext).Sandi = KotakSandi.Password;

    private void KotakSandiBaru_Changed(object sender, RoutedEventArgs e) =>
        ((LoginViewModel)DataContext).SandiBaru = KotakSandiBaru.Password;

    private void KotakSandiReset_Changed(object sender, RoutedEventArgs e) =>
        ((LoginViewModel)DataContext).SandiBaru = KotakSandiReset.Password;
}
