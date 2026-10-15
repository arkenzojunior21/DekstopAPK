using System.Globalization;
using System.Text;
using Laundry.Application.Core;

namespace Laundry.Infrastructure.Documents;

/// <summary>Berkas hasil ekspor beserta nama dan tipe MIME yang dipakai header Content-Disposition.</summary>
public sealed record HasilEkspor(string NamaBerkas, string TipeMime, byte[] Isi);

/// <summary>
/// Satu pintu ekspor: jenis laporan apa pun lewat `TabelLaporan` yang sama, jadi PDF, XLSX, dan CSV
/// selalu berisi kolom dan angka yang identik dengan yang dilihat di layar.
/// </summary>
public static class EksporDokumen
{
    public static HasilEkspor Buat(TabelLaporan tabel, FormatEkspor format, DateTime dibuatUtc) => format switch
    {
        FormatEkspor.Pdf => Baru(tabel, format, "application/pdf", dibuatUtc, PdfLaporan.Buat(tabel)),
        FormatEkspor.Xlsx => Baru(tabel, format,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", dibuatUtc, ExcelLaporan.Buat(tabel)),
        _ => Baru(tabel, format, "text/csv; charset=utf-8", dibuatUtc, CsvLaporan.Buat(tabel)),
    };

    private static HasilEkspor Baru(TabelLaporan tabel, FormatEkspor format, string mime, DateTime dibuatUtc,
        byte[] isi) => new(NamaBerkas(tabel.Judul, dibuatUtc, Ektensi(format)), mime, isi);

    private static string Ektensi(FormatEkspor format) => format switch
    {
        FormatEkspor.Pdf => "pdf",
        FormatEkspor.Xlsx => "xlsx",
        _ => "csv",
    };

    /// <summary>Hanya huruf/angka/tanda hubung: nama berkas masuk header HTTP, jadi tidak boleh memuat karakter liar.</summary>
    private static string NamaBerkas(string judul, DateTime dibuatUtc, string ektensi)
    {
        var kata = Rapikan(new string(judul
            .Select(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                ? char.ToLowerInvariant(c) : '-')
            .ToArray()));
        if (kata.Length == 0) kata = "laporan";
        if (kata.Length > 48) kata = kata[..48].Trim('-');
        return $"{kata}-{dibuatUtc.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.{ektensi}";
    }

    /// <summary>Lipat tanda hubung ganda agar "Laporan per layanan" menjadi "laporan-per-layanan".</summary>
    private static string Rapikan(string teks)
    {
        var s = new StringBuilder(teks.Length);
        foreach (var c in teks)
        {
            if (c != '-') s.Append(c);
            else if (s.Length > 0 && s[^1] != '-') s.Append('-');
        }

        return s.ToString().Trim('-');
    }
}
