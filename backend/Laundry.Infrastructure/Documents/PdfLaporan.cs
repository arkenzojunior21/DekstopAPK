using System.Text;
using Laundry.Application.Core;

namespace Laundry.Infrastructure.Documents;

/// <summary>
/// PDF tabel minim yang ditulis sendiri (PDF 1.4, font dasar Helvetica/Helvetica-Bold dengan
/// WinAnsiEncoding, satu baris per kolom, header diulang tiap halaman, dan xref berisi offset byte
/// sebenarnya). Alasannya bukan estetika: library PDF yang bebas dipakai untuk rilis proyek kampus
/// rata-rata berlisensi komunitas dengan syarat atribusi, dan berkas laporan harus bisa dibaca
/// mesin mana pun tanpa menambah dependency besar.
/// </summary>
public static class PdfLaporan
{
    private const double Margin = 40;
    private const double TinggiBaris = 13.5, UkuranIsi = 7.5, UkuranKepala = 8;
    private const double BatasBawah = 46;
    private const double LebarMinimum = 26, LebarMaksimal = 190, UkuranMinimum = 5.2;

    /// <summary>A4; versi landscape dipakai bila jumlah kolom tidak muat di portrait.</summary>
    private readonly record struct Kertas(double Lebar, double Tinggi)
    {
        public double Isi => Lebar - 2 * Margin;
    }

    private static readonly Kertas Portrait = new(595.276, 841.89);
    private static readonly Kertas Landskap = new(841.89, 595.276);

    // Lebar advance font dasar (AFM/1000) untuk 95 karakter ASCII pertama yang bisa dicetak.
    private static readonly int[] LebarReguler =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 722,
        667, 722, 722, 722, 611, 722, 667, 667, 611, 611, 278, 278, 278, 469, 556, 333,
        556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556,
        556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly int[] LebarTebal =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 722, 667, 722, 667, 667, 611, 611, 333, 333, 333, 584, 556, 333,
        556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, 611,
        611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    public static byte[] Buat(TabelLaporan tabel)
    {
        var alami = LebarAlami(tabel);
        // Tabel berkolom banyak (laporan transaksi) diputar ke landscape: nomor pesanan dan status
        // panjang tercetak utuh, bukan dipotong jadi "..".
        var kertas = alami.Sum() > Portrait.Lebar - 2 * Margin ? Landskap : Portrait;
        var kolom = LebarKolom(alami, kertas.Lebar - 2 * Margin);
        return Tulis(tabel, BagiHalaman(tabel, kertas), kolom, kertas);
    }

    private static double[] LebarAlami(TabelLaporan tabel)
    {
        var n = tabel.Kolom.Count;
        var alami = new double[n];
        for (var i = 0; i < n; i++)
        {
            var selebar = LebarTeks(tabel.Kolom[i], UkuranKepala, true);
            foreach (var baris in tabel.Baris)
                if (baris.Count > i)
                    selebar = Math.Max(selebar, Math.Min(LebarTeks(baris[i].Isi, UkuranIsi, false), LebarMaksimal));
            alami[i] = Math.Max(selebar + 8, LebarMinimum);
        }

        return alami;
    }

    private static double[] LebarKolom(double[] alami, double isiLebar)
    {
        var total = alami.Sum();
        if (total <= 0) return [];
        var hasil = alami.Select(a => a / total * isiLebar).ToArray();
        // Kolom hasil normalisasi tidak boleh jatuh di bawah minimum; kekurangan dibagi ke kolom longgar.
        var kurang = hasil.Where(w => w < LebarMinimum).Sum(w => LebarMinimum - w);
        var longgar = hasil.Where(w => w >= LebarMinimum).Sum();
        for (var i = 0; i < hasil.Length; i++)
            hasil[i] = hasil[i] < LebarMinimum
                ? LebarMinimum
                : hasil[i] + (longgar > 0 ? kurang * hasil[i] / longgar : 0);

        return hasil;
    }

    private static List<List<IReadOnlyList<Sel>>> BagiHalaman(TabelLaporan tabel, Kertas kertas)
    {
        var tinggiHeader = tabel.Kolom.Count > 0 ? TinggiBaris : 0;
        var muat = MuatBaris(kertas.Tinggi - Margin - (string.IsNullOrEmpty(tabel.Subjudul) ? 24 : 38), tinggiHeader);
        var halaman = new List<List<IReadOnlyList<Sel>>>();
        var sekarang = new List<IReadOnlyList<Sel>>();

        foreach (var baris in tabel.Baris)
        {
            if (sekarang.Count >= muat)
            {
                halaman.Add(sekarang);
                sekarang = [];
                // Halaman lanjutan hanya butuh satu baris judul, jadi ruang tabel lebih longgar.
                muat = MuatBaris(kertas.Tinggi - Margin - 26, tinggiHeader);
            }

            sekarang.Add(baris);
        }

        if (sekarang.Count > 0 || halaman.Count == 0) halaman.Add(sekarang);
        return halaman;
    }

    private static int MuatBaris(double mulaiY, double tinggiHeader) =>
        Math.Max((int)((mulaiY - tinggiHeader - 4 - BatasBawah) / TinggiBaris), 1);

    private static byte[] Tulis(TabelLaporan tabel, List<List<IReadOnlyList<Sel>>> halaman, double[] kolom,
        Kertas kertas)
    {
        var isiHalaman = halaman.Select((isi, i) => SusunHalaman(tabel, isi, kolom, i + 1, halaman.Count, kertas))
            .ToList();

        using var memori = new MemoryStream();
        var offset = new List<long>();

        void TulisTeks(string teks)
        {
            var bytes = Bytes(teks);
            memori.Write(bytes, 0, bytes.Length);
        }
        void Objek(string isi)
        {
            offset.Add(memori.Position);
            TulisTeks($"{offset.Count} 0 obj\n{isi}\nendobj\n");
        }

        // Header + baris komentar biner: wajib menurut spesifikasi PDF, dan baris biner itu menandai
        // berkas sebagai PDF untuk alat yang membaca 8 baris pertama.
        TulisTeks($"%PDF-1.4\n%{(char)0xE2}{(char)0xE3}{(char)0xCF}{(char)0xD3}\n");

        var kids = string.Join(' ', Enumerable.Range(0, halaman.Count).Select(i => $"{6 + i * 2} 0 R"));
        Objek("<< /Type /Catalog /Pages 2 0 R >>");
        Objek($"<< /Type /Pages /Kids [{kids}] /Count {halaman.Count} >>");
        Objek("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Objek("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Objek("<< /Producer (Laundry Api) /CreationDate (D:" + TanggalPdf() + ") >>");

        for (var i = 0; i < isiHalaman.Count; i++)
        {
            Objek($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(kertas.Lebar)} {N(kertas.Tinggi)}] " +
                  $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {7 + i * 2} 0 R >>");

            var bytes = Bytes(isiHalaman[i]);
            offset.Add(memori.Position);
            TulisTeks($"{offset.Count} 0 obj\n<< /Length {bytes.Length} >>\nstream\n");
            memori.Write(bytes, 0, bytes.Length);
            TulisTeks("endstream\nendobj\n");
        }

        var total = offset.Count + 1;
        var posisiXref = memori.Position;
        TulisTeks($"xref\n0 {total}\n0000000000 65535 f \n");
        foreach (var o in offset) TulisTeks($"{o:D10} 00000 n \n");
        TulisTeks($"trailer\n<< /Size {total} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{posisiXref}\n%%EOF\n");

        return memori.ToArray();
    }

    private static string SusunHalaman(TabelLaporan tabel, List<IReadOnlyList<Sel>> baris, double[] kolom,
        int nomor, int jumlah, Kertas kertas)
    {
        var isiLebar = kertas.Isi;
        var s = new StringBuilder();
        var y = kertas.Tinggi - Margin;

        Teks(s, Margin, y - 15, tabel.Judul, "F2", 13);
        y -= 22;
        if (!string.IsNullOrEmpty(tabel.Subjudul))
        {
            Teks(s, Margin, y - 10, tabel.Subjudul, "F1", 8);
            y -= 16;
        }

        if (nomor > 1) Teks(s, Margin, y - 10, $"{tabel.Judul} (lanjutan)", "F1", 8);
        Garis(s, Margin, y - 16, Margin + isiLebar, y - 16, 0.9);
        y -= 26;

        if (tabel.Kolom.Count > 0)
        {
            s.Append($"0.93 g {N(Margin)} {N(y - 3)} {N(isiLebar)} {N(TinggiBaris)} re f\n0 g\n");
            var x = Margin;
            for (var i = 0; i < tabel.Kolom.Count; i++)
            {
                TulisSel(s, x, y + 6, tabel.Kolom[i], kolom[i], "F2", UkuranKepala, false);
                x += kolom[i];
            }

            y -= TinggiBaris;
            Garis(s, Margin, y + 2, Margin + isiLebar, y + 2, 0.9);
        }

        // Laporan kosong tetap dicetak dengan kepala kolom, plus satu baris penjelasan: berkas tanpa
        // data tidak boleh terlihat seperti ekspor yang gagal.
        if (baris.Count == 0 && nomor == 1)
            Teks(s, Margin, y - 4, "Tidak ada data pada periode ini.", "F1", UkuranIsi);

        var selangSeling = false;
        foreach (var barisSel in baris)
        {
            if (selangSeling)
                s.Append($"0.975 g {N(Margin)} {N(y - 3)} {N(isiLebar)} {N(TinggiBaris)} re f\n0 g\n");
            var x = Margin;
            for (var i = 0; i < barisSel.Count && i < kolom.Length; i++)
            {
                var sel = barisSel[i];
                TulisSel(s, x, y + 6, sel.Isi, kolom[i], "F1", UkuranIsi, sel.IsAngka);
                x += kolom[i];
            }

            y -= TinggiBaris;
            selangSeling = !selangSeling;
        }

        Teks(s, Margin, BatasBawah - 16,
            $"Halaman {nomor} dari {jumlah} - {tabel.Judul} - dibuat {Timestamp()} UTC", "F1", 7);
        return s.ToString();
    }

    private static void TulisSel(StringBuilder s, double x, double y, string isi, double lebar, string font,
        double ukuran, bool rataKanan)
    {
        var tebal = font == "F2";
        var sisa = lebar - 6;
        var teks = isi;
        // Huruf dikecilkan lebih dulu: memotong nomor pesanan berarti menghapus data, huruf kecil
        // masih terbaca. Pemotongan ".." tetap ada sebagai jalan terakhir.
        while (ukuran > UkuranMinimum && LebarTeks(teks, ukuran, tebal) > sisa) ukuran *= 0.94;
        while (teks.Length > 1 && LebarTeks(teks + "..", ukuran, tebal) > sisa)
            teks = teks[..Math.Max(1, teks.Length - 2)];
        if (teks != isi) teks += "..";

        var posisi = rataKanan ? x + lebar - 3 - LebarTeks(teks, ukuran, tebal) : x + 3;
        Teks(s, posisi, y, teks, font, ukuran);
    }

    private static void Teks(StringBuilder s, double x, double y, string isi, string font, double ukuran)
    {
        s.Append($"BT /{font} {N(ukuran)} Tf 1 0 0 1 {N(x)} {N(y)} Tm (");
        s.Append(Escape(isi));
        s.Append(") Tj ET\n");
    }

    private static void Garis(StringBuilder s, double x1, double y1, double x2, double y2, double tebal)
    {
        s.Append($"{N(tebal)} w 0 0 0 RG {N(x1)} {N(y1)} m {N(x2)} {N(y2)} l S\n");
    }

    private static string Escape(string teks)
    {
        var s = new StringBuilder(teks.Length + 8);
        foreach (var karakter in teks)
        {
            var nilai = WinAnsi(karakter);
            if (karakter is '(' or ')' or '\\') s.Append('\\').Append(karakter);
            else if (nilai is >= 32 and < 127) s.Append((char)nilai);
            else s.Append('\\').Append(Convert.ToString(nilai, 8).PadLeft(3, '0'));
        }

        return s.ToString();
    }

    /// <summary>
    /// ASCII dipetakan apa adanya; garis baru harus lolos karena struktur PDF built dari teks multi-baris.
    /// En/em dash jadi tanda hubung, sisanya di atas WinAnsi jadi '?' agar berkas tetap sah.
    /// </summary>
    private static byte WinAnsi(char c) => c switch
    {
        '\n' or '\r' or '\t' => (byte)c,
        < ' ' => (byte)'?',
        <= (char)0xFF => (byte)c,
        '—' or '–' or '\u2212' => (byte)'-',
        '“' or '”' => (byte)'"',
        '‘' or '’' => (byte)'\'',
        _ => (byte)'?',
    };

    private static byte[] Bytes(string teks)
    {
        var hasil = new byte[teks.Length];
        for (var i = 0; i < teks.Length; i++) hasil[i] = WinAnsi(teks[i]);
        return hasil;
    }

    private static double LebarTeks(string teks, double ukuran, bool tebal)
    {
        var tabel = tebal ? LebarTebal : LebarReguler;
        double total = 0;
        foreach (var c in teks)
            total += c is >= (char)32 and <= (char)126 ? tabel[c - 32] : tabel['n' - 32];
        return total * ukuran / 1000.0;
    }

    private static string N(double nilai) => nilai.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Meta tanggal PDF memakai sintaks spesifikasi (D:YYYYMMDDHHmmSS), bukan format baca-manusia.</summary>
    private static string TanggalPdf() => DateTime.UtcNow.ToString("yyyyMMddHHmmss",
        System.Globalization.CultureInfo.InvariantCulture);

    private static string Timestamp() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss",
        System.Globalization.CultureInfo.InvariantCulture);
}
