using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Laundry.Application.Core;
using Laundry.Application.Sync;
using Laundry.Desktop.Services;

namespace Laundry.Desktop.ViewModels;

/// <summary>Baris provider dengan label live/simulasi yang sudah dibaca manusia.</summary>
public sealed record AdapterTampil(string Nama, string Jenis, bool Live, string Label, string Catatan);

/// <summary>
/// Layar sistem: sinkronisasi, provider, backup, versi, dan konfigurasi. Isinya sengaja hanya
/// membaca keadaan apa adanya — termasuk ketika sesuatu belum dikonfigurasi atau masih simulasi.
/// Tidak ada angka "sukses" yang dikarang ketika server menjawab gagal.
/// </summary>
public partial class SistemViewModel(Konteks konteks) : ViewModelDasar(konteks)
{
    public ObservableCollection<SyncQueueRow> GagalAdmin { get; } = [];
    public ObservableCollection<SyncQueueRow> GagalCloud { get; } = [];
    public ObservableCollection<ConfigItemDto> Kunci { get; } = [];
    public ObservableCollection<AdapterTampil> Adapter { get; } = [];
    public ObservableCollection<BackupInfo> BerkasBackup { get; } = [];

    [ObservableProperty] private BackupInfo? _arsipTerpilih;
    [ObservableProperty] private SyncStatusDto? _sinkron;
    [ObservableProperty] private ProviderStatusDto? _provider;
    [ObservableProperty] private BackupStatusDto? _backup;
    [ObservableProperty] private UpdateCheckResult? _update;
    [ObservableProperty] private string _versiAplikasi = "";
    [ObservableProperty] private string _namaUsaha = "";
    [ObservableProperty] private int _retensiHari = 7;
    [ObservableProperty] private bool _backupHarian = true;
    [ObservableProperty] private string _namaBerkasInstaller = "";
    [ObservableProperty] private string _hasilVerifikasi = "";
    [ObservableProperty] private string _ringkasanSinkron = "";
    [ObservableProperty] private string _ringkasanProvider = "";
    [ObservableProperty] private string _statusUpdate = "";

    public override string Judul => "Sistem";

    public bool AdaGagal => GagalAdmin.Count + GagalCloud.Count > 0;

    public override Task MuatAsync() => JalankanAsync(async () =>
    {
        Sinkron = await Konteks.Api.IsiAsync<SyncStatusDto>("api/sync/status");
        Provider = await Konteks.Api.IsiAsync<ProviderStatusDto>("api/providers/status");
        Backup = await Konteks.Api.IsiAsync<BackupStatusDto>("api/backup/status");
        Update = await Konteks.Api.IsiAsync<UpdateCheckResult>("api/app/update-check");
        StatusUpdate = TeksStatus(Update);
        NamaUsaha = (await Konteks.Api.IsiAsync<NamaHasil>("api/settings/business-name")).Nama ?? "";

        var konfigurasi = await Konteks.Api.IsiAsync<ConfigStatusDto>("api/config/status");
        VersiAplikasi = konfigurasi.VersiAplikasi;
        Kunci.Clear();
        foreach (var item in konfigurasi.Kunci) Kunci.Add(item);

        RetensiHari = konfigurasi.Backup.RetensiHari;
        BackupHarian = konfigurasi.Backup.BackupHarian;
        IsiBerkas(konfigurasi.Backup.Berkas);

        Adapter.Clear();
        foreach (var item in Provider.Adapter)
            Adapter.Add(new AdapterTampil(item.Nama, item.Jenis, item.Live,
                item.Live ? "LIVE" : "SIMULASI", item.Catatan));

        await MuatGagalAsync();
        RingkasanSinkron = Sinkron.PekerjaAktif
            ? $"Antrean keluar admin {Sinkron.AntrianKeluarAdmin}, cloud {Sinkron.AntrianKeluarCloud}; " +
              $"gagal admin {Sinkron.AntrianGagalAdmin}, cloud {Sinkron.AntrianGagalCloud}; " +
              $"detak terakhir {Format.Relatif(Sinkron.DetakTerakhirUtc)}"
            : "Pekerja sinkronisasi tidak berjalan. Perubahan lokal tetap aman di antrean keluar.";
        RingkasanProvider = Provider.Notifikasi.Ditunda > 0
            ? $"{Provider.Notifikasi.Ditunda} notifikasi menunggu koneksi/provider asli " +
              $"(masih menunggu Q7/Q10)."
            : $"Antrean notifikasi: menunggu {Provider.Notifikasi.Menunggu}, terkirim {Provider.Notifikasi.Terkirim}, " +
              $"gagal {Provider.Notifikasi.Gagal}.";
    });

    [RelayCommand]
    private Task JalankanSyncAsync() => JalankanAsync(async () =>
    {
        var hasil = await Konteks.Api.KirimAsync<Dictionary<string, SatuanArah>>("api/sync/run", null);
        var teks = hasil.Count == 0
            ? "tidak ada arah yang selesai dijalankan"
            : string.Join(", ", hasil.Select(k => $"{k.Key}: {k.Value.Terkirim} terkirim, {k.Value.Diterapkan} diterapkan, {k.Value.Gagal} gagal"));
        await MuatAsync();
        Beres($"Satu siklus sinkronisasi: {teks}.");
    });

    [RelayCommand]
    private Task CobaUlangAsync() => JalankanAsync(async () =>
    {
        var jumlah = await Konteks.Api.KirimAsync<int>("api/sync/retry-failed", null);
        await MuatAsync();
        Beres($"{jumlah} event gagal dimasukkan kembali ke antrean.");
    });

    [RelayCommand]
    private Task KirimNotifikasiAsync() => JalankanAsync(async () =>
    {
        var hasil = await Konteks.Api.KirimAsync<NotificationDrainResult>("api/providers/notifications/drain", null);
        await MuatAsync();
        Beres(hasil.Ditunda > 0
            ? $"{hasil.Terkirim} terkirim, {hasil.Ditunda} ditunda (butuh koneksi/provider asli), {hasil.Gagal} gagal."
            : $"{hasil.Terkirim} diproses, {hasil.Gagal} gagal.");
    });

    [RelayCommand]
    private Task BackupAsync(string? target)
    {
        var dipili = string.IsNullOrWhiteSpace(target) ? "admin" : target.Trim().ToLowerInvariant();
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<BackupRunResult>("api/backup/jalan", new { target = dipili });
            await MuatAsync();
            if (hasil.Sukses && hasil.Terverifikasi)
                Beres($"{hasil.Target}: {hasil.NamaFile} ({Format.Angka(hasil.UkuranByte)} byte, " +
                      $"{hasil.DurasiDetik}s) terverifikasi.");
            else
                Gagal($"{hasil.Target} gagal: {hasil.Error ?? "penyebab tidak dilaporkan server"}. " +
                      "Arsip lama tidak dihapus.");
        });
    }

    [RelayCommand]
    private Task VerifikasiAsync()
    {
        if (ArsipTerpilih is not { } berkas) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<BackupVerifyResult>($"api/backup/{berkas.Id}/verifikasi", null);
            await MuatAsync();
            if (hasil.Sah) Beres($"{berkas.NamaFile} sah: {Format.Angka(hasil.JumlahObjek)} objek terbaca.");
            else Gagal($"{berkas.NamaFile} TIDAK bisa dibaca: {hasil.Error}. Berkasnya dibiarkan di folder.");
        });
    }

    [RelayCommand]
    private Task RetensiAsync() => JalankanAsync(async () =>
    {
        var hasil = await Konteks.Api.KirimAsync<BackupCleanupResult>("api/backup/retensi", null);
        await MuatAsync();
        Beres(hasil.Dihapus == 0
            ? $"Tidak ada yang dihapus. {string.Join(" · ", hasil.AlasanDitolak)}"
            : $"{hasil.Dihapus} arsip lama dihapus: {string.Join(", ", hasil.NamaFileDihapus)}");
    });

    [RelayCommand]
    private Task SimpanBackupConfigAsync() => JalankanAsync(async () =>
    {
        var hasil = await Konteks.Api.GantiAsync<BackupStatusDto>("api/backup/konfigurasi",
            new { retensiHari = RetensiHari, harian = BackupHarian });
        Backup = hasil;
        IsiBerkas(hasil.Berkas);
        Beres($"Retensi {hasil.RetensiHari} hari, backup harian {(hasil.BackupHarian ? "aktif" : "mati")}.");
    });

    [RelayCommand]
    private Task CekUpdateAsync() => JalankanAsync(async () =>
    {
        Update = await Konteks.Api.IsiAsync<UpdateCheckResult>("api/app/update-check");
        StatusUpdate = TeksStatus(Update);
        Beres(StatusUpdate);
    });

    [RelayCommand]
    private Task VerifikasiInstallerAsync()
    {
        if (string.IsNullOrWhiteSpace(NamaBerkasInstaller)) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            var hasil = await Konteks.Api.KirimAsync<UpdateVerifyResult>("api/app/update-verifikasi",
                new { namaFile = NamaBerkasInstaller.Trim() });
            HasilVerifikasi = $"{(hasil.Cocok ? "COKOK" : "TIDAK COKOK")} · sha256 {hasil.Sha256} · " +
                              $"{hasil.Status} · {hasil.Catatan ?? "tanpa catatan"}";
            if (hasil.Cocok) Beres("Hash installer cocok dengan manifest.");
            else Gagal("Hash installer tidak cocok — jangan dipasang.");
        });
    }

    [RelayCommand]
    private Task SimpanNamaUsahaAsync()
    {
        if (string.IsNullOrWhiteSpace(NamaUsaha)) return Task.CompletedTask;
        return JalankanAsync(async () =>
        {
            await Konteks.Api.GantiAsync("api/settings/business-name", new { nama = NamaUsaha.Trim() });
            Beres("Nama usaha disimpan dan dipakai di kepala struk.");
        });
    }

    private async Task MuatGagalAsync()
    {
        GagalAdmin.Clear();
        GagalCloud.Clear();
        foreach (var baris in await Konteks.Api.IsiAsync<List<SyncQueueRow>>("api/sync/failed?arah=admin_ke_cloud"))
            GagalAdmin.Add(baris);
        foreach (var baris in await Konteks.Api.IsiAsync<List<SyncQueueRow>>("api/sync/failed?arah=cloud_ke_admin"))
            GagalCloud.Add(baris);
        OnPropertyChanged(nameof(AdaGagal));
    }

    private void IsiBerkas(IReadOnlyList<BackupInfo> berkas)
    {
        var idTerpilih = ArsipTerpilih?.Id;
        BerkasBackup.Clear();
        foreach (var item in berkas.OrderByDescending(b => b.DibuatUtc)) BerkasBackup.Add(item);
        ArsipTerpilih = BerkasBackup.FirstOrDefault(b => b.Id == idTerpilih);
    }

    /// <summary>Status pemeriksaan versi diterjemahkan tanpa mengubah maknanya.</summary>
    private static string TeksStatus(UpdateCheckResult hasil) => hasil.Status switch
    {
        "belum_dikonfigurasi" =>
            "Belum ada manifest versi (UPDATE_CHECK_URL kosong), jadi tidak ada yang bisa disimpulkan tentang rilis baru.",
        "gagal_diperiksa" =>
            $"Manifest tidak berhasil dibaca: {hasil.Catatan}. Ini bukan berarti versi terpasang sudah terbaru.",
        "manifest_tidak_sah" =>
            $"Manifest terbaca tapi bentuknya tidak sah: {hasil.Catatan}.",
        _ => hasil.AdaVersiBaru
            ? $"Versi {hasil.VersiTersedia} diumumkan manifest. Unduh installer manual, lalu cocokkan sha256-nya."
            : $"Versi terpasang {hasil.VersiSaatIni} sama dengan yang diumumkan manifest.",
    };

    /// <summary>Refleksi dari hasil satu siklus worker; bentuknya sama dengan JSON server.</summary>
    private sealed record SatuanArah(int Terkirim, int Diterapkan, int Gagal, bool TargetTerhubung);

    private sealed record NamaHasil(string? Nama);
}
