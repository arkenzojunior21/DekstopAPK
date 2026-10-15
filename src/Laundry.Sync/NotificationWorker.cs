using Laundry.Application.Core;
using Laundry.Infrastructure.Sync;

namespace Laundry.Sync;

/// <summary>
/// Pengulang tak berujung antrean notifikasi. Adapter yang terpasang masih mock, jadi baris
/// ditandai terkirim oleh simulasi — status provider menunjukkan live=false untuk adapter itu.
/// Nomor yang tidak sah tetap ditandai perlu koneksi, bukan disembunyikan sebagai sukses.
/// </summary>
public sealed class NotificationWorker(
    IServiceScopeFactory scopes,
    SyncConfig config,
    ILogger<NotificationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Pekerja notifikasi mulai (interval {Detik} detik).", config.IntervalDetik);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                var hasil = await dispatcher.DrainAsync(stoppingToken);
                if (hasil.Diproses > 0)
                {
                    log.LogInformation("Notifikasi: dicoba {Dicoba}, terkirim {Terkirim}, gagal {Gagal}, ditunda {Ditunda}.",
                        hasil.Diproses, hasil.Terkirim, hasil.Gagal, hasil.Ditunda);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Siklus notifikasi gagal; dicoba lagi pada siklus berikutnya.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(config.IntervalDetik), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        log.LogInformation("Pekerja notifikasi berhenti.");
    }
}
