using Laundry.Infrastructure.Backup;
using Laundry.Infrastructure.Persistence;

namespace Laundry.Api.Infrastructure;

/// <summary>
/// Menjaga backup tetap harian tanpa perlu penjadwal Windows: tiap BACKUP_CEK_MENIT (bawaan 30)
/// diperiksa apakah backup sukses+terverifikasi terakhir sudah lebih tua dari 26 jam. Kalau sudah,
/// kedua sisi di-dump dan berkas lama dibersihkan. Matikan dengan menyimpan backup_harian=0
/// (lewat PUT /api/backup/konfigurasi) atau BACKUP_AUTO=0 di environment.
/// Tidak ada backup yang dicatat sukses kalau pg_dump tidak selesai — kalau perkakas atau database
/// tidak ada, worker hanya menulis peringatan dan mencoba lagi siklus berikutnya.
/// </summary>
public sealed class BackupWorker(
    IServiceScopeFactory scopes,
    BackupConfig config,
    ILogger<BackupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (EnvironmentConfig.Get("BACKUP_AUTO", "1") == "0")
        {
            log.LogInformation("Backup otomatis dimatikan lewat BACKUP_AUTO=0.");
            return;
        }

        var jeda = TimeSpan.FromMinutes(Math.Clamp(
            int.TryParse(EnvironmentConfig.Get("BACKUP_CEK_MENIT"), out var m) ? m : 30, 1, 720));
        await Task.Delay(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SiklusAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Siklus backup gagal; dicoba lagi pada siklus berikutnya");
            }

            try { await Task.Delay(jeda, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SiklusAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
        var status = await backup.StatusAsync(ct);
        if (!status.BackupHarian) return;

        if (BackupService.PerluBackupBaru(status.BackupSuksesTerakhirUtc, DateTime.UtcNow))
        {
            foreach (var target in BackupService.Target)
            {
                try
                {
                    var hasil = await backup.JalankanAsync(target, ct);
                    log.LogInformation("Backup {Target}: sukses={Sukses}, berkas={Berkas}, {Ukuran} byte, " +
                                       "terverifikasi={Verifikasi}, catatan={Catatan}",
                        target, hasil.Sukses, hasil.NamaFile, hasil.UkuranByte, hasil.Terverifikasi, hasil.Error);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(ex, "Backup target {Target} tidak selesai", target);
                }
            }
        }

        var bersih = await backup.BersihkanAsync(ct);
        if (bersih.Dihapus > 0 || bersih.AlasanDitolak.Count > 0)
            log.LogInformation("Retensi {Hari} hari: {Jumlah} berkas dihapus, {Tolak} ditolak (pg_dump={Dump})",
                status.RetensiHari, bersih.Dihapus, bersih.AlasanDitolak.Count, config.PgDumpTersedia);
    }
}
