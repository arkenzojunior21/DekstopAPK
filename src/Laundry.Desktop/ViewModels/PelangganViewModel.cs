using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Data pelanggan dan saldo kreditnya. Nomor HP adalah kunci identitas (registrasi OTP memakai nomor
/// yang sama), jadi ia tidak bisa diubah dari layar ini — nama dan alamat saja yang bisa dikoreksi.
/// </summary>
public partial class PelangganViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    private const int UkuranHalaman = 30;

    public ObservableCollection<CustomerDto> Daftar { get; } = [];

    [ObservableProperty] private CustomerDto? _terpilih;
    [ObservableProperty] private string _cari = "";
    [ObservableProperty] private string _nama = "";
    [ObservableProperty] private string _nomor = "";
    [ObservableProperty] private string _alamat = "";
    [ObservableProperty] private string _saldo = "";
    [ObservableProperty] private int _halaman;
    [ObservableProperty] private bool _adaHalamanBerikut;

    public override string Judul => "Pelanggan";

    public bool AdaTerpilih => Terpilih is not null;

    partial void OnTerpilihChanged(CustomerDto? value)
    {
        OnPropertyChanged(nameof(AdaTerpilih));
        OnPropertyChanged(nameof(SedangMemperbaiki));
        if (value is { } c)
        {
            Nama = c.Nama;
            Nomor = c.Phone;
            Alamat = c.Alamat ?? "";
            Saldo = "";
            _ = MuatSaldoAsync(c.Id);
        }
    }

    /// <summary>Bedakan mode "buat baru" dan "perbaiki yang ada" supaya nomor HP tidak bisa diubah diam-diam.</summary>
    public bool SedangMemperbaiki => Terpilih is not null;

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var query = $"api/customers?page={Halaman}&pageSize={UkuranHalaman}";
        if (!string.IsNullOrWhiteSpace(Cari)) query += $"&q={Uri.EscapeDataString(Cari.Trim())}";
        var daftar = await Konteks.Api.IsiAsync<List<CustomerDto>>(query);

        var idTerpilih = Terpilih?.Id;
        Daftar.Clear();
        foreach (var pelanggan in daftar) Daftar.Add(pelanggan);
        AdaHalamanBerikut = daftar.Count == UkuranHalaman;
        Terpilih = Daftar.FirstOrDefault(d => d.Id == idTerpilih);
    });

    [RelayCommand]
    private Task CariAsync()
    {
        Halaman = 0;
        return MuatAsync();
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

    [RelayCommand]
    private void Baru()
    {
        Terpilih = null;
        Nama = "";
        Nomor = "";
        Alamat = "";
        Saldo = "";
    }

    [RelayCommand]
    private Task SimpanAsync()
    {
        if (string.IsNullOrWhiteSpace(Nama)) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            CustomerDto hasil;
            if (Terpilih is { } lama)
            {
                hasil = await Konteks.Api.GantiAsync<CustomerDto>($"api/customers/{lama.Id}",
                    new UpdateCustomerRequest(Nama.Trim(), Kosongkan(Alamat)));
                Beres($"Data pelanggan {hasil.Nama} diperbarui.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Nomor)) return;
                hasil = await Konteks.Api.KirimAsync<CustomerDto>("api/customers",
                    new CreateCustomerRequest(Nama.Trim(), Nomor.Trim(), Kosongkan(Alamat)));
                Beres($"Pelanggan {hasil.Nama} ({hasil.Phone}) tersimpan.");
            }

            await MuatAsync();
            Terpilih = Daftar.FirstOrDefault(d => d.Id == hasil.Id);
        });
    }

    [RelayCommand]
    private Task GantiAktifAsync()
    {
        if (Terpilih is not { } lama) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.GantiAsync<CustomerDto>($"api/customers/{lama.Id}/active?aktif={!lama.Aktif}", null);
            Beres(hasil.Aktif ? "Pelanggan diaktifkan kembali." : "Pelanggan dinonaktifkan.");
            await MuatAsync();
            Terpilih = Daftar.FirstOrDefault(d => d.Id == hasil.Id);
        });
    }

    private async Task MuatSaldoAsync(Guid customerId)
    {
        try
        {
            var hasil = await Konteks.Api.IsiAsync<SaldoHasil>($"api/payments/credit-balance?customerId={customerId}");
            Saldo = Format.Rupiah(hasil.Balance);
        }
        catch (ApiException ex)
        {
            Saldo = ex.Message;
        }
    }

    private static string? Kosongkan(string teks) => string.IsNullOrWhiteSpace(teks) ? null : teks.Trim();

    private sealed record SaldoHasil(decimal Balance);
}
