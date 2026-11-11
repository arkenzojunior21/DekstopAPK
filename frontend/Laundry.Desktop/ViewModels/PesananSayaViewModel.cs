using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;
using Laundry.Domain;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Daftar pesanan milik pelanggan yang sedang login. Endpointnya `api/orders/mine` dan server
/// membatasi hasilnya ke profile pelanggan ini, jadi tidak ada jalur untuk melihat pesanan orang lain.
/// </summary>
public partial class PesananSayaViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    public ObservableCollection<OrderListItem> Daftar { get; } = [];

    public ObservableCollection<BarisItem> Items { get; } = [];

    [ObservableProperty] private OrderListItem? _terpilih;
    [ObservableProperty] private OrderDetailDto? _detail;
    [ObservableProperty] private IReadOnlyList<PaymentDto> _pembayaran = [];
    [ObservableProperty] private string _alasanBatal = "";
    [ObservableProperty] private int _halaman;
    [ObservableProperty] private bool _adaHalamanBerikut;

    private const int UkuranHalaman = 20;

    public override string Judul => "Pesanan saya";

    public bool AdaTerpilih => Terpilih is not null;

    /// <summary>Sama dengan aturan server: setelah cucian masuk toko, pembatalan bukan lagi hak pelanggan.</summary>
    public bool BisaAjukanBatal => Detail is not null
        && Detail.Status != OrderStatus.Dibatalkan
        && !OrderStatusAlur.SudahMulaiDiproses(Detail.Status);

    partial void OnTerpilihChanged(OrderListItem? value)
    {
        OnPropertyChanged(nameof(AdaTerpilih));
        if (value is not null) _ = BukaDetailAsync(value);
    }

    partial void OnDetailChanged(OrderDetailDto? value) => OnPropertyChanged(nameof(BisaAjukanBatal));

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var daftar = await Konteks.Api.IsiAsync<List<OrderListItem>>(
            $"api/orders/mine?page={Halaman}&pageSize={UkuranHalaman}");
        var idTerpilih = Terpilih?.Id;
        Daftar.Clear();
        foreach (var baris in daftar) Daftar.Add(baris);
        AdaHalamanBerikut = daftar.Count == UkuranHalaman;
        Terpilih = Daftar.FirstOrDefault(d => d.Id == idTerpilih) ?? Daftar.FirstOrDefault();
    });

    [RelayCommand]
    private Task HalamanSebelumnyaAsync()
    {
        if (Halaman == 0) return Task.CompletedTask;
        Halaman--;
        return MuatAsync();
    }

    [RelayCommand]
    private Task HalamanBerikutnyaAsync()
    {
        if (!AdaHalamanBerikut) return Task.CompletedTask;
        Halaman++;
        return MuatAsync();
    }

    private async Task BukaDetailAsync(OrderListItem item)
    {
        await JalankanAsync(async () =>
        {
            var detail = await Konteks.Api.IsiAsync<OrderDetailDto>($"api/orders/{item.Id}");
            Detail = detail;
            Items.Clear();
            foreach (var baris in detail.Items) Items.Add(BarisItem.Dari(baris));
            Pembayaran = await Konteks.Api.IsiAsync<List<PaymentDto>>($"api/orders/{item.Id}/payments");
        });
    }

    [RelayCommand]
    private Task AjukanBatalAsync()
    {
        if (Detail is null || string.IsNullOrWhiteSpace(AlasanBatal)) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<OrderDetailDto>($"api/orders/{Detail.Id}/cancel-request",
                new { reason = AlasanBatal.Trim() });
            Detail = hasil;
            AlasanBatal = "";
            await MuatAsync();
            Beres("Permintaan pembatalan dikirim. Admin toko yang memutuskan.");
        });
    }

    [RelayCommand]
    private async Task TampilkanStrukAsync()
    {
        if (Detail is null) return;
        await JalankanAsync(async () =>
        {
            var struk = await Konteks.Api.IsiAsync<ReceiptDto>($"api/orders/{Detail.Id}/receipt");
            Views.JendelaStruk.Buka(struk);
        });
    }
}
