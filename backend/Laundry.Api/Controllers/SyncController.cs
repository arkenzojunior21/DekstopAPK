using Laundry.Application.Sync;
using Laundry.Infrastructure.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laundry.Api.Controllers;

/// <summary>
/// Jendela operasional sinkronisasi untuk UI: status dua arah, daftar event gagal,
/// jalankan siklus sekarang, dan retry manual. Semua hanya untuk Admin.
/// </summary>
[ApiController]
[Route("api/sync")]
[Authorize(Roles = "Admin")]
public sealed class SyncController(SyncEngine engine) : ControllerBase
{
    [HttpGet("status")]
    public Task<SyncStatusDto> Status(CancellationToken ct) => engine.StatusAsync(ct);

    [HttpGet("failed")]
    public async Task<List<SyncQueueRow>> Failed([FromQuery] string arah, CancellationToken ct)
    {
        if (arah is not (SyncDirection.AdminKeCloud or SyncDirection.CloudKeAdmin))
            throw new InvalidOperationException("Arah harus admin_ke_cloud atau cloud_ke_admin.");
        return await engine.AntreanGagalAsync(arah, ct);
    }

    /// <summary>Satu siklus manual (tombol "Sinkron sekarang" di UI).</summary>
    [HttpPost("run")]
    public async Task<Dictionary<string, SyncSatuanArah>> Run(CancellationToken ct) =>
        new(await engine.JalankanSatuSiklusAsync(ct));

    /// <summary>Mengantrekan ulang event berstatus Gagal di kedua sisi dari nol.</summary>
    [HttpPost("retry-failed")]
    public Task<int> RetryFailed(CancellationToken ct) => engine.CobaUlangGagalAsync(ct);
}
