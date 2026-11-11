using Laundry.Domain;

namespace Laundry.Tests.Unit;

/// <summary>
/// Aturan angka laporan (fase 9) diuji tanpa database: enam konsep yang wajib dibedakan — omzet,
/// penerimaan, DP, piutang, refund, saldo kredit — plus normalisasi rentang tanggal. Kalau salah
/// satu bocor, laporan di layar dan di berkas ekspor ikut salah tanpa terlihat.
/// </summary>
public class ReportMathTests
{
    private static readonly DateTime Awal = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Kini = new(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);

    private static BarisPesanan Pesanan(DateTime dibuat, OrderStatus status, decimal total,
        decimal bayar = 0m, decimal refund = 0m, decimal kredit = 0m, decimal dp = 0m) =>
        new(Guid.NewGuid(), $"LDR-{dibuat:yyyyMMdd}-01", "Budi", dibuat, status, total, bayar, refund, kredit,
            dp, bayar - dp);

    private static BarisUang Uang(DateTime kapan, decimal jumlah, PaymentKind kind = PaymentKind.Pelunasan,
        PaymentMethod metode = PaymentMethod.Tunai) => new(kapan, jumlah, metode, kind);

    private static RentangLaporan Rentang(DateTime? dari = null, DateTime? sampai = null) =>
        RentangLaporan.Normalisasi(dari, sampai, Kini);

    [Fact]
    public void RentangTanpaTanggalAdalahTigaPuluhHariTerakhirTermasukHariIni()
    {
        var rentang = Rentang();
        Assert.Equal(30, rentang.JumlahHari);
        Assert.Equal(new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc), rentang.MulaiUtc);
        // Batas atas eksklusif: hari ini ikut terhitung, tapi 16 Oktober tidak.
        Assert.Equal(new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc), rentang.BatasAkhirUtc);
        Assert.True(rentang.Dalam(Kini));
        Assert.False(rentang.Dalam(new DateTime(2026, 10, 16, 0, 0, 1, DateTimeKind.Utc)));
    }

    [Fact]
    public void TanggalTerbalikDibalikkanBukanDijadikanKosong()
    {
        var rentang = Rentang(dari: new DateTime(2026, 10, 10), sampai: new DateTime(2026, 10, 5));
        Assert.Equal(new DateTime(2026, 10, 5), rentang.MulaiUtc);
        Assert.Equal(new DateTime(2026, 10, 11), rentang.BatasAkhirUtc);
        Assert.Equal(6, rentang.JumlahHari);
    }

    [Fact]
    public void RentangLebarDipotongDariSisiTuaSupayaDataTerbaruTetapUtuh()
    {
        var rentang = Rentang(dari: new DateTime(2020, 1, 1), sampai: new DateTime(2026, 10, 15));
        Assert.Equal(RentangLaporan.MaksimumHari, rentang.JumlahHari);
        Assert.Equal(new DateTime(2026, 10, 16), rentang.BatasAkhirUtc);
    }

    [Fact]
    public void EnamAngkaTidakSalingMenggantikan()
    {
        var dalam = Pesanan(Awal.AddDays(1), OrderStatus.Selesai, total: 100_000m, bayar: 60_000m, dp: 30_000m);
        var batal = Pesanan(Awal.AddDays(2), OrderStatus.Dibatalkan, total: 50_000m);
        var hasil = LaporanAngka.Hitung(Rentang(), [dalam, batal],
            [Uang(Awal.AddDays(1), 30_000m, PaymentKind.Dp), Uang(Awal.AddDays(3), 30_000m)], [], []);

        Assert.Equal(1, hasil.JumlahPesanan);
        Assert.Equal(1, hasil.JumlahDibatalkan);
        Assert.Equal(100_000m, hasil.Omzet);
        Assert.Equal(60_000m, hasil.Penerimaan);
        Assert.Equal(30_000m, hasil.Dp);
        Assert.Equal(30_000m, hasil.Pelunasan);
        Assert.Equal(0m, hasil.Refund);
        // Sisa 40.000 tetap piutang walaupun pesanan sudah selesai (belum lunas).
        Assert.Equal(40_000m, hasil.Piutang);
        Assert.Equal(1, hasil.JumlahTagihanTerbuka);
    }

    [Fact]
    public void RefundMengurangiPembayaranTetapiTetapTampilSendiri()
    {
        var pesanan = Pesanan(Awal.AddDays(1), OrderStatus.Selesai, 100_000m, bayar: 100_000m, refund: 25_000m);
        var hasil = LaporanAngka.Hitung(Rentang(), [pesanan], [Uang(Awal.AddDays(1), 100_000m)],
            [new BarisRefund(Awal.AddDays(5), 25_000m)], []);

        Assert.Equal(100_000m, hasil.Penerimaan);
        Assert.Equal(25_000m, hasil.Refund);
        Assert.Equal(25_000m, hasil.Piutang);
    }

    [Fact]
    public void PiutangAdalahPosisiTermasukTagihanSebelumRentang()
    {
        var lama = Pesanan(Awal.AddDays(-40), OrderStatus.Dikonfirmasi, 80_000m, bayar: 20_000m);
        var hasil = LaporanAngka.Hitung(Rentang(), [lama], [], [], []);

        // Tidak masuk arus (tidak ada uang masuk periode ini), tapi tetap wajib tertagih.
        Assert.Equal(0m, hasil.Omzet);
        Assert.Equal(60_000m, hasil.Piutang);
        Assert.Equal(1, hasil.JumlahTagihanTerbuka);
    }

    [Fact]
    public void PesananDibatalkanTidakMenagihApaPun()
    {
        var batal = Pesanan(Awal.AddDays(1), OrderStatus.Dibatalkan, 100_000m, bayar: 40_000m, refund: 40_000m);
        var hasil = LaporanAngka.Hitung(Rentang(), [batal], [], [], []);

        Assert.Equal(0m, hasil.Piutang);
        Assert.Equal(0, hasil.JumlahTagihanTerbuka);
    }

    [Fact]
    public void SisaTidakPernahNegatifWalaupunKelebihanBayar()
    {
        Assert.Equal(0m, LaporanAngka.Sisa(100_000m, 150_000m, 0m, 0m));
        var pesanan = Pesanan(Awal.AddDays(1), OrderStatus.Selesai, 100_000m, bayar: 150_000m);
        Assert.Equal(0m, pesanan.Sisa);
    }

    [Fact]
    public void SaldoKreditAdalahPosisiLedgerSampaiAkhirRentang()
    {
        var kredit = new[]
        {
            new BarisKredit(Awal.AddDays(-60), 50_000m),
            new BarisKredit(Awal.AddDays(1), 25_000m),
            new BarisKredit(Awal.AddDays(2), -10_000m),
            // Setelah akhir rentang: tidak boleh ikut menghitung posisi.
            new BarisKredit(Awal.AddDays(30), 9_000m),
        };

        var hasil = LaporanAngka.Hitung(Rentang(), [], [], [], kredit);
        Assert.Equal(65_000m, hasil.SaldoKreditPosisi);
        Assert.Equal(25_000m, hasil.KreditMasuk);
        Assert.Equal(10_000m, hasil.KreditDipakai);
    }

    [Fact]
    public void DeretHarianMemuatHariKosongSupayaSumbuTidakMelompat()
    {
        var pesanan = Pesanan(Awal.AddDays(3), OrderStatus.Selesai, 100_000m, bayar: 100_000m);
        var harian = LaporanAngka.Harian(Rentang(dari: Awal, sampai: Awal.AddDays(6)), [pesanan],
            [Uang(Awal.AddDays(3), 100_000m)], []);

        Assert.Equal(7, harian.Count);
        Assert.Equal(100_000m, harian.Sum(h => h.Omzet));
        Assert.Equal(100_000m, harian[3].Omzet);
        Assert.Equal(1, harian[3].JumlahPesanan);
        Assert.Equal(0m, harian[4].Omzet);
        Assert.Equal(0, harian[4].JumlahPesanan);
    }

    [Fact]
    public void KuantitasLayananMemakaiAngkaAktualDanEstimasiHanyaCadangan()
    {
        var id = Guid.NewGuid();
        var perKg = new BarisItemLayanan(id, "Cuci Setrika", ServiceUnitType.PerKg, 4.5m, null, 6m, null, 13_500m);
        Assert.Equal(4.5m, LaporanAngka.Kuantitas(perKg));
        Assert.Equal(6m, LaporanAngka.Kuantitas(perKg with { BeratAktual = null }));

        var perItem = new BarisItemLayanan(id, "Setrika", ServiceUnitType.PerItem, null, 3m, null, 2m, 9_000m);
        Assert.Equal(3m, LaporanAngka.Kuantitas(perItem));

        var paket = new BarisItemLayanan(id, "Paket Bed Cover", ServiceUnitType.PaketTetap, null, null, null, null,
            30_000m);
        Assert.Equal(1m, LaporanAngka.Kuantitas(paket));
    }

    [Fact]
    public void LayananHanyaMenghitungPesananDalamRentangDanUrutDariSubtotalTerbesar()
    {
        var dalamId = Guid.NewGuid();
        var luarId = Guid.NewGuid();
        var items = new[]
        {
            new BarisItemLayanan(dalamId, "Cuci", ServiceUnitType.PerKg, 2m, null, null, null, 20_000m),
            new BarisItemLayanan(dalamId, "Setrika", ServiceUnitType.PerItem, null, 3m, null, null, 45_000m),
            new BarisItemLayanan(luarId, "Cuci", ServiceUnitType.PerKg, 9m, null, null, null, 90_000m),
        };

        var layanan = LaporanAngka.Layanan(new HashSet<Guid> { dalamId }, items);
        Assert.Equal(new[] { "Setrika", "Cuci" }, layanan.Select(l => l.Nama));
        Assert.Equal(45_000m, layanan[0].Subtotal);
        Assert.Equal(3m, layanan[0].Kuantitas);
        Assert.Equal(20_000m, layanan[1].Subtotal);
    }

    [Fact]
    public void MetodeHanyaMenampungUangYangTerkonfirmasiDalamRentang()
    {
        var uang = new[]
        {
            Uang(Awal.AddDays(1), 30_000m, metode: PaymentMethod.Qris),
            Uang(Awal.AddDays(2), 70_000m, metode: PaymentMethod.Qris),
            Uang(Awal.AddDays(1), 20_000m, metode: PaymentMethod.Tunai),
            Uang(Awal.AddDays(400), 5_000m, metode: PaymentMethod.Tunai),
        };

        var metode = LaporanAngka.Metode(Rentang(dari: Awal, sampai: Awal.AddDays(3)), uang);
        Assert.Equal(2, metode.Count);
        Assert.Equal(PaymentMethod.Qris, metode[0].Metode);
        Assert.Equal(100_000m, metode[0].Jumlah);
        Assert.Equal(2, metode[0].JumlahTransaksi);
        Assert.Equal(20_000m, metode[1].Jumlah);
    }

    [Fact]
    public void TransaksiTerbaruDuluDanHanyaYangDibuatDalamRentang()
    {
        var tua = Pesanan(Awal.AddDays(-1), OrderStatus.Selesai, 10_000m);
        var tengah = Pesanan(Awal.AddDays(2), OrderStatus.Selesai, 20_000m);
        var baru = Pesanan(Awal.AddDays(5), OrderStatus.Dikonfirmasi, 30_000m);

        var hasil = LaporanAngka.Transaksi(Rentang(dari: Awal, sampai: Awal.AddDays(5)), [tua, baru, tengah]);
        Assert.Equal(new[] { baru.Id, tengah.Id }, hasil.Select(t => t.Id));
    }
}
