using System.Globalization;
using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Services;

/// <summary>
/// Membaca laporan dari database lalu menyerahkan SEMUA perhitungan angka ke `LaporanAngka` (Domain).
/// Pemisahan ini sengaja: query hanya memproyeksikan baris, jadi aturan omzet/penerimaan/piutang
/// bisa diuji tanpa PostgreSQL dan tidak bisa berbeda antara layar laporan dan berkas ekspor.
/// </summary>
public sealed class ReportService(LaundryDbContext db) : IReportService
{
    private const int MaksimumBarisEkspor = 5000;
    private static readonly CultureInfo IdId = CultureInfo.GetCultureInfo("id-ID");

    public async Task<LaporanDto> LaporanAsync(FilterLaporan filter, CancellationToken ct = default)
    {
        var rentang = filter.Rentang(DateTime.UtcNow);
        var data = await MuatAsync(rentang, ct);
        var transaksi = LaporanAngka.Transaksi(rentang, data.Pesanan);
        var id = transaksi.Select(t => t.Id).ToHashSet();

        return new LaporanDto(rentang,
            LaporanAngka.Hitung(rentang, data.Pesanan, data.Uang, data.Refund, data.Kredit),
            LaporanAngka.Harian(rentang, data.Pesanan, data.Uang, data.Refund),
            LaporanAngka.Layanan(id, data.Items),
            LaporanAngka.Metode(rentang, data.Uang),
            transaksi, Terbuka(data.Pesanan, rentang));
    }

    public async Task<TabelLaporan> TabelAsync(JenisLaporan jenis, FilterLaporan filter, CancellationToken ct = default)
    {
        var rentang = filter.Rentang(DateTime.UtcNow);
        var subjudul = Subjudul(rentang);

        if (jenis == JenisLaporan.Audit)
        {
            var hasil = await AuditAsync(filter, 0, MaksimumBarisEkspor, ct);
            return TabelLaporan.Buat("Audit transaksi", $"{subjudul} · {hasil.Total} kejadian, " +
                $"maksimum {MaksimumBarisEkspor} baris terbaru diekspor",
            [
                ("Waktu", false), ("Tipe", false), ("Entitas", false), ("Aksi", false), ("Aktor", false),
                ("Sebelum", false), ("Sesudah", false), ("Alasan", false), ("Sumber", false),
            ], hasil.Baris.Select(a => (IReadOnlyList<Sel>)(new[]
            {
                Sel.Teks(Waktu(a.AtUtc)), Sel.Teks(a.EntityType), Sel.Teks(a.EntityId.ToString("N")),
                Sel.Teks(a.Action), Sel.Teks(a.Actor ?? "-"), Sel.Teks(Potong(a.BeforeJson)),
                Sel.Teks(Potong(a.AfterJson)), Sel.Teks(Potong(a.Reason)), Sel.Teks(a.Source),
            })));
        }

        var data = await MuatAsync(rentang, ct);
        var transaksi = LaporanAngka.Transaksi(rentang, data.Pesanan);
        var id = transaksi.Select(t => t.Id).ToHashSet();

        return jenis switch
        {
            JenisLaporan.Ringkasan => RingkasanTabel(rentang, data),
            JenisLaporan.Harian => TabelLaporan.Buat("Laporan harian", subjudul,
            [
                ("Tanggal", false), ("Pesanan", true), ("Batal", true), ("Omzet", true),
                ("Penerimaan", true), ("Refund", true),
            ], LaporanAngka.Harian(rentang, data.Pesanan, data.Uang, data.Refund)
                .Select(h => (IReadOnlyList<Sel>)(new[]
                {
                    Sel.Teks(h.Tanggal.ToString("dd MMM yyyy", IdId)), Sel.Num(h.JumlahPesanan),
                    Sel.Num(h.JumlahDibatalkan), Sel.Uang(h.Omzet), Sel.Uang(h.Penerimaan), Sel.Uang(h.Refund),
                }))),
            JenisLaporan.Layanan => TabelLaporan.Buat("Laporan per layanan", subjudul,
            [
                ("Layanan", false), ("Satuan", false), ("Baris", true), ("Kuantitas", true), ("Subtotal", true),
            ], LaporanAngka.Layanan(id, data.Items).Select(l => (IReadOnlyList<Sel>)(new[]
            {
                Sel.Teks(l.Nama), Sel.Teks(Satuan(l.Satuan)), Sel.Num(l.JumlahBaris), Sel.Qty(l.Kuantitas),
                Sel.Uang(l.Subtotal),
            }))),
            JenisLaporan.Metode => TabelLaporan.Buat("Penerimaan per metode", subjudul,
            [("Metode", false), ("Transaksi", true), ("Jumlah", true)],
            LaporanAngka.Metode(rentang, data.Uang).Select(m => (IReadOnlyList<Sel>)(new[]
            {
                Sel.Teks(m.Metode.ToString()), Sel.Num(m.JumlahTransaksi), Sel.Uang(m.Jumlah),
            }))),
            JenisLaporan.Piutang => TabelLaporan.Buat("Piutang per akhir periode", subjudul,
            [
                ("Nomor", false), ("Tanggal", false), ("Pelanggan", false), ("Total", true),
                ("Dibayar", true), ("Sisa", true),
            ], Terbuka(data.Pesanan, rentang).Select(p => (IReadOnlyList<Sel>)(new[]
            {
                Sel.Teks(p.OrderNumber), Sel.Teks(p.DibuatUtc.ToString("dd MMM yyyy", IdId)),
                Sel.Teks(p.NamaPelanggan ?? "walk-in"), Sel.Uang(p.TotalAkhir),
                Sel.Uang(p.TotalAkhir - p.Sisa), Sel.Uang(p.Sisa),
            }))),
            _ => TabelLaporan.Buat("Laporan transaksi", subjudul,
            [
                ("Nomor", false), ("Tanggal", false), ("Pelanggan", false), ("Status", false),
                ("Total", true), ("DP", true), ("Pembayaran", true), ("Refund", true), ("Kredit", true),
                ("Sisa", true),
            ], transaksi.Select(p => (IReadOnlyList<Sel>)(new[]
            {
                Sel.Teks(p.OrderNumber), Sel.Teks(p.DibuatUtc.ToString("dd MMM yyyy", IdId)),
                Sel.Teks(p.NamaPelanggan ?? "walk-in"), Sel.Teks(p.Status.ToString()), Sel.Uang(p.TotalAkhir),
                Sel.Uang(p.Dp), Sel.Uang(p.PembayaranSukses), Sel.Uang(p.RefundSukses),
                Sel.Uang(p.KreditTerpakai), Sel.Uang(p.Sisa),
            }))),
        };
    }

    public async Task<AuditHasil> AuditAsync(FilterLaporan filter, int page = 0, int pageSize = 50,
        CancellationToken ct = default)
    {
        var rentang = filter.Rentang(DateTime.UtcNow);
        var q = db.AuditLogs.AsNoTracking().Where(a => a.AtUtc >= rentang.MulaiUtc && a.AtUtc < rentang.BatasAkhirUtc);

        if (!string.IsNullOrWhiteSpace(filter.Entity))
        {
            var entity = filter.Entity.Trim();
            q = q.Where(a => a.EntityType.ToLower() == entity.ToLower());
        }

        var kata = filter.KataKunci?.Trim();
        if (!string.IsNullOrEmpty(kata))
        {
            // Jejak correlation/alasan/isi JSON ikut dicari: nomor pesanan hanya muncul di dalam before/after.
            q = q.Where(a => a.Action.Contains(kata) || a.CorrelationId.Contains(kata)
                || (a.Reason != null && a.Reason.Contains(kata))
                || a.EntityId.ToString().Contains(kata)
                || a.BeforeJson.Contains(kata) || a.AfterJson.Contains(kata));
        }

        var total = await q.CountAsync(ct);
        var ukuran = Math.Clamp(pageSize, 1, 500);
        var baris = await q.OrderByDescending(a => a.AtUtc).Skip(page * ukuran).Take(ukuran)
            .Select(a => new
            {
                a.Id, a.AtUtc, a.EntityType, a.EntityId, a.Action, a.ActorUserId,
                a.BeforeJson, a.AfterJson, a.Reason, a.CorrelationId, a.Source,
            })
            .ToListAsync(ct);

        var aktor = await NamaAktorAsync(baris.Select(b => b.ActorUserId).Distinct().ToList(), ct);
        return new AuditHasil(rentang, total, page, ukuran,
        [
            .. baris.Select(b => new AuditBaris(b.Id, b.AtUtc, b.EntityType, b.EntityId, b.Action,
                aktor.GetValueOrDefault(b.ActorUserId), b.BeforeJson, b.AfterJson, b.Reason, b.CorrelationId,
                b.Source)),
        ]);
    }

    private async Task<Dictionary<Guid, string>> NamaAktorAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, Nama = u.Profile!.Nama, u.NormalizedPhone })
            .ToDictionaryAsync(r => r.Id, r => string.IsNullOrWhiteSpace(r.Nama) ? r.NormalizedPhone : r.Nama, ct);
    }

    private TabelLaporan RingkasanTabel(RentangLaporan rentang, (List<BarisPesanan> Pesanan, List<BarisUang> Uang,
        List<BarisRefund> Refund, List<BarisKredit> Kredit, List<BarisItemLayanan> Items) data)
    {
        var r = LaporanAngka.Hitung(rentang, data.Pesanan, data.Uang, data.Refund, data.Kredit);
        var baris = new (string Label, decimal Angka, bool Uang)[]
        {
            ("Omzet (pesanan dibuat, tanpa yang dibatalkan)", r.Omzet, true),
            ("Penerimaan (uang masuk tercatat)", r.Penerimaan, true),
            ("Penerimaan dari DP", r.Dp, true),
            ("Penerimaan dari pelunasan", r.Pelunasan, true),
            ("Refund keluar", r.Refund, true),
            ("Piutang per akhir periode", r.Piutang, true),
            ("Saldo kredit pelanggan (posisi)", r.SaldoKreditPosisi, true),
            ("Kredit masuk periode ini", r.KreditMasuk, true),
            ("Kredit dipakai periode ini", r.KreditDipakai, true),
        };

        return TabelLaporan.Buat("Ringkasan laporan", Subjudul(rentang),
            [("Ukuran", false), ("Nilai", true)],
            baris.Select(b => (IReadOnlyList<Sel>)(new[] { Sel.Teks(b.Label), Sel.Uang(b.Angka) }))
                .Append((IReadOnlyList<Sel>)[Sel.Teks("Jumlah pesanan (tidak dibatalkan)"), Sel.Num(r.JumlahPesanan)])
                .Append((IReadOnlyList<Sel>)[Sel.Teks("Pesanan dibatalkan"), Sel.Num(r.JumlahDibatalkan)])
                .Append((IReadOnlyList<Sel>)[Sel.Teks("Tagihan masih terbuka"), Sel.Num(r.JumlahTagihanTerbuka)]));
    }

    private static List<BarisPesanan> Terbuka(IReadOnlyList<BarisPesanan> pesanan, RentangLaporan rentang) =>
        [.. pesanan.Where(p => !p.Dibatalkan && p.DibuatUtc < rentang.BatasAkhirUtc && p.Sisa > 0m)
            .OrderByDescending(p => p.DibuatUtc)];

    private async Task<(List<BarisPesanan> Pesanan, List<BarisUang> Uang, List<BarisRefund> Refund,
        List<BarisKredit> Kredit, List<BarisItemLayanan> Items)> MuatAsync(RentangLaporan rentang,
        CancellationToken ct)
    {
        // Pesanan diambil sampai batas akhir rentang (bukan hanya yang dibuat di dalamnya): piutang
        // adalah posisi, jadi tagihan lama yang belum lunas wajib ikut terlihat.
        var pesanan = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAtUtc < rentang.BatasAkhirUtc)
            .Select(o => new BarisPesanan(o.Id, o.OrderNumber, o.Customer!.Nama, o.CreatedAtUtc, o.Status,
                o.FinalTotal,
                o.Payments.Where(p => p.Status == PaymentStatus.Lunas).Sum(p => p.Amount),
                o.Refunds.Where(r => r.Status == PaymentStatus.Lunas).Sum(r => r.Amount),
                o.AppliedCredit,
                o.Payments.Where(p => p.Status == PaymentStatus.Lunas && p.Kind == PaymentKind.Dp)
                    .Sum(p => p.Amount),
                o.Payments.Where(p => p.Status == PaymentStatus.Lunas && p.Kind == PaymentKind.Pelunasan)
                    .Sum(p => p.Amount)))
            .ToListAsync(ct);

        var uang = await db.Payments.AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Lunas && p.ConfirmedAtUtc != null
                && p.ConfirmedAtUtc >= rentang.MulaiUtc && p.ConfirmedAtUtc < rentang.BatasAkhirUtc)
            .Select(p => new BarisUang(p.ConfirmedAtUtc!.Value, p.Amount, p.Method, p.Kind))
            .ToListAsync(ct);

        var refund = await db.Refunds.AsNoTracking()
            .Where(r => r.Status == PaymentStatus.Lunas && r.CreatedAtUtc >= rentang.MulaiUtc
                && r.CreatedAtUtc < rentang.BatasAkhirUtc)
            .Select(r => new BarisRefund(r.CreatedAtUtc, r.Amount))
            .ToListAsync(ct);

        var kredit = await db.CreditLedger.AsNoTracking()
            .Where(k => k.CreatedAtUtc < rentang.BatasAkhirUtc)
            .Select(k => new BarisKredit(k.CreatedAtUtc, k.Delta))
            .ToListAsync(ct);

        var items = await db.OrderItems.AsNoTracking()
            .Where(i => i.Order != null && i.Order.CreatedAtUtc >= rentang.MulaiUtc
                && i.Order.CreatedAtUtc < rentang.BatasAkhirUtc)
            .Select(i => new BarisItemLayanan(i.OrderId, i.ServiceNameSnapshot, i.UnitTypeSnapshot,
                i.ActualWeightKg, i.ActualItemCount, i.EstimatedWeightKg, i.EstimatedItemCount, i.Subtotal))
            .ToListAsync(ct);

        return (pesanan, uang, refund, kredit, items);
    }

    private static string Subjudul(RentangLaporan rentang)
    {
        var mulai = rentang.MulaiUtc.ToString("dd MMM yyyy", IdId);
        var akhir = rentang.BatasAkhirUtc.AddDays(-1).ToString("dd MMM yyyy", IdId);
        var kini = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return $"Periode {mulai} - {akhir} (UTC), dibuat {kini} UTC";
    }

    private static string Satuan(ServiceUnitType satuan) => satuan switch
    {
        ServiceUnitType.PerKg => "kg",
        ServiceUnitType.PerItem => "item",
        _ => "paket",
    };

    private static string Waktu(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", IdId);

    private static string Potong(string? teks, int maksimum = 160) =>
        string.IsNullOrEmpty(teks) ? "-" : teks.Length > maksimum ? teks[..maksimum] + "..." : teks;
}
