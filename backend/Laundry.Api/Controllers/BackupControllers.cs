using Laundry.Application.Core;
using Laundry.Infrastructure.Backup;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Update;
using Laundry.Infrastructure.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laundry.Api.Controllers;

/// <summary>
/// Backup dan retensi. Sengaja tidak ada endpoint restore: menimpa database adalah tindakan
/// yang tidak bisa dibatalkan, jadi dijalankan manual lewat scripts/restore.ps1 dengan konfirmasi.
/// </summary>
[ApiController]
[Route("api/backup")]
[Authorize(Roles = "Admin")]
public class BackupController(IBackupService backup) : ControllerBase
{
    public sealed record JalankanRequest(string Target);
    public sealed record KonfigurasiRequest(int? RetensiHari, bool? Harian);

    [HttpGet("status")]
    public Task<BackupStatusDto> Status(CancellationToken ct) => backup.StatusAsync(ct);

    [HttpPost("jalan")]
    public async Task<IActionResult> Jalankan([FromBody] JalankanRequest req, CancellationToken ct)
    {
        if (req.Target is not ("admin" or "cloud"))
            return BadRequest(new { error = "Target harus 'admin' atau 'cloud'." });
        try
        {
            return Ok(await backup.JalankanAsync(req.Target, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }

    [HttpPost("{id:guid}/verifikasi")]
    public async Task<IActionResult> Verifikasi(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await backup.VerifikasiAsync(id, ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Riwayat backup tidak ditemukan." });
        }
    }

    /// <summary>Terapkan retensi sekarang (hanya berkas terverifikasi yang lebih tua dari batas).</summary>
    [HttpPost("retensi")]
    public Task<BackupCleanupResult> Retensi(CancellationToken ct) => backup.BersihkanAsync(ct);

    [HttpPut("konfigurasi")]
    public async Task<IActionResult> Konfigurasi([FromBody] KonfigurasiRequest req, CancellationToken ct)
    {
        if (req.RetensiHari is { } hari)
        {
            if (hari is < 1 or > 90) return BadRequest(new { error = "Retensi harus 1..90 hari." });
            await backup.AturRetensiAsync(hari, ct);
        }

        if (req.Harian is { } auto) await backup.AturHarianAsync(auto, ct);
        return Ok(await backup.StatusAsync(ct));
    }
}

/// <summary>
/// Pemeriksaan versi aplikasi. Tanpa UPDATE_CHECK_URL hasilnya "belum_dikonfigurasi" — sistem tidak
/// pernah menyimpulkan "sudah terbaru" dari ketiadaan data, dan tidak ada unduh/install otomatis.
/// </summary>
[ApiController]
[Route("api/app")]
[Authorize(Roles = "Admin")]
public class AppUpdateController(IUpdateChecker checker) : ControllerBase
{
    public sealed record VerifikasiRequest(string NamaFile);

    [HttpGet("versi")]
    public IActionResult Versi() => Ok(new { versi = AppInfo.Versi });

    [HttpGet("update-check")]
    public Task<UpdateCheckResult> Periksa(CancellationToken ct) => checker.PeriksaAsync(ct);

    [HttpPost("update-verifikasi")]
    public Task<UpdateVerifyResult> Verifikasi([FromBody] VerifikasiRequest req, CancellationToken ct) =>
        checker.VerifikasiBerkasAsync(req.NamaFile, ct);
}

/// <summary>
/// Ringkasan konfigurasi hybrid (Q14): kunci mana yang dibaca dari environment/.env dan apakah
/// terisi. Nilainya tidak pernah dikirim ke klien — yang dilaporkan hanya ada atau tidaknya.
/// </summary>
[ApiController]
[Route("api/config")]
[Authorize(Roles = "Admin")]
public class ConfigController(
    IBackupService backup,
    IUpdateChecker update,
    INotificationDispatcher notifikasi,
    SyncEngine sync) : ControllerBase
{
    private static readonly (string Kunci, string Kategori, string Catatan)[] Daftar =
    [
        ("CONN_ADMIN", "koneksi", "Database sisi admin; wajib."),
        ("CONN_CLOUD", "koneksi", "Database sisi cloud; wajib untuk sinkronisasi."),
        ("JWT_KEY", "rahasia", "Kunci tanda tangan token; wajib diganti dari bawaan."),
        ("WEBHOOK_SECRET", "rahasia", "HMAC webhook gateway; kosong berarti semua webhook ditolak."),
        ("UPDATE_CHECK_URL", "rilis", "Manifest versi aplikasi; kosong berarti cek update belum bisa dilakukan."),
        ("PG_BIN", "sistem", "Folder binari PostgreSQL (pg_dump/pg_restore); bawaan instalasi lokal."),
        ("BACKUP_DIR", "sistem", "Folder hasil backup; bawaan di %LOCALAPPDATA%\\laundry-pg\\backups."),
        ("UPDATE_DIR", "rilis", "Folder berkas installer unduhan yang boleh diverifikasi sha256-nya."),
        ("BACKUP_AUTO", "backup", "0 = pekerja backup harian dimatikan."),
        ("BACKUP_CEK_MENIT", "backup", "Jeda antar pemeriksaan backup harian (bawaan 30)."),
        ("BACKUP_TIMEOUT_DETIK", "backup", "Batas waktu pg_dump/pg_restore (bawaan 300)."),
        ("SYNC_BATCH", "sinkronisasi", "Event per arah per siklus worker (bawaan 100, maks 500)."),
        ("SYNC_INTERVAL_DETIK", "sinkronisasi", "Jeda antar siklus worker (bawaan 15)."),
        ("SEED_DEV_CONTOH", "dev", "1 = isi katalog contoh berlabel (CONTOH) saat API dev mulai."),
    ];

    [HttpGet("status")]
    public async Task<ConfigStatusDto> Status(CancellationToken ct)
    {
        var kunci = Daftar.Select(k => new ConfigItemDto(k.Kunci, EnvironmentConfig.Terisi(k.Kunci),
            k.Kategori, k.Catatan)).ToList();
        var sinkron = await sync.StatusAsync(ct);
        return new ConfigStatusDto(AppInfo.Versi, kunci, await backup.StatusAsync(ct),
            await update.PeriksaAsync(ct), sinkron.PekerjaAktif, await notifikasi.QueueStatusAsync(5, ct));
    }
}
