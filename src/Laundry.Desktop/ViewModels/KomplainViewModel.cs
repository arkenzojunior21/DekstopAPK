using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Desktop.Services;
using Laundry.Domain;

namespace Laundry.Desktop.ViewModels;

/// <summary>
/// Satu tiket komplain siap tampil. Nomor pesanan dan nama pengirim dirakit di sini karena server
/// hanya mengirim identifier — tanpa ini layarnya menampilkan GUID yang tidak bisa dibaca manusia.
/// </summary>
public sealed record BarisKirim(string Isi, string Pengirim, string Kapan);

public sealed record BarisKomplain(ComplaintDto Komplain, string NomorPesanan, bool Admin, Guid? UserAktif)
{
    public Guid Id => Komplain.Id;
    public string Nomor => NomorPesanan;
    public string Kategori => Komplain.Category;
    public string Isi => Komplain.Description;
    public ComplaintStatus Status => Komplain.Status;
    public string Dibuat => Format.TanggalJam(Komplain.CreatedAtUtc);
    public int JumlahPesan => Komplain.Messages.Count;

    public IReadOnlyList<BarisKirim> Pesan =>
        [.. Komplain.Messages.Select(m => new BarisKirim(m.Body,
            m.SenderUserId == UserAktif ? "Saya" : Admin ? "Pelanggan / admin lain" : "Toko",
            Format.TanggalJam(m.SentAtUtc)))];
}

/// <summary>
/// Tiket komplain, satu layar untuk dua peran. Pelanggan hanya bisa membuat tiket untuk pesanannya
/// sendiri dan membalas; hanya admin yang mengubah status, dan penolakan server ditampilkan apa adanya.
/// </summary>
public partial class KomplainViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    public ObservableCollection<BarisKomplain> Daftar { get; } = [];

    public ObservableCollection<OrderListItem> PesananAktif { get; } = [];

    [ObservableProperty] private BarisKomplain? _terpilih;
    [ObservableProperty] private ComplaintStatus? _filterStatus;
    [ObservableProperty] private string _balasan = "";
    [ObservableProperty] private ComplaintStatus _statusBaru = ComplaintStatus.Ditinjau;
    [ObservableProperty] private string _kategori = "";
    [ObservableProperty] private string _isiKomplain = "";
    [ObservableProperty] private OrderListItem? _pesananDipilih;

    public override string Judul => "Komplain";

    public bool Admin => Konteks.Sesi.Admin;

    public bool AdaTerpilih => Terpilih is not null;

    public bool BisaBalas => Terpilih is not null;

    public bool BisaUbahStatus => Admin && Terpilih is not null;

    public IReadOnlyList<ComplaintStatus> SemuaStatus { get; } = (ComplaintStatus[])Enum.GetValues(typeof(ComplaintStatus));

    partial void OnTerpilihChanged(BarisKomplain? value)
    {
        OnPropertyChanged(nameof(AdaTerpilih));
        OnPropertyChanged(nameof(BisaBalas));
        OnPropertyChanged(nameof(BisaUbahStatus));
        if (value is not null) StatusBaru = value.Status;
    }

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        var query = "api/complaints";
        if (FilterStatus is { } s) query += $"?status={s}";
        var tiket = await Konteks.Api.IsiAsync<List<ComplaintDto>>(query);

        var pesanan = await Konteks.Api.IsiAsync<List<OrderListItem>>(Admin ? "api/orders?pageSize=200" : "api/orders/mine?pageSize=200");
        var nomor = pesanan.ToDictionary(o => o.Id, o => o.OrderNumber);

        Daftar.Clear();
        foreach (var item in tiket.OrderByDescending(t => t.CreatedAtUtc))
            Daftar.Add(new BarisKomplain(item, nomor.GetValueOrDefault(item.OrderId, $"pesanan {Singkat(item.OrderId)}"),
                Admin, Konteks.Sesi.UserId));

        // Tanpa pilihan otomatis panel kanan tampak rusak; seleksi lama dipertahankan kalau tiketnya masih ada.
        var idLama = Terpilih?.Id;
        Terpilih = Daftar.FirstOrDefault(b => b.Id == idLama) ?? Daftar.FirstOrDefault();

        if (Admin) return;
        PesananAktif.Clear();
        foreach (var item in pesanan.Where(p => p.Status != OrderStatus.Dibatalkan && p.Status != OrderStatus.Selesai))
            PesananAktif.Add(item);
    });

    [RelayCommand]
    private Task TerapkanFilterAsync() => MuatAsync();

    [RelayCommand]
    private Task BalasAsync()
    {
        if (Terpilih is null || string.IsNullOrWhiteSpace(Balasan)) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            await Konteks.Api.KirimAsync<ComplaintDto>($"api/complaints/{Terpilih.Id}/reply",
                new { body = Balasan.Trim() });
            Balasan = "";
            await MuatAsync();
            Beres("Balasan terkirim.");
        });
    }

    [RelayCommand]
    private Task GantiStatusAsync()
    {
        if (Terpilih is null) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.GantiAsync<ComplaintDto>($"api/complaints/{Terpilih.Id}/status",
                new { status = StatusBaru });
            await MuatAsync();
            Terpilih = Daftar.FirstOrDefault(d => d.Id == hasil.Id);
            Beres($"Status tiket {NomorSingkat(hasil)} = {Format.Label(hasil.Status)}.");
        });
    }

    [RelayCommand]
    private Task BuatAsync()
    {
        if (PesananDipilih is null || string.IsNullOrWhiteSpace(Kategori) || string.IsNullOrWhiteSpace(IsiKomplain))
            return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            await Konteks.Api.KirimAsync("api/complaints",
                new CreateComplaintRequest(PesananDipilih.Id, Kategori.Trim(), IsiKomplain.Trim()));
            Kategori = "";
            IsiKomplain = "";
            PesananDipilih = null;
            await MuatAsync();
            Beres("Komplain terkirim ke toko. Status berubah saat admin menanganinya.");
        });
    }

    // Guid hanya menerima format D/N/P/B/X tanpa presisi; "N5" melempar FormatException.
    private static string Singkat(Guid id) => id.ToString("N")[..8];

    private static string NomorSingkat(ComplaintDto komplain) => $"#{Singkat(komplain.Id)}";
}
