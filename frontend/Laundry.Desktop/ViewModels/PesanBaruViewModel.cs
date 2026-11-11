using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;
using Laundry.Domain;

namespace Laundry.Desktop.ViewModels;

/// <summary>Baris layanan pada keranjang pesanan; kuantitas mana yang dipakai tergantung satuan layanan.</summary>
public partial class BarisKeranjang(ServiceDto layanan) : ObservableObject
{
    public Guid ServiceId { get; } = layanan.Id;
    public string Nama { get; } = layanan.Nama;
    public ServiceUnitType Satuan { get; } = layanan.UnitType;
    public decimal Harga { get; } = layanan.CurrentPrice;
    public decimal? MinimumBerat { get; } = layanan.MinimumWeightKg;

    [ObservableProperty] private decimal? _estimasiBerat;
    [ObservableProperty] private decimal? _estimasiJumlah;

    public string SatuanTeks => Format.Label(Satuan);

    public bool PakaiBerat => Satuan == ServiceUnitType.PerKg;

    public bool PakaiItem => Satuan is ServiceUnitType.PerItem or ServiceUnitType.PaketTetap;

    /// <summary>
    /// Perkiraan dihitung dengan rumus Domain yang sama dipakai kasir (termasuk minimum berat),
    /// jadi angkanya bukan tebakan UI. Ongkos antar-jemput sengaja tidak ikut dihitung karena
    /// tarifnya hanya bisa dibaca admin — total resmi tetap ditetapkan server.
    /// </summary>
    public decimal SubtotalPerkiraan
    {
        get
        {
            var item = new OrderItem
            {
                ServiceId = ServiceId,
                ServiceNameSnapshot = Nama,
                UnitTypeSnapshot = Satuan,
                PriceSnapshot = Harga,
                EstimatedWeightKg = EstimasiBerat,
                EstimatedItemCount = EstimasiJumlah,
                MinimumWeightKgSnapshot = MinimumBerat,
            };
            try
            {
                return PriceCalculator.ItemSubtotal(item, useActual: false);
            }
            catch (InvalidOperationException)
            {
                return 0m;
            }
        }
    }

    partial void OnEstimasiBeratChanged(decimal? value) => OnPropertyChanged(nameof(SubtotalPerkiraan));

    partial void OnEstimasiJumlahChanged(decimal? value) => OnPropertyChanged(nameof(SubtotalPerkiraan));

    public OrderItemInput KeInput() => new(ServiceId, EstimasiBerat, EstimasiJumlah);
}

/// <summary>
/// Pembuatan pesanan satu layar untuk dua peran: admin memakai jalur walk-in di kasir, pelanggan
/// memakai jalur online yang status awalnya selalu "Menunggu Konfirmasi". Endpointnya berbeda dan
/// perhitungan resmi dikerjakan server — layar ini hanya mengirim niat + memperkirakan.
/// </summary>
public partial class PesanBaruViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    public ObservableCollection<BarisKeranjang> Keranjang { get; } = [];
    public ObservableCollection<ServiceDto> LayananTersedia { get; } = [];
    public ObservableCollection<SlotDto> SlotTersedia { get; } = [];
    public ObservableCollection<CustomerDto> PelangganTersimpan { get; } = [];

    [ObservableProperty] private ServiceDto? _layananDipilih;
    [ObservableProperty] private SlotDto? _slotDipilih;
    [ObservableProperty] private CustomerDto? _pelangganDipilih;
    [ObservableProperty] private FulfillmentMethod _pengambilan = FulfillmentMethod.AmbilSendiri;
    [ObservableProperty] private bool _express;
    [ObservableProperty] private string _alamat = "";
    [ObservableProperty] private string _catatan = "";
    [ObservableProperty] private string _walkInNama = "";
    [ObservableProperty] private string _walkInNomor = "";
    [ObservableProperty] private string _perkiraan = "Rp0";
    [ObservableProperty] private string _nomorHasil = "";

    public bool Admin => Konteks.Sesi.Admin;

    public override string Judul => Admin ? "Pesanan Baru" : "Pesan";

    public bool PerluSlot => Pengambilan == FulfillmentMethod.AntarJemput;

    public bool KeranjangKosong => Keranjang.Count == 0;

    /// <summary>Walk-in boleh tanpa data pelanggan (tunai di kasir), jadi hanya divalidasi bila diisi.</summary>
    public IReadOnlyList<FulfillmentMethod> SemuaPengambilan { get; } =
        (FulfillmentMethod[])Enum.GetValues(typeof(FulfillmentMethod));

    partial void OnPengambilanChanged(FulfillmentMethod value) => OnPropertyChanged(nameof(PerluSlot));

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var layanan = await Konteks.Api.IsiAsync<List<ServiceDto>>("api/catalog?activeOnly=true");
        LayananTersedia.Clear();
        foreach (var item in layanan.OrderBy(l => l.Nama, StringComparer.OrdinalIgnoreCase)) LayananTersedia.Add(item);

        var slot = await Konteks.Api.IsiAsync<List<SlotDto>>("api/slots");
        SlotTersedia.Clear();
        foreach (var item in slot.Where(s => s.IsActive && s.StartsAtUtc >= DateTime.UtcNow && s.BookedCount < s.Capacity)
                     .OrderBy(s => s.StartsAtUtc)) SlotTersedia.Add(item);

        if (!Admin) return;
        var pelanggan = await Konteks.Api.IsiAsync<List<CustomerDto>>("api/customers?pageSize=200");
        PelangganTersimpan.Clear();
        foreach (var item in pelanggan.Where(c => c.Aktif).OrderBy(c => c.Nama, StringComparer.OrdinalIgnoreCase))
            PelangganTersimpan.Add(item);
    });

    [RelayCommand]
    private void Tambah()
    {
        if (LayananDipilih is null) return;
        var baris = new BarisKeranjang(LayananDipilih);
        baris.PropertyChanged += PadaBarisBerubah;
        Keranjang.Add(baris);
        LayananDipilih = null;
        HitungPerkiraan();
    }

    [RelayCommand]
    private void Hapus(BarisKeranjang? baris)
    {
        if (baris is null) return;
        baris.PropertyChanged -= PadaBarisBerubah;
        Keranjang.Remove(baris);
        HitungPerkiraan();
    }

    private void PadaBarisBerubah(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BarisKeranjang.SubtotalPerkiraan)) HitungPerkiraan();
    }

    private void HitungPerkiraan()
    {
        Perkiraan = Format.Rupiah(Keranjang.Sum(b => b.SubtotalPerkiraan));
        OnPropertyChanged(nameof(KeranjangKosong));
    }

    [RelayCommand]
    private Task KirimAsync()
    {
        if (Keranjang.Count == 0)
        {
            Gagal("Tambahkan minimal satu layanan.");
            return Task.CompletedTask;
        }

        return JalankanAsync(async () =>
        {
            var pelangganWalkIn = PelangganDipilih;
            var permintaan = new CreateOrderRequest(
                CustomerId: Admin ? pelangganWalkIn?.Id : null,
                WalkInNama: Admin && pelangganWalkIn is null ? Potong(WalkInNama) : null,
                WalkInPhone: Admin && pelangganWalkIn is null ? Potong(WalkInNomor) : null,
                Items: [.. Keranjang.Select(b => b.KeInput())],
                Express: Express,
                Fulfillment: Pengambilan,
                SlotId: PerluSlot ? SlotDipilih?.Id : null,
                Alamat: Potong(Alamat),
                Catatan: Potong(Catatan));

            var path = Admin ? "api/orders" : "api/orders/online";
            var hasil = await Konteks.Api.KirimAsync<OrderDetailDto>(path, permintaan);
            NomorHasil = $"{hasil.OrderNumber} · {Format.Label(hasil.Status)} · total {Format.Rupiah(hasil.FinalTotal)}";
            Beres(Admin
                ? $"Pesanan {hasil.OrderNumber} dibuat. Total {Format.Rupiah(hasil.FinalTotal)}."
                : $"Pesanan {hasil.OrderNumber} terkirim. Toko akan mengonfirmasi dulu; perkiraan awal " +
                  $"{Format.Rupiah(hasil.EstimatedTotal)} dan total resmi ditetapkan setelah ditimbang.");
            Bersihkan();
            await MuatAsync();
        });
    }

    private void Bersihkan()
    {
        foreach (var baris in Keranjang) baris.PropertyChanged -= PadaBarisBerubah;
        Keranjang.Clear();
        SlotDipilih = null;
        PelangganDipilih = null;
        WalkInNama = "";
        WalkInNomor = "";
        Catatan = "";
        Express = false;
        Pengambilan = FulfillmentMethod.AmbilSendiri;
        HitungPerkiraan();
    }

    private static string? Potong(string teks) => string.IsNullOrWhiteSpace(teks) ? null : teks.Trim();
}
