using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;
using Laundry.Domain;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Katalog layanan + tarif (Q2/Q3/Q4 masih OPEN QUESTION, jadi nilainya hanya yang diketik admin).
/// Layar tidak pernah mengisi tarif bawaan: kolom kosong berarti belum ada angka, bukan nol.
/// </summary>
public partial class KatalogViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    public ObservableCollection<ServiceDto> Daftar { get; } = [];

    [ObservableProperty] private ServiceDto? _terpilih;
    [ObservableProperty] private string _nama = "";
    [ObservableProperty] private ServiceUnitType _satuan = ServiceUnitType.PerKg;
    [ObservableProperty] private decimal _tarif;
    [ObservableProperty] private decimal? _minimumBerat;
    [ObservableProperty] private int _hariStandar = 3;
    [ObservableProperty] private int _hariExpress = 1;
    [ObservableProperty] private decimal _tambahanExpress;
    [ObservableProperty] private bool _aktif = true;
    [ObservableProperty] private decimal _ongkos;
    [ObservableProperty] private bool _hanyaAktif;

    public override string Judul => "Katalog";

    public IReadOnlyList<ServiceUnitType> SemuaSatuan { get; } = (ServiceUnitType[])Enum.GetValues(typeof(ServiceUnitType));

    public bool AdaTerpilih => Terpilih is not null;

    partial void OnTerpilihChanged(ServiceDto? value)
    {
        OnPropertyChanged(nameof(AdaTerpilih));
        if (value is { } s)
        {
            Nama = s.Nama;
            Satuan = s.UnitType;
            Tarif = s.CurrentPrice;
            MinimumBerat = s.MinimumWeightKg;
            HariStandar = s.StandardDurationDays;
            HariExpress = s.ExpressDurationDays;
            TambahanExpress = s.ExpressSurcharge;
            Aktif = s.IsActive;
        }
    }

    partial void OnHanyaAktifChanged(bool value) => _ = MuatAsync();

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var daftar = await Konteks.Api.IsiAsync<List<ServiceDto>>($"api/catalog?activeOnly={(HanyaAktif ? "true" : "false")}");
        var idTerpilih = Terpilih?.Id;
        Daftar.Clear();
        foreach (var layanan in daftar.OrderBy(l => l.Nama, StringComparer.OrdinalIgnoreCase)) Daftar.Add(layanan);
        Ongkos = (await Konteks.Api.IsiAsync<OngkosHasil>("api/catalog/delivery-fee")).Fee;
        Terpilih = Daftar.FirstOrDefault(d => d.Id == idTerpilih);
    });

    [RelayCommand]
    private void Baru()
    {
        Terpilih = null;
        Nama = "";
        Satuan = ServiceUnitType.PerKg;
        Tarif = 0m;
        MinimumBerat = null;
        HariStandar = 3;
        HariExpress = 1;
        TambahanExpress = 0m;
        Aktif = true;
    }

    /// <summary>Upsert berdasarkan nama: layanan dengan nama sama dapat versi tarif baru, bukan baris kembar.</summary>
    [RelayCommand]
    private Task SimpanAsync()
    {
        if (string.IsNullOrWhiteSpace(Nama)) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<ServiceDto>("api/catalog", new UpsertServiceRequest(
                Nama.Trim(), Satuan, MinimumBerat, Tarif, HariStandar, HariExpress, TambahanExpress, Aktif));
            await MuatAsync();
            Terpilih = Daftar.FirstOrDefault(d => d.Id == hasil.Id);
            Beres($"Layanan '{hasil.Nama}' tersimpan: {Format.Rupiah(hasil.CurrentPrice)} " +
                  $"per {Format.Label(hasil.UnitType)}, standar {hasil.StandardDurationDays} hari, " +
                  $"express {hasil.ExpressDurationDays} hari.");
        });
    }

    [RelayCommand]
    private Task SimpanOngkosAsync() => JalankanAsync(async () =>
    {
        await Konteks.Api.GantiAsync("api/catalog/delivery-fee", new { fee = Ongkos });
        Beres($"Ongkos antar-jemput {Format.Rupiah(Ongkos)} tersimpan.");
    });

    private sealed record OngkosHasil(decimal Fee);
}
