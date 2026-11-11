using System.Windows;
using Laundry.Desktop.ViewModels;

namespace Laundry.Desktop.Views;

public partial class JendelaLogin : Window
{
    public JendelaLogin()
    {
        InitializeComponent();
        DataContext = new LoginViewModel();
    }

    // PasswordBox sengaja tidak bisa di-bind: sandi tidak pernah tinggal di memori binding.
    private void KotakSandi_Changed(object sender, RoutedEventArgs e) =>
        ((LoginViewModel)DataContext).Sandi = KotakSandi.Password;

    private void KotakSandiBaru_Changed(object sender, RoutedEventArgs e) =>
        ((LoginViewModel)DataContext).SandiBaru = KotakSandiBaru.Password;

    private void KotakSandiReset_Changed(object sender, RoutedEventArgs e) =>
        ((LoginViewModel)DataContext).SandiBaru = KotakSandiReset.Password;
}
