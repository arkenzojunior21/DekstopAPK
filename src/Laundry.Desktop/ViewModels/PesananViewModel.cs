using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;
using Laundry.Domain;

namespace Laundry.Desktop.ViewModels;

/// <summary>Baris layanan di dalam pesanan yang berat/jumlah aktualnya boleh diubah kasir.</summary>
public partial class BarisItem(Guid orderItemId, string nama, string satuan, decimal hargaSnapshot,
    decimal? estimasiBerat, decimal? estimasiJumlah) : ObservableObject
{
    [ObservableProperty] private decimal? _beratAktual;
    [ObservableProperty] private decimal? _jumlahAktual;

    public Guid OrderItemId { get; } = orderItemId;
    public string Nama { get; } = nama;
    public string Satuan { get; } = satuan;
    public decimal HargaSnapshot { get; } = hargaSnapshot;
    public decimal? EstimasiBerat { get; } = estimasiBerat;
    public decimal? EstimasiJumlah { get; } = estimasiJumlah;

    public static BarisItem Dari(OrderItemDto item) => new(item.Id, item.ServiceName,
        Format.Label(item.UnitType), item.PriceSnapshot, item.EstimatedWeightKg, item.EstimatedItemCount)
    {
        BeratAktual = item.ActualWeightKg,
        JumlahAktual = item.ActualItemCount,
    };

    public ActualItemInput KeInput() => new(OrderItemId, BeratAktual, JumlahAktual);
}

/// <summary>
/// Halaman operasional pesanan untuk admin: daftar + filter di kiri, detail dan semua aksi di kanan.
/// Tidak ada satu pun angka yang dihitung di sini — transisi status, berat aktual, pembayaran, dan
/// refund dikirim ke API lalu hasilnya dibaca ulang, jadi layar tidak pernah lebih benar dari server.
/// </summary>
public partial class PesananViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    private const int UkuranHalaman = 25;

    public ObservableCollection<OrderListItem> Daftar { get; } = [];

    public ObservableCollection<BarisItem> Items { get; } = [];

    [ObservableProperty] private OrderListItem? _terpilih;
    [ObservableProperty] private OrderDetailDto? _detail;
    [ObservableProperty] private IReadOnlyList<OrderStatus> _langkahBerikut = [];
    [ObservableProperty] private IReadOnlyList<PaymentDto> _pembayaran = [];
    [ObservableProperty] private OrderStatus? _statusBaru;
    [ObservableProperty] private string _CatatanStatus = "";
    [ObservableProperty] private string _cari = "";
    [ObservableProperty] private OrderStatus? _filterStatus;
    [ObservableProperty] private PaymentStatus? _filterPembayaran;
    [ObservableProperty] private DateTime? _dari;
    [ObservableProperty] private DateTime? _sampai;
    [ObservableProperty] private int _halaman;
    [ObservableProperty] private decimal _jumlah;
    [ObservableProperty] private string _CatatanBayar = "";
    [ObservableProperty] private PaymentKind _jenisBayar = PaymentKind.Dp;
    [ObservableProperty] private bool _setujuKredit;
    [ObservableProperty] private decimal _jumlahRefund;
    [ObservableProperty] private string _alasanRefund = "";
    [ObservableProperty] private DateTime? _estimasiSelesai;
    [ObservableProperty] private string _CatatanEstimasi = "";
    [ObservableProperty] private string _kutipanQris = "";
    [ObservableProperty] private string _CatatanBatal = "";
    [ObservableProperty] private bool _adaHalamanBerikut;

    public override string Judul => "Pesanan";

    public bool AdaTerpilih => Terpilih is not null;

    /// <summary>
    /// Keputusan pembatalan selalu ditawarkan selama pesanan belum dibatalkan: kalau sebenarnya tidak
    /// ada permintaan dari pelanggan, server yang menolaknya — UI tidak menebak status internal.
    /// </summary>
    public bool BisaPutuskanBatal => Detail is not null && Detail.Status != OrderStatus.Dibatalkan;

    public IReadOnlyList<OrderStatus> SemuaStatus { get; } =
        (OrderStatus[])Enum.GetValues(typeof(OrderStatus));

    public IReadOnlyList<PaymentStatus> SemuaStatusBayar { get; } =
        (PaymentStatus[])Enum.GetValues(typeof(PaymentStatus));

    public IReadOnlyList<PaymentKind> SumberJenisBayar { get; } =
        (PaymentKind[])Enum.GetValues(typeof(PaymentKind));

    partial void OnTerpilihChanged(OrderListItem? value)
    {
        OnPropertyChanged(nameof(AdaTerpilih));
        if (value is not null) _ = BukaDetailAsync(value);
    }

    [RelayCommand]
    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var daftar = await Konteks.Api.AmbilAsync<List<OrderListItem>>(UrlDaftar()) ?? [];
        Daftar.Clear();
        foreach (var baris in daftar) Daftar.Add(baris);
        AdaHalamanBerikut = daftar.Count == UkuranHalaman;
        if (Terpilih is null && daftar.Count > 0) Terpilih = daftar[0];
    });

    private string UrlDaftar()
    {
        var query = $"api/orders?page={Halaman}&pageSize={UkuranHalaman}";
        if (!string.IsNullOrWhiteSpace(Cari)) query += $"&q={Uri.EscapeDataString(Cari.Trim())}";
        if (FilterStatus is { } s) query += $"&status={s}";
        if (FilterPembayaran is { } p) query += $"&payment={p}";
        if (Dari is { } d) query += $"&from={d.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}";
        if (Sampai is { } t) query += $"&to={t.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}";
        return query;
    }

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
            LangkahBerikut = OrderStateMachine.Next(detail.Status);
            StatusBaru = null;
            Pembayaran = await Konteks.Api.AmbilAsync<List<PaymentDto>>($"api/orders/{item.Id}/payments") ?? [];
            Jumlah = detail.Remaining;
            OnPropertyChanged(nameof(BisaPutuskanBatal));
        });
    }

    [RelayCommand]
    private Task GantiStatusAsync()
    {
        if (Detail is null || StatusBaru is not { } status) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<OrderDetailDto>($"api/orders/{Detail.Id}/transition",
                new { to = status, note = Kosongkan(CatatanStatus) });
            Terapkan(hasil);
            Beres($"Status diubah ke {Format.Label(status)}.");
        });
    }

    [RelayCommand]
    private Task SimpanActualAsync()
    {
        if (Detail is null) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<OrderDetailDto>($"api/orders/{Detail.Id}/actuals",
                new { actuals = Items.Select(i => i.KeInput()).ToList() });
            Terapkan(hasil);
            Beres($"Berat/jumlah aktual tersimpan. Total akhir {Format.Rupiah(hasil.FinalTotal)}.");
        });
    }

    [RelayCommand]
    private Task SetEstimasiAsync()
    {
        if (Detail is null) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<OrderDetailDto>($"api/orders/{Detail.Id}/estimate",
                new { estimatedFinishUtc = EstimasiSelesai, note = Kosongkan(CatatanEstimasi) });
            Terapkan(hasil);
            Beres("Perkiraan selesai diperbarui.");
        });
    }

    [RelayCommand]
    private Task BayarTunaiAsync()
    {
        if (Detail is null || Jumlah <= 0) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<PaymentDto>("api/payments/cash",
                new { orderId = Detail.Id, amount = Jumlah, kind = JenisBayar, note = Kosongkan(CatatanBayar) });
            Beres($"Pembayaran {Format.Rupiah(hasil.Amount)} ({Format.Label(hasil.Kind)}) tercatat " +
                  $"dengan status {Format.Label(hasil.Status)}.");
            await MuatUlangDetailAsync();
        });
    }

    [RelayCommand]
    private Task BayarKreditAsync()
    {
        if (Detail is null || Jumlah <= 0 || !SetujuKredit) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<OrderDetailDto>("api/payments/credit",
                new { orderId = Detail.Id, amount = Jumlah, customerConsent = true });
            Terapkan(hasil);
            Beres($"Saldo kredit dipakai {Format.Rupiah(Jumlah)}.");
        });
    }

    [RelayCommand]
    private Task RefundAsync()
    {
        if (Detail is null || JumlahRefund <= 0 || string.IsNullOrWhiteSpace(AlasanRefund)) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<RefundDto>("api/payments/refund",
                new { orderId = Detail.Id, amount = JumlahRefund, reason = AlasanRefund.Trim() });
            Beres($"Refund {Format.Rupiah(hasil.Amount)} tercatat ({Format.Label(hasil.Status)}).");
            await MuatUlangDetailAsync();
        });
    }

    [RelayCommand]
    private Task KutipQrisAsync()
    {
        if (Detail is null) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<PaymentQuoteDto>("api/payments/gateway/quote",
                new { orderId = Detail.Id, kind = JenisBayar });
            KutipanQris = $"{hasil.GatewayName} · {hasil.Reference} · {Format.Rupiah(hasil.Amount)} " +
                          $"· kedaluwarsa {Format.TanggalJam(hasil.ExpiresAtUtc)} · " +
                          $"{(hasil.Live ? "provider sungguhan" : "SIMULASI: tidak ada dana yang berpindah")}";
            Beres("Tagihan gateway dibuat. Webhook provider masih simulasi.");
            await MuatUlangDetailAsync();
        });
    }

    [RelayCommand]
    private Task PutuskanBatalAsync(object? setuju)
    {
        if (Detail is null) return Task.CompletedTask;
        var approve = setuju as string == "ya";
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<OrderDetailDto>($"api/orders/{Detail.Id}/cancel-decision",
                new { approve, note = Kosongkan(CatatanBatal) });
            Terapkan(hasil);
            Beres(approve ? "Pembatalan disetujui." : "Permintaan pembatalan ditolak.");
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

    private async Task MuatUlangDetailAsync()
    {
        if (Terpilih is { } item) await BukaDetailAsync(item);
        await MuatAsync();
    }

    private void Terapkan(OrderDetailDto hasil)
    {
        Detail = hasil;
        Items.Clear();
        foreach (var baris in hasil.Items) Items.Add(BarisItem.Dari(baris));
        LangkahBerikut = OrderStateMachine.Next(hasil.Status);
        Jumlah = hasil.Remaining;
        OnPropertyChanged(nameof(BisaPutuskanBatal));
        SegarkanBarisDaftar(hasil);
    }

    /// <summary>Baris di daftar ikut diperbarui tanpa menutup detail yang sedang dibuka.</summary>
    private void SegarkanBarisDaftar(OrderDetailDto hasil)
    {
        var baris = Daftar.FirstOrDefault(d => d.Id == hasil.Id);
        if (baris is null) return;
        var index = Daftar.IndexOf(baris);
        Daftar[index] = baris with
        {
            Status = hasil.Status,
            Payment = hasil.Payment,
            FinalTotal = hasil.FinalTotal,
            EstimatedFinishUtc = hasil.EstimatedFinishUtc,
        };
        if (Terpilih?.Id == hasil.Id) Terpilih = Daftar[index];
    }

    [RelayCommand]
    private Task TerapkanFilterAsync()
    {
        Halaman = 0;
        return MuatAsync();
    }

    [RelayCommand]
    private Task BersihkanFilterAsync()
    {
        Cari = "";
        FilterStatus = null;
        FilterPembayaran = null;
        Dari = null;
        Sampai = null;
        return TerapkanFilterAsync();
    }

    private static string? Kosongkan(string teks) => string.IsNullOrWhiteSpace(teks) ? null : teks.Trim();
}
