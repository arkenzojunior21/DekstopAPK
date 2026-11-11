namespace Laundry.Domain;

/// <summary>
/// Rentang kalender laporan. Batas bawah dan atas diminta sebagai tanggal; hasilnya selalu
/// sehari penuh UTC dan batas atas bersifat eksklusif, jadi pemanggil tidak perlu menebak
/// apakah "sampai 15 Oktober" ikut menghitung hari ke-15.
/// </summary>
public readonly record struct RentangLaporan(DateTime MulaiUtc, DateTime BatasAkhirUtc)
{
    public const int MaksimumHari = 366;

    public static RentangLaporan Normalisasi(DateTime? dari, DateTime? sampai, DateTime kiniUtc,
        int hariBawaan = 30)
    {
        var akhir = (sampai ?? kiniUtc).Date;
        var mulai = (dari ?? akhir.AddDays(-(hariBawaan - 1))).Date;
        if (mulai > akhir) (mulai, akhir) = (akhir, mulai);

        // Rentang terlalu lebar: tepi bawah yang digeser, supaya data terbaru tetap terbaca.
        if ((akhir - mulai).Days + 1 > MaksimumHari) mulai = akhir.AddDays(-(MaksimumHari - 1));
        return new RentangLaporan(mulai, akhir.AddDays(1));
    }

    public bool Dalam(DateTime utc) => utc >= MulaiUtc && utc < BatasAkhirUtc;
    public int JumlahHari => (BatasAkhirUtc - MulaiUtc).Days;
    public DateTime HariKe(int i) => MulaiUtc.AddDays(i);

    public IEnumerable<DateTime> Harinya()
    {
        for (var hari = 0; hari < JumlahHari; hari++) yield return HariKe(hari);
    }
}

/// <summary>Satu pesanan + uang yang sudah menempel padanya, hasil proyeksi dari database.</summary>
public sealed record BarisPesanan(Guid Id, string OrderNumber, string? NamaPelanggan, DateTime DibuatUtc,
    OrderStatus Status, decimal TotalAkhir, decimal PembayaranSukses, decimal RefundSukses,
    decimal KreditTerpakai, decimal Dp, decimal Pelunasan)
{
    public bool Dibatalkan => Status == OrderStatus.Dibatalkan;

    /// <summary>Sisa tagihan dengan aturan yang sama dengan kas: refund mengurangi pembayaran.</summary>
    public decimal Sisa => LaporanAngka.Sisa(TotalAkhir, PembayaranSukses, RefundSukses, KreditTerpakai);
}

public sealed record BarisUang(DateTime TerkonfirmasiUtc, decimal Jumlah, PaymentMethod Metode, PaymentKind Kind);
public sealed record BarisRefund(DateTime TerbuatUtc, decimal Jumlah);
public sealed record BarisKredit(DateTime TerbuatUtc, decimal Delta);

/// <summary>Satu baris layanan pada sebuah pesanan; kuantitas boleh aktual atau estimasi.</summary>
public sealed record BarisItemLayanan(Guid IdPesanan, string Nama, ServiceUnitType Satuan, decimal? BeratAktual,
    decimal? ItemAktual, decimal? BeratEstimasi, decimal? ItemEstimasi, decimal Subtotal);

public sealed record RingkasanLaporan(DateTime MulaiUtc, DateTime BatasAkhirUtc, int JumlahPesanan,
    int JumlahDibatalkan, decimal Omzet, decimal Penerimaan, decimal Dp, decimal Pelunasan,
    decimal Refund, decimal Piutang, int JumlahTagihanTerbuka, decimal SaldoKreditPosisi,
    decimal KreditMasuk, decimal KreditDipakai);

public sealed record DeretHarian(DateTime Tanggal, int JumlahPesanan, int JumlahDibatalkan,
    decimal Omzet, decimal Penerimaan, decimal Refund);

public sealed record BarisLayanan(string Nama, ServiceUnitType Satuan, int JumlahBaris, decimal Kuantitas,
    decimal Subtotal);

public sealed record BarisMetode(PaymentMethod Metode, int JumlahTransaksi, decimal Jumlah);

/// <summary>
/// Enam angka yang wajib dibedakan laporan (PROMPT_AGENT fase 9): omzet, penerimaan, DP,
/// piutang, refund, dan saldo kredit. Ditaruh di Domain supaya bisa dibuktikan tanpa database
/// dan tanpa EF, dan supaya tidak ada service lain yang mengarang rumusnya sendiri.
/// </summary>
public static class LaporanAngka
{
    /// <summary>Sisa tagihan: total final dikurangi pembayaran neto (setelah refund) dan kredit terpakai.</summary>
    public static decimal Sisa(decimal totalAkhir, decimal pembayaran, decimal refund, decimal kredit) =>
        Math.Max(totalAkhir - (pembayaran - refund) - kredit, 0m);

    /// <summary>Kuantitas yang dihitung per layanan: angka aktual menang, estimasi jadi cadangan.</summary>
    public static decimal Kuantitas(BarisItemLayanan item) => item.Satuan switch
    {
        ServiceUnitType.PerKg => item.BeratAktual ?? item.BeratEstimasi ?? 0m,
        ServiceUnitType.PerItem => item.ItemAktual ?? item.ItemEstimasi ?? 0m,
        _ => item.ItemAktual ?? item.ItemEstimasi ?? 1m,
    };

    public static RingkasanLaporan Hitung(RentangLaporan rentang, IReadOnlyList<BarisPesanan> pesanan,
        IEnumerable<BarisUang> uang, IEnumerable<BarisRefund> refund, IEnumerable<BarisKredit> kredit)
    {
        var dalam = pesanan.Where(p => rentang.Dalam(p.DibuatUtc)).ToList();
        var hidup = dalam.Where(p => !p.Dibatalkan).ToList();
        var uangRentang = uang.Where(u => rentang.Dalam(u.TerkonfirmasiUtc)).ToList();
        var refundRentang = refund.Where(r => rentang.Dalam(r.TerbuatUtc)).ToList();
        var kreditRentang = kredit.Where(k => rentang.Dalam(k.TerbuatUtc)).ToList();

        // Piutang adalah posisi, bukan arus: semua pesanan yang belum lunas sampai batas akhir rentang,
        // termasuk yang dibuat sebelum rentang dimulai. Pesanan dibatalkan tidak menagih apa pun.
        var terbuka = pesanan.Where(p => !p.Dibatalkan && p.DibuatUtc < rentang.BatasAkhirUtc
            && p.Sisa > 0m).ToList();

        return new RingkasanLaporan(rentang.MulaiUtc, rentang.BatasAkhirUtc,
            JumlahPesanan: hidup.Count,
            JumlahDibatalkan: dalam.Count - hidup.Count,
            Omzet: hidup.Sum(p => p.TotalAkhir),
            Penerimaan: uangRentang.Sum(u => u.Jumlah),
            Dp: uangRentang.Where(u => u.Kind == PaymentKind.Dp).Sum(u => u.Jumlah),
            Pelunasan: uangRentang.Where(u => u.Kind == PaymentKind.Pelunasan).Sum(u => u.Jumlah),
            Refund: refundRentang.Sum(r => r.Jumlah),
            Piutang: terbuka.Sum(p => p.Sisa),
            JumlahTagihanTerbuka: terbuka.Count,
            // Posisi saldo per akhir rentang = seluruh ledger sampai batas itu, termasuk dari masa lalu.
            SaldoKreditPosisi: kredit.Where(k => k.TerbuatUtc < rentang.BatasAkhirUtc).Sum(k => k.Delta),
            KreditMasuk: kreditRentang.Where(k => k.Delta > 0).Sum(k => k.Delta),
            KreditDipakai: -kreditRentang.Where(k => k.Delta < 0).Sum(k => k.Delta));
    }

    /// <summary>Deret untuk grafik: setiap hari di rentang muncul, termasuk hari nol supaya sumbu tidak melompat.</summary>
    public static IReadOnlyList<DeretHarian> Harian(RentangLaporan rentang, IReadOnlyList<BarisPesanan> pesanan,
        IEnumerable<BarisUang> uang, IEnumerable<BarisRefund> refund)
    {
        var perOrder = pesanan.GroupBy(p => p.DibuatUtc.Date).ToDictionary(g => g.Key, g => g.ToList());
        var perUang = uang.Where(u => rentang.Dalam(u.TerkonfirmasiUtc)).GroupBy(u => u.TerkonfirmasiUtc.Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Jumlah));
        var perRefund = refund.Where(r => rentang.Dalam(r.TerbuatUtc)).GroupBy(r => r.TerbuatUtc.Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Jumlah));

        var hasil = new List<DeretHarian>(rentang.JumlahHari);
        foreach (var hari in rentang.Harinya())
        {
            perOrder.TryGetValue(hari, out var baris);
            baris ??= [];
            hasil.Add(new DeretHarian(hari,
                JumlahPesanan: baris.Count(p => !p.Dibatalkan),
                JumlahDibatalkan: baris.Count(p => p.Dibatalkan),
                Omzet: baris.Where(p => !p.Dibatalkan).Sum(p => p.TotalAkhir),
                Penerimaan: perUang.GetValueOrDefault(hari),
                Refund: perRefund.GetValueOrDefault(hari)));
        }

        return hasil;
    }

    public static IReadOnlyList<BarisLayanan> Layanan(IReadOnlySet<Guid> idDalamRentang,
        IEnumerable<BarisItemLayanan> items)
    {
        return items.Where(i => idDalamRentang.Contains(i.IdPesanan)).GroupBy(i => (i.Nama, i.Satuan))
            .Select(g => new BarisLayanan(g.Key.Nama, g.Key.Satuan, g.Count(),
                g.Sum(Kuantitas), g.Sum(i => i.Subtotal)))
            .OrderByDescending(b => b.Subtotal).ThenBy(b => b.Nama, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<BarisMetode> Metode(RentangLaporan rentang, IEnumerable<BarisUang> uang)
    {
        return uang.Where(u => rentang.Dalam(u.TerkonfirmasiUtc)).GroupBy(u => u.Metode)
            .Select(g => new BarisMetode(g.Key, g.Count(), g.Sum(u => u.Jumlah)))
            .OrderByDescending(m => m.Jumlah).ToList();
    }

    /// <summary>Detail transaksi untuk tabel dan ekspor; urutan newest-first seperti daftar pesanan.</summary>
    public static IReadOnlyList<BarisPesanan> Transaksi(RentangLaporan rentang,
        IReadOnlyList<BarisPesanan> pesanan) =>
        [.. pesanan.Where(p => rentang.Dalam(p.DibuatUtc)).OrderByDescending(p => p.DibuatUtc)
            .ThenBy(p => p.OrderNumber, StringComparer.OrdinalIgnoreCase)];
}
