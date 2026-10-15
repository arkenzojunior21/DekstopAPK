using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Jadwal antar-jemput (Q11/Q12 masih OPEN QUESTION): layar ini hanya mencatat slot yang diketik
/// admin beserta kapasitasnya, tidak mengarang jam operasional, area layanan, maupun biaya per kondisi.
/// </summary>
public partial class SlotViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    public ObservableCollection<SlotDto> Daftar { get; } = [];

    [ObservableProperty] private DateTime? _dari = DateTime.Today;
    [ObservableProperty] private DateTime? _sampai = DateTime.Today.AddDays(14);
    [ObservableProperty] private DateTime? _tanggalBaru = DateTime.Today;
    [ObservableProperty] private string _jamMulai = "09:00";
    [ObservableProperty] private int _durasiJam = 2;
    [ObservableProperty] private int _kapasitas = 5;

    public override string Judul => "Slot";

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var dari = (Dari ?? DateTime.Today).ToUniversalTime();
        var sampai = (Sampai ?? DateTime.Today.AddDays(14)).ToUniversalTime();
        if (sampai < dari) (dari, sampai) = (sampai, dari);
        sampai = sampai.AddDays(1).AddSeconds(-1);

        var daftar = await Konteks.Api.IsiAsync<List<SlotDto>>(
            $"api/slots?from={dari:yyyy-MM-ddTHH:mm:ssZ}&to={sampai:yyyy-MM-ddTHH:mm:ssZ}");
        Daftar.Clear();
        foreach (var slot in daftar.OrderBy(s => s.StartsAtUtc)) Daftar.Add(slot);
    });

    [RelayCommand]
    private Task TambahAsync()
    {
        if (TanggalBaru is not { } tanggal) return Task.CompletedTask;
        if (!CobaJam(JamMulai, out var jam))
        {
            Gagal("Jam mulai harus berbentuk HH:mm, misalnya 09:00.");
            return Task.CompletedTask;
        }

        var mulai = DateTime.SpecifyKind(tanggal.Date.Add(jam), DateTimeKind.Local).ToUniversalTime();
        var selesai = mulai.AddHours(Math.Max(DurasiJam, 1));
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<SlotDto>("api/slots",
                new CreateSlotRequest(mulai, selesai, Kapasitas));
            await MuatAsync();
            Beres($"Slot {Format.TanggalJam(hasil.StartsAtUtc)}–{Format.Jam(hasil.EndsAtUtc)} " +
                  $"kapasitas {hasil.Capacity} dibuat.");
        });
    }

    private static bool CobaJam(string teks, out TimeSpan jam)
    {
        jam = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(teks)) return false;
        var potongan = teks.Trim().Split(':');
        if (potongan.Length != 2) return false;
        if (!int.TryParse(potongan[0], NumberStyles.None, CultureInfo.InvariantCulture, out var jamHari) ||
            !int.TryParse(potongan[1], NumberStyles.None, CultureInfo.InvariantCulture, out var menit))
            return false;
        if (jamHari is < 0 or > 23 || menit is < 0 or > 59) return false;
        jam = TimeSpan.FromHours(jamHari) + TimeSpan.FromMinutes(menit);
        return true;
    }
}
