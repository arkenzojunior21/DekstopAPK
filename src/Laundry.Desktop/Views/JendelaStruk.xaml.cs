using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Laundry.Application.Core;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.Views;

/// <summary>
/// Struk pesanan. Wajahnya sengaja selalu putih-hitam supaya hasil cetak identik dengan yang dilihat,
/// dan isinya murni hasil GET /api/orders/{id}/receipt — angka tidak dihitung ulang di sini.
/// </summary>
public partial class JendelaStruk : Window
{
    public JendelaStruk(ReceiptDto struk)
    {
        InitializeComponent();
        DataContext = struk;
        JudulUsaha.Text = string.IsNullOrWhiteSpace(struk.BusinessName)
            ? "Nama usaha belum diisi (menu Sistem)"
            : struk.BusinessName!;
        foreach (var baris in struk.Lines) TambahBaris(baris);
        foreach (var bayar in struk.Payments) TambahPembayaran(bayar);
    }

    public static void Buka(ReceiptDto struk)
    {
        var jendela = new JendelaStruk(struk) { Owner = System.Windows.Application.Current?.MainWindow };
        jendela.Show();
    }

    private void TambahBaris(ReceiptLineDto baris)
    {
        IsiItem.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 2, 0, 2),
            Text = $"{baris.Nama} · {Format.Kuantitas(baris.Jumlah)} {baris.Satuan} × {Format.Rupiah(baris.Harga)}" +
                   $" = {Format.Rupiah(baris.Subtotal)}",
        });
    }

    private void TambahPembayaran(ReceiptPaymentDto bayar)
    {
        IsiPembayaran.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 2, 0, 2),
            Text = $"{Format.Label(bayar.Method)} · {Format.Rupiah(bayar.Amount)} · {Format.Label(bayar.Status)}",
        });
    }

    private void Tutup_Click(object sender, RoutedEventArgs e) => Close();

    private void Cetak_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true) dialog.PrintVisual(Kertas, "Struk pesanan");
    }
}
