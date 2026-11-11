using Laundry.Infrastructure.Sync;

namespace Laundry.Sync;

/// <summary>
/// Pengulang tak berujung antrian sinkronisasi dua arah. Gagal satu siklus tidak menghentikan
/// pekerja: event tetap di antrean dengan hitungan retry dan status yang terlihat.
/// </summary>
public sealed class SyncWorker(
    IServiceScopeFactory scopes,
    SyncConfig config,
    ILogger<SyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Pekerja sinkronisasi mulai (interval {Detik} detik, batch {Batch}).",
            config.IntervalDetik, config.Batch);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();
                foreach (var (arah, hasil) in await engine.JalankanSatuSiklusAsync(stoppingToken))
                {
                    if (hasil.Terkirim == 0 && hasil.Diterapkan == 0 && hasil.Gagal == 0) continue;
                    log.LogInformation("{Arah}: terkirim {Terkirim}, diterapkan {Diterapkan}, gagal {Gagal}.",
                        arah, hasil.Terkirim, hasil.Diterapkan, hasil.Gagal);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Siklus sinkronisasi gagal; dicoba lagi pada siklus berikutnya.");
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

        log.LogInformation("Pekerja sinkronisasi berhenti.");
    }
}
