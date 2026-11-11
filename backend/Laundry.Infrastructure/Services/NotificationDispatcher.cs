using Laundry.Application.Abstractions;
using Laundry.Application.Auth;
using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laundry.Infrastructure.Services;

/// <summary>
/// Mengosongkan antrean notifikasi lewat adapter WhatsApp (PRD 6.12).
/// Aturan utama: nomor tujuan yang tidak bisa diselesaikan ditandai DitundaPerluKoneksi,
/// bukan Terkirim — operasi eksternal yang tidak bisa diverifikasi tidak pernah pura-pura sukses.
/// Idempotensi dijamin EventId unik pada antrean, jadi kirim ulang tidak menghasilkan pesan ganda.
/// </summary>
public sealed class NotificationDispatcher(
    LaundryDbContext db,
    IWhatsAppSender sender,
    SyncConfig config,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async Task<NotificationDrainResult> DrainAsync(CancellationToken ct = default)
    {
        var rows = await db.NotificationOutbox
            .Where(n => n.Status == NotificationStatus.Menunggu && n.RetryCount < SyncConfig.MaksimumRetry)
            .OrderBy(n => n.CreatedAtUtc)
            .Take(config.Batch)
            .ToListAsync(ct);

        var terkirim = 0;
        var gagal = 0;
        var ditunda = 0;

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            var tujuan = PhoneNormalizer.Normalize(row.TargetPhone);
            if (tujuan is null)
            {
                row.Status = NotificationStatus.DitundaPerluKoneksi;
                row.LastError = "Nomor tujuan tidak sah atau belum diketahui.";
                ditunda++;
                logger.LogWarning("Notifikasi {Template} ditunda: nomor tujuan {Target} tidak dapat diselesaikan.",
                    row.TemplateKey, row.TargetPhone);
                continue;
            }

            try
            {
                row.ProviderMessageId = await sender.SendAsync(row.TemplateKey, tujuan, row.PayloadJson, ct);
                row.Status = NotificationStatus.Terkirim;
                row.SentAtUtc = DateTime.UtcNow;
                row.LastError = null;
                terkirim++;
            }
            catch (ProviderUnavailableException ex)
            {
                row.Status = NotificationStatus.DitundaPerluKoneksi;
                row.LastError = ex.Message;
                ditunda++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                row.RetryCount++;
                row.LastError = Ringkas(ex.Message);
                if (row.RetryCount >= SyncConfig.MaksimumRetry)
                {
                    row.Status = NotificationStatus.Gagal;
                    gagal++;
                }
                logger.LogWarning(ex, "Pengiriman notifikasi {Id} percobaan {Retry} gagal.",
                    row.Id, row.RetryCount);
            }
        }

        await db.SaveChangesAsync(ct);
        return new NotificationDrainResult(rows.Count, terkirim, gagal, ditunda);
    }

    public async Task<NotificationQueueStatus> QueueStatusAsync(int contoh, CancellationToken ct = default)
    {
        var kelompok = await db.NotificationOutbox
            .GroupBy(n => n.Status)
            .Select(g => new { Status = g.Key, Jumlah = g.Count() })
            .ToListAsync(ct);

        List<NotificationQueueRow> macet = await db.NotificationOutbox
            .Where(n => n.Status == NotificationStatus.Gagal || n.Status == NotificationStatus.DitundaPerluKoneksi)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(Math.Max(contoh, 0))
            .Select(n => new NotificationQueueRow(n.Id, n.TemplateKey, n.TargetPhone, n.EventId, n.Status,
                n.RetryCount, n.ProviderMessageId, n.LastError, n.CreatedAtUtc, n.SentAtUtc))
            .ToListAsync(ct);

        int Ambil(NotificationStatus status) => kelompok.Where(g => g.Status == status).Sum(g => g.Jumlah);
        return new NotificationQueueStatus(Ambil(NotificationStatus.Menunggu), Ambil(NotificationStatus.Terkirim),
            Ambil(NotificationStatus.Gagal), Ambil(NotificationStatus.DitundaPerluKoneksi), macet);
    }

    private static string Ringkas(string pesan) => pesan.Length > 500 ? pesan[..500] : pesan;
}
