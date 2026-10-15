using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Beranda pelanggan: saldo, total tagihan berjalan, dan pesanan yang masih hidup. Angkanya datang
/// dari endpoint dashboard yang sudah dikhususkan untuk peran ini — tidak ada angka milik toko.
/// </summary>
public partial class BerandaPelangganViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    [ObservableProperty] private IReadOnlyList<OrderListItem> _aktif = [];
    [ObservableProperty] private string _saldo = "Rp0";
    [ObservableProperty] private string _tagihan = "Rp0";
    [ObservableProperty] private string _catatan = "";

    public override string Judul => "Beranda";

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var d = await Konteks.Api.IsiAsync<CustomerDashboardDto>("api/orders/dashboard/customer");
        Saldo = Format.Rupiah(d.SaldoKredit);
        Tagihan = Format.Rupiah(d.TotalTagihan);
        Aktif = d.PesananAktif;
        Catatan = d.PesananAktif.Count == 0
            ? "Belum ada pesanan yang sedang berjalan."
            : $"{d.PesananAktif.Count} pesanan masih dalam proses.";
    });

    [RelayCommand]
    private void Buka(string judul) => Konteks.BukaMenu(judul);
}
