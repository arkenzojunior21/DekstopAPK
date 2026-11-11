namespace Laundry.Application.Core;

/// <summary>Satu berkas backup hasil pg_dump, dilihat dari riwayat di database.</summary>
public record BackupInfo(Guid Id, string Target, string NamaFile, long UkuranByte, DateTime DibuatUtc,
    bool Sukses, bool Terverifikasi, string? Error);

public record BackupRunResult(string Target, bool Sukses, Guid Id, string? NamaFile, long UkuranByte,
    bool Terverifikasi, string? Error, int DurasiDetik);

public record BackupVerifyResult(Guid Id, bool Sah, int JumlahObjek, string? Error);

public record BackupCleanupResult(int Dihapus, IReadOnlyList<string> NamaFileDihapus,
    IReadOnlyList<string> AlasanDitolak);

public record BackupStatusDto(string FolderBackup, string? PathPgDump, int RetensiHari, bool BackupHarian,
    DateTime? BackupSuksesTerakhirUtc, IReadOnlyList<BackupInfo> Berkas);

/// <summary>
/// Layanan backup database: dump custom format, verifikasi baca arsip, lalu retensi.
/// Aturan utamanya: berkas hanya boleh dihapus kalau pernah terverifikasi, dan backup yang
/// gagal dilaporkan sebagai gagal — tidak pernah dicatat sukses tanpa pg_dump selesai benar.
/// </summary>
public interface IBackupService
{
    Task<BackupRunResult> JalankanAsync(string target, CancellationToken ct = default);

    Task<BackupVerifyResult> VerifikasiAsync(Guid id, CancellationToken ct = default);

    Task<BackupCleanupResult> BersihkanAsync(CancellationToken ct = default);

    Task<BackupStatusDto> StatusAsync(CancellationToken ct = default);

    Task AturRetensiAsync(int hari, CancellationToken ct = default);

    Task AturHarianAsync(bool aktif, CancellationToken ct = default);
}

/// <summary>Keputusan retensi tanpa IO supaya bisa diuji langsung.</summary>
public static class RetensiBackup
{
    /// <summary>
    /// Berkas yang boleh dihapus: lebih tua dari batas retensi, pernah sukses, dan sudah diverifikasi.
    /// Satu berkas terbaru per target selalu disimpan supaya selalu ada titik pemulihan, dan berkas
    /// yang belum terverifikasi tidak pernah dihapus buta — kalau tidak bisa diverifikasi, itu masalah
    /// yang harus terlihat, bukan hilang.
    /// </summary>
    public static IReadOnlyList<BackupInfo> YangBolehDihapus(IEnumerable<BackupInfo> semua, DateTime kini,
        int hariRetensi)
    {
        var batas = kini.AddDays(-Math.Max(hariRetensi, 1));
        var hasil = new List<BackupInfo>();
        foreach (var grup in semua.GroupBy(b => b.Target))
        {
            var urut = grup.OrderByDescending(b => b.DibuatUtc).ToList();
            var terbaru = urut[0];
            hasil.AddRange(urut.Where(b => b.Id != terbaru.Id
                && b.Sukses && b.Terverifikasi && b.DibuatUtc <= batas));
        }

        return hasil.OrderBy(b => b.Target).ThenBy(b => b.DibuatUtc).ToList();
    }
}

/// <summary>
/// Hasil pemeriksaan versi aplikasi. Status selalu jujur tentang apa yang bisa diperiksa:
/// tanpa URL manifest tidak ada yang bisa disimpulkan, dan kegagalan jaringan tidak pernah
/// diterjemahkan menjadi "sudah versi terbaru".
/// </summary>
public record UpdateCheckResult(string VersiSaatIni, string Status, string? VersiTersedia, bool AdaVersiBaru,
    string? UrlUnduhan, string? Sha256, string? Catatan);

public interface IUpdateChecker
{
    Task<UpdateCheckResult> PeriksaAsync(CancellationToken ct = default);

    Task<UpdateVerifyResult> VerifikasiBerkasAsync(string namaFile, CancellationToken ct = default);
}

/// <summary>Hasil pencocokan sha256 berkas installer yang sudah diunduh manual.</summary>
public record UpdateVerifyResult(bool Cocok, string? Sha256, string? VersiManifest, string Status, string? Catatan);

/// <summary>Satu kunci konfigurasi lingkungan; nilainya tidak pernah dikirim ke klien.</summary>
public record ConfigItemDto(string Kunci, bool Terisi, string Kategori, string Catatan);

public record ConfigStatusDto(string VersiAplikasi, IReadOnlyList<ConfigItemDto> Kunci,
    BackupStatusDto Backup, UpdateCheckResult Update, bool PekerjaSinkronisasiAktif,
    NotificationQueueStatus Notifikasi);
