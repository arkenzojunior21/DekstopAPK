using System.Globalization;
using System.Text;
using Laundry.Application.Core;

namespace Laundry.Infrastructure.Documents;

/// <summary>
/// CSV untuk alat lain (Excel, spreadsheet, import). Tata letaknya sengaja sama XLSX supaya satu
/// laporan tidak punya dua bentuk: baris 1 `sep=;`, baris 2 judul, baris 3 subjudul, baris 4 kepala
/// kolom, data mulai baris 5.
/// Angka ditulis datar dengan pemisah desimal titik (bukan "Rp1.000") supaya nilainya tidak
/// bergantung pada pengaturan regional pembaca; untuk dihitung di Excel, format yang tepat adalah XLSX.
/// </summary>
public static class CsvLaporan
{
    public static byte[] Buat(TabelLaporan tabel)
    {
        var sb = new StringBuilder();
        sb.Append("sep=;\r\n");
        sb.Append(Baris([Sitasi(tabel.Judul)])).Append("\r\n");
        sb.Append(Baris([Sitasi(tabel.Subjudul)])).Append("\r\n");
        sb.Append(Baris([.. tabel.Kolom.Select(Sitasi)])).Append("\r\n");

        foreach (var baris in tabel.Baris)
        {
            var teks = new string[tabel.Kolom.Count];
            for (var i = 0; i < teks.Length; i++)
            {
                if (i >= baris.Count)
                {
                    teks[i] = string.Empty;
                    continue;
                }

                var sel = baris[i];
                teks[i] = sel.IsAngka && sel.Angka is { } angka
                    ? AngkaDatar(angka)
                    : Sitasi(sel.Isi);
            }

            sb.Append(Baris(teks)).Append("\r\n");
        }

        // BOM: tanpanya Excel membaca berkas sebagai ANSI dan karakter non-ASCII menjadi rusak.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Baris(IReadOnlyList<string> kolom) => string.Join(';', kolom);

    /// <summary>
    /// Tanda kutip hanya untuk teks. Angka dibiarkan telanjang agar Excel tetap mengenalinya sebagai
    /// angka; awalan =, +, -, @ pada teks diberi kutip supaya tidak dieksekusi sebagai rumus.
    /// </summary>
    private static string Sitasi(string teks)
    {
        if (string.IsNullOrEmpty(teks)) return string.Empty;
        var perlu = teks.Any(c => c is ';' or '"' or '\r' or '\n')
            || teks.StartsWith(' ') || teks.EndsWith(' ')
            || teks[0] is '=' or '+' or '-' or '@';
        var isi = teks.Replace("\"", "\"\"");
        return perlu ? $"\"{isi}\"" : isi;
    }

    private static string AngkaDatar(decimal nilai) => nilai == Math.Truncate(nilai)
        ? nilai.ToString("0", CultureInfo.InvariantCulture)
        : nilai.ToString("0.00####", CultureInfo.InvariantCulture);
}
