using System.Text;
using ClosedXML.Excel;
using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Documents;

namespace Laundry.Tests.Unit;

/// <summary>
/// Mesin ekspor fase 9 diuji sampai ke bentuk bytes-nya. PDF ditulis tangan, jadi struktur xref
/// harus dibuktikan: offset yang meleset satu byte saja membuat berkas tidak bisa dibuka viewer,
/// dan itu tidak akan ketahuan kalau tes cuma cek "tidak melempar exception".
/// </summary>
public class ReportDocumentTests
{
    private static readonly DateTime Dibuat = new(2026, 10, 16, 8, 15, 0, DateTimeKind.Utc);

    private static TabelLaporan Tabel(int jumlahBaris = 0)
    {
        var baris = new List<IReadOnlyList<Sel>>();
        for (var i = 0; i < jumlahBaris; i++)
            baris.Add(new[] { Sel.Teks($"LDR-{i:000}"), Sel.Teks("Budi"), Sel.Uang(1000 * (i + 1)),
                Sel.Qty(2.5m + i) });

        if (jumlahBaris == 0)
        {
            baris.Add(new[] { Sel.Teks("LDR-20261001-01"), Sel.Teks("Andi (uji)"), Sel.Uang(1_500_000m), Sel.Qty(4.5m) });
            baris.Add(new[] { Sel.Teks("LDR-20261002-01"), Sel.Teks("Sari; Budi"), Sel.Uang(-25_000m), Sel.Qty(0m) });
        }

        return TabelLaporan.Buat("Laporan per layanan", "Periode 01 Okt 2026 - 15 Okt 2026 (UTC), dibuat 2026-10-16 08:15 UTC",
            [("Nomor", false), ("Pelanggan", false), ("Sisa", true), ("Kuantitas", true)], baris);
    }

    private static string Latin1(byte[] isi) => Encoding.Latin1.GetString(isi);

    /// <summary>Cek header, penutup, dan bahwa setiap entri xref menunjuk ke objek yang benar.</summary>
    private static void PastikanStrukturPdf(byte[] pdf)
    {
        var teks = Latin1(pdf);
        Assert.StartsWith("%PDF-1.4\n", teks);
        Assert.EndsWith("%%EOF\n", teks);
        Assert.Contains("trailer", teks);

        var mulai = teks.LastIndexOf("startxref", StringComparison.Ordinal);
        Assert.True(mulai > 0);
        var posisiXref = int.Parse(teks[(mulai + "startxref".Length)..].TrimStart('\n', '\r', ' ').Split('\n')[0]);
        Assert.Equal("xref", teks.Substring(posisiXref, 4));

        var kepala = teks[(posisiXref + 5)..].Split('\n')[0].Split(' ');
        var jumlahObjek = int.Parse(kepala[1]) - 1;
        // +20 melewati entri pertama (objek 0, selalu "f free") sebelum menghitung entri objek 1..N.
        var entri = posisiXref + 5 + $"{kepala[0]} {kepala[1]}\n".Length + 20;
        for (var i = 1; i <= jumlahObjek; i++)
        {
            // Tiap baris xref tepat 20 byte: "ooooooooo ggggg n \n".
            var offset = int.Parse(teks.Substring(entri + ((i - 1) * 20), 10));
            Assert.StartsWith($"{i} 0 obj", teks.Substring(offset, Math.Min(12, teks.Length - offset)));
        }
    }

    [Fact]
    public void PdfPunyaXrefYangMenujuObjekBenaran()
    {
        var pdf = PdfLaporan.Buat(Tabel());
        PastikanStrukturPdf(pdf);

        var teks = Latin1(pdf);
        Assert.Contains("/Type /Catalog", teks);
        Assert.Contains("/BaseFont /Helvetica", teks);
        Assert.Contains("Laporan per layanan", teks);
        Assert.Contains("LDR-20261001-01", teks);
        // Nilai uang tampil versi bacanya (sama dengan layar), bukan angka mentah.
        Assert.Contains("Rp1.500.000", teks);
    }

    [Fact]
    public void TabelLebarDiputarKeLanskapTanpaMemotongNomor()
    {
        // Sepuluh kolom seperti laporan transaksi: muat portrait tidak akan pernah cukup.
        var kolom = Enumerable.Range(0, 10).Select(i => ($"Kolom{i}", false)).ToList();
        var baris = new[]
        {
            (IReadOnlyList<Sel>)new[]
            {
                Sel.Teks("LDR-20261016-0007"), Sel.Teks("DitimbangDanDiverifikasi"),
                Sel.Teks("Pelanggan Panjang Namanya"), Sel.Uang(12_345_678m), Sel.Teks("x"), Sel.Teks("y"),
                Sel.Teks("z"), Sel.Teks("p"), Sel.Teks("q"), Sel.Teks("r"),
            },
        };

        var pdf = Latin1(PdfLaporan.Buat(TabelLaporan.Buat("Laporan transaksi", "periode", kolom, baris)));
        Assert.Contains("[0 0 841.89 595.28]", pdf);
        Assert.Contains("LDR-20261016-0007", pdf);
        Assert.Contains("DitimbangDanDiverifikasi", pdf);
        Assert.Contains("Pelanggan Panjang Namanya", pdf);
    }

    [Fact]
    public void TabelKosongTetapMenghasilkanPdfYangSah()
    {
        var kosong = TabelLaporan.Buat("Piutang", "Periode kosong",
            [("Nomor", false), ("Sisa", true)], []);
        PastikanStrukturPdf(PdfLaporan.Buat(kosong));
    }

    [Fact]
    public void PdfPecahHalamanDanMenomoriHalaman()
    {
        var penuh = TabelLaporan.Buat("Laporan transaksi", "Periode panjang",
            [("Nomor", false), ("Pelanggan", false), ("Sisa", true)],
            Enumerable.Range(0, 400).Select(i => (IReadOnlyList<Sel>)new[]
            {
                Sel.Teks($"LDR-{i:0000}"), Sel.Teks("Budi"), Sel.Uang(i),
            }));

        var teks = Latin1(PdfLaporan.Buat(penuh));
        var halaman = teks.Split("/Type /Page /Parent").Length - 1;
        Assert.True(halaman > 1, $"harus lebih dari satu halaman, dapat {halaman}");
        Assert.Contains($"/Count {halaman}", teks);
        // Tanda kurung di dalam teks PDF selalu di-escape, jadi cari kata di antaranya saja.
        Assert.Contains("lanjutan", teks);
        Assert.Contains("Halaman 1 dari", teks);
        Assert.Contains($"Halaman {halaman} dari {halaman}", teks);
    }

    [Fact]
    public void TandaKutipDanKurungTidakMerusakPdf()
    {
        var tabel = TabelLaporan.Buat("Uji (karakter)", "subtitle",
            [("Isi", false)],
            [new[] { Sel.Teks("budi (aji) \\ 100% é") }]);

        var pdf = PdfLaporan.Buat(tabel);
        PastikanStrukturPdf(pdf);
        var teks = Latin1(pdf);
        Assert.Contains(@"budi \(aji\) \\ 100", teks);
        Assert.Contains(@"\351", teks); // 'é' sebagai oktal WinAnsi, bukan byte lepas di dalam string.
    }

    [Fact]
    public void XlsxMenyimpanAngkaSebagaiAngkaBukanTeks()
    {
        var bytes = ExcelLaporan.Buat(Tabel());
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var lembar = workbook.Worksheets.First();

        Assert.Equal("Laporan per layanan", lembar.Cell(1, 1).GetString());
        Assert.Equal("Nomor", lembar.Cell(4, 1).GetString());
        Assert.Equal("Sisa", lembar.Cell(4, 3).GetString());
        Assert.Equal("LDR-20261001-01", lembar.Cell(5, 1).GetString());

        var uang = lembar.Cell(5, 3);
        Assert.Equal(1_500_000m, uang.GetValue<decimal>());
        Assert.Equal("\"Rp\"#,##0", uang.Style.NumberFormat.Format);

        var kuantitas = lembar.Cell(5, 4);
        Assert.Equal(4.5m, kuantitas.GetValue<decimal>());
        Assert.Equal("#,##0.00", kuantitas.Style.NumberFormat.Format);

        // Refund/negatif tetap angka negatif, bukan teks "-Rp25.000".
        Assert.Equal(-25_000m, lembar.Cell(6, 3).GetValue<decimal>());
        Assert.Equal(0m, lembar.Cell(6, 4).GetValue<decimal>());
    }

    [Fact]
    public void XlsxNamaLembarTidakMelebihiBatasExcel()
    {
        var panjang = TabelLaporan.Buat(new string('A', 80), "sub", [("Kolom", false)],
            [new[] { Sel.Teks("isi") }]);
        using var workbook = new XLWorkbook(new MemoryStream(ExcelLaporan.Buat(panjang)));
        Assert.Equal(31, workbook.Worksheets.First().Name.Length);
    }

    [Fact]
    public void CsvMengikutiBarisXlsxDanMenulisAngkaDatar()
    {
        var bytes = CsvLaporan.Buat(Tabel());
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);

        var baris = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n");
        Assert.Equal("sep=;", baris[0]);
        Assert.Equal("Laporan per layanan", baris[1]);
        Assert.Equal("Periode 01 Okt 2026 - 15 Okt 2026 (UTC), dibuat 2026-10-16 08:15 UTC", baris[2]);
        Assert.Equal("Nomor;Pelanggan;Sisa;Kuantitas", baris[3]);
        // Kurung dan koma biasa tidak perlu dikutip; yang butuh kutip hanya pemisah, kutip, dan baris baru.
        Assert.Equal("LDR-20261001-01;Andi (uji);1500000;4.50", baris[4]);
        Assert.Equal("LDR-20261002-01;\"Sari; Budi\";-25000;0", baris[5]);
        Assert.Equal(string.Empty, baris[6]);
    }

    [Fact]
    public void CsvMenjagaTeksYangMiripRumus()
    {
        var tabel = TabelLaporan.Buat("Uji", "sub",
            [("Rumus", false), ("TandaMinus", false)],
            [new[] { Sel.Teks("=SUM(A1)"), Sel.Teks("-10") }]);
        var pdf = Encoding.UTF8.GetString(CsvLaporan.Buat(tabel));
        Assert.Contains("\"=SUM(A1)\"", pdf);
        // Teks yang kebetulan dimulai dengan tanda minus juga dikutip; angka sungguhan tidak.
        Assert.Contains("\"-10\"", pdf);
    }

    [Fact]
    public void EksporMemberiNamaBerkasAmanDanMimeType()
    {
        var tabel = Tabel();
        var pdf = EksporDokumen.Buat(tabel, FormatEkspor.Pdf, Dibuat);
        var xlsx = EksporDokumen.Buat(tabel, FormatEkspor.Xlsx, Dibuat);
        var csv = EksporDokumen.Buat(tabel, FormatEkspor.Csv, Dibuat);

        Assert.Equal("laporan-per-layanan-20261016-0815.pdf", pdf.NamaBerkas);
        Assert.Equal("application/pdf", pdf.TipeMime);
        Assert.Equal("laporan-per-layanan-20261016-0815.xlsx", xlsx.NamaBerkas);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.TipeMime);
        Assert.Equal("laporan-per-layanan-20261016-0815.csv", csv.NamaBerkas);
        Assert.Equal("text/csv; charset=utf-8", csv.TipeMime);
        Assert.True(pdf.Isi.Length > 500);
    }

    [Fact]
    public void JudulAnehTidakBocorKeNamaBerkas()
    {
        var tabel = TabelLaporan.Buat("Laporan ../../etc/passwd?format=pdf", "sub",
            [("Kolom", false)], [new[] { Sel.Teks("isi") }]);
        var nama = EksporDokumen.Buat(tabel, FormatEkspor.Csv, Dibuat).NamaBerkas;

        Assert.DoesNotContain("..", nama);
        Assert.DoesNotContain("/", nama);
        Assert.DoesNotContain("?", nama);
        Assert.EndsWith("-20261016-0815.csv", nama);
    }

    [Fact]
    public void SatuTabelTigaFormatJumlahBarisnyaIdentik()
    {
        var tabel = Tabel(jumlahBaris: 40);
        using var xlsxWorkbook = new XLWorkbook(new MemoryStream(ExcelLaporan.Buat(tabel)));
        var csvBytes = CsvLaporan.Buat(tabel);
        var csv = Encoding.UTF8.GetString(csvBytes, 3, csvBytes.Length - 3).Split("\r\n");
        var pdf = Latin1(PdfLaporan.Buat(tabel));

        // XLSX: judul + subjudul + baris kosong + kepala + 40 data.
        Assert.Equal(4 + 40, xlsxWorkbook.Worksheets.First().LastRowUsed()!.RowNumber());
        Assert.Equal(4 + 40 + 1, csv.Length);
        // PDF: baris pertama dan terakhir harus benar-benar tertulis, termasuk saat berpindah halaman.
        Assert.Contains("LDR-000", pdf);
        Assert.Contains("LDR-039", pdf);
        PastikanStrukturPdf(PdfLaporan.Buat(tabel));
    }
}
