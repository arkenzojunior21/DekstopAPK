using System.Text.Json;
using Laundry.Application.Sync;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laundry.Infrastructure.Sync;

public record SyncSatuanArah(int Terkirim, int Diterapkan, int Gagal, bool TargetTerhubung);

/// <summary>
/// Mesin antrian dua arah (admin ⇄ cloud). Per siklus: (1) salin outbox sumber ke inbox tujuan
/// dengan dedup berbasis EventId, (2) terapkan inbox tujuan urut rank agregat lalu sequence
/// (dependency ordering), (3) checkpoint per arah + detak untuk indikator UI.
/// Satu event yang gagal tidak menghentikan antrean: retry exponential backoff,
/// setelah 5 percobaan ditandai Gagal dan tetap terlihat (dengan baris SyncConflict bila konflik data).
/// </summary>
public sealed class SyncEngine(
    LaundryDbContext admin,
    CloudDbContext cloud,
    SyncConfig config,
    ILogger<SyncEngine> log)
{
    public const string HeartbeatKey = "sync_detak_utc";
    private static readonly TimeSpan MaksimumTunda = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyDictionary<string, SyncSatuanArah>> JalankanSatuSiklusAsync(CancellationToken ct)
    {
        var kini = DateTime.UtcNow;
        return new Dictionary<string, SyncSatuanArah>
        {
            [SyncDirection.AdminKeCloud] = await PindahkanAsync(admin, cloud, SyncDirection.AdminKeCloud, kini, ct),
            [SyncDirection.CloudKeAdmin] = await PindahkanAsync(cloud, admin, SyncDirection.CloudKeAdmin, kini, ct),
        };
    }

    private static TimeSpan Tunda(int retryCount) =>
        TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Math.Max(retryCount, 1)), MaksimumTunda.TotalSeconds));

    private async Task<SyncSatuanArah> PindahkanAsync(LaundryDbContext sumber, LaundryDbContext tujuan,
        string arah, DateTime kini, CancellationToken ct)
    {
        var detak = await TulisDetakAsync(kini, ct);
        if (!detak) return new SyncSatuanArah(0, 0, 0, false);

        if (!await tujuan.Database.CanConnectAsync(ct))
        {
            log.LogWarning("Sisi {Arah} tidak terjangkau; antrian lokal tetap utuh dan belum terkirim.", arah);
            return new SyncSatuanArah(0, 0, 0, false);
        }

        var terkirim = await KirimAsync(sumber, tujuan, arah, kini, ct);
        var (diterapkan, gagal) = await TerapkanAsync(tujuan, arah, kini, ct);
        await SimpanCheckpointAsync(tujuan, arah, kini, gagal == 0, ct);
        return new SyncSatuanArah(terkirim, diterapkan, gagal, true);
    }

    private async Task<int> KirimAsync(LaundryDbContext sumber, LaundryDbContext tujuan, string arah,
        DateTime kini, CancellationToken ct)
    {
        var antrean = await sumber.SyncOutbox
            .Where(s => s.Status == SyncEventStatus.Menunggu
                && (s.NextRetryAtUtc == null || s.NextRetryAtUtc <= kini))
            .OrderBy(s => s.Sequence).Take(config.Batch).ToListAsync(ct);
        var terkirim = 0;
        foreach (var row in antrean)
        {
            try
            {
                if (!await tujuan.SyncInbox.AnyAsync(i => i.EventId == row.EventId, ct))
                {
                    tujuan.SyncInbox.Add(new SyncInbox
                    {
                        EventId = row.EventId,
                        Sequence = row.Sequence,
                        AggregateType = row.AggregateType,
                        AggregateId = row.AggregateId,
                        Operation = row.Operation,
                        PayloadJson = row.PayloadJson,
                        ReceivedAtUtc = kini,
                    });
                    await tujuan.SaveChangesAsync(ct);
                }
                row.Status = SyncEventStatus.Dikirim;
                row.SentAtUtc = kini;
                row.LastError = null;
                await sumber.SaveChangesAsync(ct);
                terkirim++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                sumber.ChangeTracker.Clear();
                var disimpan = await sumber.SyncOutbox.FirstAsync(o => o.EventId == row.EventId, ct);
                disimpan.RetryCount++;
                disimpan.LastError = Ringkas(ex.Message);
                if (disimpan.RetryCount >= SyncConfig.MaksimumRetry) disimpan.Status = SyncEventStatus.Gagal;
                else disimpan.NextRetryAtUtc = kini + Tunda(disimpan.RetryCount);
                await sumber.SaveChangesAsync(ct);
                log.LogError(ex, "Gagal mengirim event {EventId} ke {Arah}", row.EventId, arah);
            }
        }
        return terkirim;
    }

    private async Task<(int Diterapkan, int Gagal)> TerapkanAsync(LaundryDbContext tujuan, string arah,
        DateTime kini, CancellationToken ct)
    {
        var mentah = await tujuan.SyncInbox
            .Where(i => i.Status == SyncEventStatus.Menunggu
                && (i.NextRetryAtUtc == null || i.NextRetryAtUtc <= kini))
            .ToListAsync(ct);
        var diterapkan = 0;
        var gagal = 0;
        var applier = new SyncApplier(tujuan);
        foreach (var row in mentah
                     .OrderBy(i => SyncAggregate.Rank(i.AggregateType)).ThenBy(i => i.Sequence)
                     .Take(config.Batch))
        {
            try
            {
                await using var tx = await tujuan.Database.BeginTransactionAsync(ct);
                await applier.ApplyAsync(row, kini, ct);
                row.Status = SyncEventStatus.Diproses;
                row.ProcessedAtUtc = kini;
                row.LastError = null;
                await tujuan.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                diterapkan++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                gagal++;
                tujuan.ChangeTracker.Clear();
                log.LogError(ex, "Event {EventId} ({Aggregate}) belum dapat diterapkan pada {Arah}",
                    row.EventId, row.AggregateType, arah);
                await CatatKegagalanAsync(tujuan, row, ex, arah, kini, ct);
            }
        }
        return (diterapkan, gagal);
    }

    private async Task CatatKegagalanAsync(LaundryDbContext tujuan, SyncInbox row, Exception ex, string arah,
        DateTime kini, CancellationToken ct)
    {
        try
        {
            var disimpan = await tujuan.SyncInbox.FirstAsync(i => i.EventId == row.EventId, ct);
            disimpan.RetryCount++;
            disimpan.LastError = Ringkas(ex.Message);
            if (ex is SyncConflictException)
            {
                // Konflik data finansial/identitas: tidak ditaup ulang, dicatat untuk rekonsiliasi.
                disimpan.Status = SyncEventStatus.Gagal;
                disimpan.NextRetryAtUtc = null;
                tujuan.SyncConflicts.Add(new SyncConflict
                {
                    AggregateType = row.AggregateType,
                    AggregateId = row.AggregateId,
                    Detail = $"{arah}: {disimpan.LastError} (event {row.EventId})",
                    Resolution = "PerluRekonsiliasi",
                    CreatedAtUtc = kini,
                });
            }
            else if (disimpan.RetryCount >= SyncConfig.MaksimumRetry)
            {
                disimpan.Status = SyncEventStatus.Gagal;
            }
            else
            {
                disimpan.NextRetryAtUtc = kini + Tunda(disimpan.RetryCount);
            }
            await tujuan.SaveChangesAsync(ct);
        }
        catch (Exception dalam)
        {
            log.LogError(dalam, "Gagal mencatat kegagalan penerapan event {EventId}", row.EventId);
            tujuan.ChangeTracker.Clear();
        }
    }

    private static async Task SimpanCheckpointAsync(LaundryDbContext sisi, string arah, DateTime kini,
        bool bersih, CancellationToken ct)
    {
        var terakhir = await sisi.SyncInbox.Where(i => i.Status == SyncEventStatus.Diproses)
            .Select(i => (long?)i.Sequence).MaxAsync(ct) ?? 0;
        var row = await sisi.SyncCheckpoints.FirstOrDefaultAsync(c => c.Direction == arah, ct);
        if (row is null)
        {
            sisi.SyncCheckpoints.Add(new SyncCheckpoint
            {
                Direction = arah,
                LastSequence = terakhir,
                LastSuccessAtUtc = bersih ? kini : null,
            });
        }
        else
        {
            row.LastSequence = Math.Max(row.LastSequence, terakhir);
            if (bersih) row.LastSuccessAtUtc = kini;
        }
        await sisi.SaveChangesAsync(ct);
    }

    private async Task<bool> TulisDetakAsync(DateTime kini, CancellationToken ct)
    {
        try
        {
            var row = await admin.AppSettings.FirstOrDefaultAsync(a => a.Key == HeartbeatKey, ct);
            var json = JsonSerializer.Serialize(kini);
            if (row is null)
                admin.AppSettings.Add(new AppSetting { Key = HeartbeatKey, ValueJson = json, UpdatedAtUtc = kini });
            else
            {
                row.ValueJson = json;
                row.UpdatedAtUtc = kini;
            }
            await admin.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Database admin lokal tidak bisa diakses sama sekali: worker dianggap offline.
            log.LogWarning(ex, "Database lokal tidak terjangkau saat menulis detak sinkronisasi");
            admin.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Retry manual dari UI: event Gagal di kedua sisi diantrekan ulang dari nol.</summary>
    public async Task<int> CobaUlangGagalAsync(CancellationToken ct)
    {
        var jumlah = 0;
        var kini = DateTime.UtcNow;
        foreach (var sisi in new LaundryDbContext[] { admin, cloud })
        {
            foreach (var row in await sisi.SyncInbox.Where(i => i.Status == SyncEventStatus.Gagal).ToListAsync(ct))
            {
                row.Status = SyncEventStatus.Menunggu;
                row.RetryCount = 0;
                row.NextRetryAtUtc = kini;
                jumlah++;
            }
            foreach (var row in await sisi.SyncOutbox.Where(o => o.Status == SyncEventStatus.Gagal).ToListAsync(ct))
            {
                row.Status = SyncEventStatus.Menunggu;
                row.RetryCount = 0;
                row.NextRetryAtUtc = kini;
                jumlah++;
            }
            await sisi.SaveChangesAsync(ct);
        }
        return jumlah;
    }

    /// <summary>
    /// Event berstatus Gagal pada satu arah, baik di antrean kirim maupun di antrean terima,
    /// supaya tidak ada kegagalan yang hilang diam-diam.
    /// </summary>
    public async Task<List<SyncQueueRow>> AntreanGagalAsync(string arah, CancellationToken ct)
    {
        LaundryDbContext pengirim = arah == SyncDirection.AdminKeCloud ? admin : cloud;
        LaundryDbContext penerima = arah == SyncDirection.AdminKeCloud ? cloud : admin;

        var keluar = await pengirim.SyncOutbox.Where(o => o.Status == SyncEventStatus.Gagal)
            .Select(o => new SyncQueueRow(o.Sequence, o.EventId, o.AggregateType, o.AggregateId, o.Operation,
                o.PayloadJson, o.CreatedAtUtc, o.RetryCount, o.LastError))
            .ToListAsync(ct);
        var masuk = await penerima.SyncInbox.Where(i => i.Status == SyncEventStatus.Gagal)
            .Select(i => new SyncQueueRow(i.Sequence, i.EventId, i.AggregateType, i.AggregateId, i.Operation,
                i.PayloadJson, i.ReceivedAtUtc, i.RetryCount, i.LastError))
            .ToListAsync(ct);

        return keluar.Concat(masuk).OrderBy(r => r.Sequence).Take(200).ToList();
    }

    public async Task<SyncStatusDto> StatusAsync(CancellationToken ct)
    {
        var checkpointAdmin = await admin.SyncCheckpoints.ToListAsync(ct);
        var checkpointCloud = await cloud.SyncCheckpoints.ToListAsync(ct);
        var semua = checkpointAdmin.Concat(checkpointCloud);
        var detakRow = await admin.AppSettings.FirstOrDefaultAsync(a => a.Key == HeartbeatKey, ct);
        DateTime? detak = null;
        if (detakRow is not null)
        {
            try { detak = JsonSerializer.Deserialize<DateTime>(detakRow.ValueJson); }
            catch (JsonException) { detak = null; }
        }
        var segar = detak is { } d && DateTime.UtcNow - d < TimeSpan.FromSeconds(config.IntervalDetik * 3L);
        var sukses = semua.Select(c => c.LastSuccessAtUtc).Where(x => x != null).ToList();
        return new SyncStatusDto(
            await admin.SyncOutbox.CountAsync(s => s.Status == SyncEventStatus.Menunggu, ct),
            await cloud.SyncOutbox.CountAsync(s => s.Status == SyncEventStatus.Menunggu, ct),
            await admin.SyncOutbox.CountAsync(s => s.Status == SyncEventStatus.Gagal, ct),
            await cloud.SyncOutbox.CountAsync(s => s.Status == SyncEventStatus.Gagal, ct),
            await admin.SyncInbox.CountAsync(i => i.Status == SyncEventStatus.Menunggu, ct),
            await cloud.SyncInbox.CountAsync(i => i.Status == SyncEventStatus.Menunggu, ct),
            await admin.SyncInbox.CountAsync(i => i.Status == SyncEventStatus.Gagal, ct),
            await cloud.SyncInbox.CountAsync(i => i.Status == SyncEventStatus.Gagal, ct),
            semua.Where(c => c.Direction == SyncDirection.AdminKeCloud).Select(c => c.LastSequence).DefaultIfEmpty(0).Max(),
            semua.Where(c => c.Direction == SyncDirection.CloudKeAdmin).Select(c => c.LastSequence).DefaultIfEmpty(0).Max(),
            sukses.Count == 0 ? null : sukses.Max(),
            detak, segar);
    }

    private static string Ringkas(string pesan) => pesan.Length > 500 ? pesan[..500] : pesan;
}

/// <summary>Konfigurasi siklus sinkronisasi; angkanya dari environment, tidak tersembunyi di kode.</summary>
public sealed record SyncConfig(int Batch, int IntervalDetik)
{
    public const int MaksimumRetry = 5;

    public static SyncConfig DariLingkungan() => new(
        Math.Clamp(int.TryParse(EnvironmentConfig.Get("SYNC_BATCH"), out var b) ? b : 100, 1, 500),
        Math.Clamp(int.TryParse(EnvironmentConfig.Get("SYNC_INTERVAL_DETIK"), out var i) ? i : 15, 1, 3600));
}
