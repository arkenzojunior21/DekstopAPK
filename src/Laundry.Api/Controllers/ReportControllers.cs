using System.Globalization;
using Laundry.Application.Core;
using Laundry.Infrastructure.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laundry.Api.Controllers;

/// <summary>
/// Parse filter laporan yang sama dipakai semua endpoint laporan dan audit. Tanggal selalu dianggap
/// UTC (kolom di database juga UTC) supaya batas hari tidak bergeser mengikuti zona waktu komputer
/// admin yang membuka aplikasi.
/// </summary>
internal static class LaporanFilter
{
    private const int MaksimumKataKunci = 64;

    internal static FilterLaporan Buat(string? dari, string? sampai, string? kata = null, string? entitas = null) =>
        new(Tanggal(dari, "dari"), Tanggal(sampai, "sampai"), Bersih(kata, "kata"), Bersih(entitas, "entitas"));

    internal static JenisLaporan Jenis(string? nilai)
    {
        if (Enum.TryParse<JenisLaporan>(nilai, true, out var jenis) && Enum.IsDefined(jenis)) return jenis;
        throw new InvalidOperationException(
            $"Jenis laporan tidak dikenali. Pilihan: {string.Join(", ", Enum.GetNames<JenisLaporan>())}.");
    }

    internal static FormatEkspor Format(string? nilai)
    {
        if (Enum.TryParse<FormatEkspor>(nilai, true, out var format) && Enum.IsDefined(format)) return format;
        // Nama format ditampilkan kecil karena begitu pula pemanggil mengirimkannya (pdf, xlsx, csv).
        throw new InvalidOperationException(
            $"Format ekspor tidak dikenali. Pilihan: {string.Join(", ", Enum.GetNames<FormatEkspor>().Select(n => n.ToLowerInvariant()))}.");
    }

    private static DateTime? Tanggal(string? nilai, string nama)
    {
        if (string.IsNullOrWhiteSpace(nilai)) return null;
        if (!DateTime.TryParse(nilai, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var hasil))
        {
            throw new InvalidOperationException($"Filter '{nama}' bukan tanggal yang sah.");
        }

        return hasil;
    }

    /// <summary>Nilai teks pendek: dipakai mencari di kolom JSON audit, jadi panjangnya dibatasi.</summary>
    private static string? Bersih(string? nilai, string nama)
    {
        if (string.IsNullOrWhiteSpace(nilai)) return null;
        var bersih = nilai.Trim();
        if (bersih.Length > MaksimumKataKunci)
            throw new InvalidOperationException($"Filter '{nama}' maksimal {MaksimumKataKunci} karakter.");
        return bersih;
    }
}

/// <summary>
/// Laporan usaha (fase 9). Khusus Admin: omzet, piutang, dan saldo kredit bukan informasi yang boleh
/// dibaca lintas pelanggan. Rentang tanggal dinormalkan ke hari penuh UTC dan dibatasi 366 hari.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Admin")]
public class ReportsController(IReportService reports) : ControllerBase
{
    /// <summary>Semua angka laporan sekaligus (ringkasan, deret harian, layanan, metode, transaksi, piutang).</summary>
    [HttpGet("laporan")]
    public Task<LaporanDto> Laporan([FromQuery] string? dari, [FromQuery] string? sampai, CancellationToken ct) =>
        reports.LaporanAsync(LaporanFilter.Buat(dari, sampai), ct);

    /// <summary>Satu laporan sebagai tabel netral; bentuk persis yang dipakai mesin ekspor.</summary>
    [HttpGet("tabel")]
    public async Task<TabelLaporan> Tabel([FromQuery] string? jenis, [FromQuery] string? dari,
        [FromQuery] string? sampai, CancellationToken ct) =>
        await reports.TabelAsync(LaporanFilter.Jenis(jenis), LaporanFilter.Buat(dari, sampai), ct);

    /// <summary>
    /// Ekspor laporan ke PDF, XLSX, atau CSV. `jenis=audit` mengekspor jejak audit, jadi hanya ada
    /// satu jalur ekspor untuk semua laporan.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Ekspor([FromQuery] string? jenis, [FromQuery] string? format,
        [FromQuery] string? dari, [FromQuery] string? sampai, CancellationToken ct)
    {
        var tabel = await reports.TabelAsync(LaporanFilter.Jenis(jenis), LaporanFilter.Buat(dari, sampai), ct);
        var hasil = EksporDokumen.Buat(tabel, LaporanFilter.Format(format), DateTime.UtcNow);
        return File(hasil.Isi, hasil.TipeMime, hasil.NamaBerkas);
    }
}

/// <summary>
/// Pencarian jejak audit transaksi (append-only). Endpoint ini hanya membaca: tidak ada jalur ubah
/// atau hapus dari aplikasi, dan ekspor dilakukan lewat /api/reports/export?jenis=audit.
/// </summary>
[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Admin")]
public class AuditController(IReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<AuditHasil> Daftar([FromQuery] string? dari, [FromQuery] string? sampai,
        [FromQuery] string? kata, [FromQuery] string? entitas, [FromQuery] int halaman = 0,
        [FromQuery] int ukuran = 50, CancellationToken ct = default) =>
        reports.AuditAsync(LaporanFilter.Buat(dari, sampai, kata, entitas), halaman, ukuran, ct);
}
