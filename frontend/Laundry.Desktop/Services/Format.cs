using System.Globalization;
using System.Text;

namespace Laundry.Desktop.Services;

/// <summary>
/// Format tampilan. Pemisah ribuan disusun manual (titik, gaya Indonesia) supaya angka di layar
/// tidak pernah berubah mengikuti region Windows pengguna maupun kultur invarian yang memakai koma.
/// </summary>
public static class Format
{
    private static readonly string[] Bulan =
        ["Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des"];

    public static string Angka(decimal nilai, int desimal = 0)
    {
        var dibulatkan = desimal == 0
            ? Math.Round(nilai, MidpointRounding.AwayFromZero)
            : Math.Round(nilai, desimal, MidpointRounding.AwayFromZero);
        var pola = desimal == 0 ? "0" : "0." + new string('0', desimal);
        var teks = Math.Abs(dibulatkan).ToString(pola, CultureInfo.InvariantCulture);

        var utuh = teks;
        var pecahan = "";
        var titik = teks.IndexOf('.');
        if (titik >= 0)
        {
            utuh = teks[..titik];
            pecahan = teks[titik..];
        }

        var sb = new StringBuilder(utuh.Length + pecahan.Length + utuh.Length / 3 + 1);
        for (var i = 0; i < utuh.Length; i++)
        {
            if (i > 0 && (utuh.Length - i) % 3 == 0) sb.Append('.');
            sb.Append(utuh[i]);
        }

        sb.Append(pecahan);
        return (dibulatkan < 0 ? "-" : "") + sb;
    }

    public static string Rupiah(decimal? nilai) => nilai is null ? "-" : "Rp" + Angka(nilai.Value);

    public static string Rupiah(decimal nilai) => "Rp" + Angka(nilai);

    /// <summary>Kuantitas (kg/item) tanpa pembulatan ke rupiah: 2,5 kg tetap "2.5".</summary>
    public static string Kuantitas(decimal? nilai) => nilai is null
        ? "-"
        : Angka(nilai.Value, 2).TrimEnd('0').TrimEnd('.');

    public static string Tanggal(object? nilai)
    {
        var waktu = Lokal(nilai);
        return waktu is null ? "-" : $"{waktu.Value.Day:00} {Bulan[waktu.Value.Month - 1]} {waktu.Value.Year}";
    }

    public static string TanggalJam(object? nilai)
    {
        var waktu = Lokal(nilai);
        return waktu is null ? "-" : $"{Tanggal(waktu)} {waktu.Value:HH:mm}";
    }

    public static string Jam(object? nilai)
    {
        var waktu = Lokal(nilai);
        return waktu is null ? "-" : $"{waktu.Value:HH:mm}";
    }

    /// <summary>Waktu relatif singkat untuk penanda "terakhir sinkron" dan daftar yang hidup.</summary>
    public static string Relatif(object? nilai)
    {
        var waktu = Lokal(nilai);
        if (waktu is null) return "belum pernah";
        var selisih = DateTime.Now - waktu.Value;
        if (selisih.TotalSeconds < 0) return TanggalJam(waktu);
        if (selisih.TotalMinutes < 1) return "baru saja";
        if (selisih.TotalHours < 1) return $"{(int)selisih.TotalMinutes} menit lalu";
        if (selisih.TotalDays < 1) return $"{(int)selisih.TotalHours} jam lalu";
        if (selisih.TotalDays < 30) return $"{(int)selisih.TotalDays} hari lalu";
        return Tanggal(waktu);
    }

    /// <summary>Enum → label manusiawi ("DitimbangDanDiverifikasi" → "Ditimbang Dan Diverifikasi").</summary>
    public static string Label(object? nilai)
    {
        if (nilai is null) return "-";
        var teks = nilai as string ?? nilai.ToString() ?? "-";
        if (teks.Length == 0) return "-";

        var sb = new StringBuilder(teks.Length + 8);
        var habisGarisBawah = false;
        for (var i = 0; i < teks.Length; i++)
        {
            var c = teks[i];
            // "belum_dikonfigurasi" terbaca "Belum Dikonfigurasi", bukan "Belumdikonfigurasi".
            if (c == '_')
            {
                if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                habisGarisBawah = true;
                continue;
            }

            var sebelum = i > 0 ? teks[i - 1] : ' ';
            if (habisGarisBawah)
            {
                sb.Append(char.ToUpperInvariant(c));
                habisGarisBawah = false;
                continue;
            }

            if (i > 0 && char.IsUpper(c) && (char.IsLower(sebelum) || (char.IsUpper(sebelum) && i + 1 < teks.Length && char.IsLower(teks[i + 1]))))
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        // Singkatan yang tidak boleh terbaca sebagai kata biasa.
        return sb.ToString()
            .Replace("Dp", "DP", StringComparison.Ordinal)
            .Replace("Qris", "QRIS", StringComparison.Ordinal);
    }

    private static DateTime? Lokal(object? nilai) => nilai switch
    {
        null => null,
        DateTime utc when utc.Kind == DateTimeKind.Utc => utc.ToLocalTime(),
        DateTime lokal when lokal.Kind == DateTimeKind.Local => lokal,
        DateTime tanpaZona => DateTime.SpecifyKind(tanpaZona, DateTimeKind.Utc).ToLocalTime(),
        _ => null,
    };
}
