using Laundry.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Laundry.Api.Infrastructure;

/// <summary>
/// /health ikut tidak sehat saat database mati. Tanpa pemeriksaan ini, layar login melaporkan
/// "Terhubung" karena proses API-nya hidup, lalu setiap permintaan berikutnya gagal.
/// Batas 3 detik dipakai supaya layar tetap merespons saat host PostgreSQL tidak menjawab.
/// </summary>
public sealed class DatabaseHealthCheck(LaundryDbContext db) : IHealthCheck
{
    private static readonly TimeSpan BatasKoneksi = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var batas = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        batas.CancelAfter(BatasKoneksi);
        try
        {
            return await db.Database.CanConnectAsync(batas.Token)
                ? HealthCheckResult.Healthy("Database sisi admin dapat dihubungi.")
                : HealthCheckResult.Unhealthy("Database sisi admin tidak dapat dihubungi.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy(
                $"Database sisi admin tidak menjawab dalam {BatasKoneksi.TotalSeconds:0} detik.");
        }
    }
}
