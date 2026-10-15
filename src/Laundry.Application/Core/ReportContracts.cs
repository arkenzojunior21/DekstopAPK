using System.Globalization;
using Laundry.Domain;

namespace Laundry.Application.Core;

/// <summary>
/// Filter laporan dan audit. Tanggal selalu dinormalkan ke hari penuh UTC (lihat RentangLaporan);
/// `KataKunci` dan `Entity` hanya dipakai pencarian audit.
/// </summary>
public sealed record FilterLaporan(DateTime? Dari, DateTime? Sampai, string? KataKunci = null, string? Entity = null)
{
    public RentangLaporan Rentang(DateTime kiniUtc) => RentangLaporan.Normalisasi(Dari, Sampai, kiniUtc);
}

public enum JenisLaporan
{
    Ringkasan,
    Transaksi,
    Harian,
    Layanan,
    Metode,
    Piutang,
    Audit,
}

public enum FormatEkspor
{
    Pdf,
    Xlsx,
    Csv,
}

public enum JenisSel
{
    Teks,
    Uang,
    Bilangan,
    Kuantitas,
}

/// <summary>
/// Satu sel tabel ekspor. Angka disimpan sebagai angka (bukan teks yang sudah diformat) supaya
/// XLSX tetap bisa dihitung; `Isi` adalah bentuk baca-manusia yang dipakai PDF dan CSV.
/// </summary>
public readonly record struct Sel(string Isi, decimal? Angka, JenisSel Jenis)
{
    private static readonly CultureInfo IdId = CultureInfo.GetCultureInfo("id-ID");

    public static Sel Teks(string isi) => new(isi, null, JenisSel.Teks);
    public static Sel Uang(decimal jumlah) => new("Rp" + jumlah.ToString("N0", IdId), jumlah, JenisSel.Uang);
    public static Sel Num(decimal jumlah) => new(jumlah.ToString("N0", IdId), jumlah, JenisSel.Bilangan);
    public static Sel Qty(decimal jumlah) => new(jumlah.ToString("N2", IdId), jumlah, JenisSel.Kuantitas);
    public bool IsAngka => Jenis != JenisSel.Teks;
}

/// <summary>
/// Model tabel netral: sumber data laporan dan mesin ekspor (PDF/XLSX/CSV) berbicara dalam bentuk ini,
/// sehingga satu laporan tidak punya tiga implementasi kolom yang bisa berbeda sendiri-sendiri.
/// </summary>
public sealed record TabelLaporan(string Judul, string Subjudul, IReadOnlyList<string> Kolom,
    IReadOnlyList<bool> KolomAngka, IReadOnlyList<IReadOnlyList<Sel>> Baris)
{
    public int Lebar => Kolom.Count;

    public static TabelLaporan Buat(string judul, string subjudul, IReadOnlyList<(string Judul, bool Angka)> kolom,
        IEnumerable<IReadOnlyList<Sel>> baris)
    {
        return new TabelLaporan(judul, subjudul, [.. kolom.Select(c => c.Judul)],
            [.. kolom.Select(c => c.Angka)], [.. baris]);
    }
}

public sealed record LaporanDto(RentangLaporan Rentang, RingkasanLaporan Ringkasan,
    IReadOnlyList<DeretHarian> Harian, IReadOnlyList<BarisLayanan> Layanan,
    IReadOnlyList<BarisMetode> Metode, IReadOnlyList<BarisPesanan> Transaksi,
    IReadOnlyList<BarisPesanan> Piutang);

public sealed record AuditBaris(Guid Id, DateTime AtUtc, string EntityType, Guid EntityId, string Action,
    string? Actor, string BeforeJson, string AfterJson, string? Reason, string CorrelationId, string Source);

public sealed record AuditHasil(RentangLaporan Rentang, int Total, int Halaman, int UkuranHalaman,
    IReadOnlyList<AuditBaris> Baris);

/// <summary>
/// Laporan usaha (fase 9). Semua angka dihitung dari data tersimpan; tidak ada nominal yang
/// diasumsikan, dan rentang kosong menghasilkan nol — bukan data contoh.
/// </summary>
public interface IReportService
{
    Task<LaporanDto> LaporanAsync(FilterLaporan filter, CancellationToken ct = default);

    /// <summary>
    /// Satu laporan sebagai tabel siap diekspor. Jenis yang sama dipakai PDF, XLSX, CSV, dan layar,
    /// jadi angkanya tidak mungkin berbeda antar format. Ekspor selalu mengambil seluruh baris
    /// laporan, bukan satu halaman.
    /// </summary>
    Task<TabelLaporan> TabelAsync(JenisLaporan jenis, FilterLaporan filter, CancellationToken ct = default);

    /// <summary>Audit transaksi append-only: hanya dibaca, tidak ada jalur ubah/hapus dari aplikasi.</summary>
    Task<AuditHasil> AuditAsync(FilterLaporan filter, int page = 0, int pageSize = 50, CancellationToken ct = default);
}
