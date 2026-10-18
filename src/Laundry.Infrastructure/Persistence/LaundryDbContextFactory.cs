using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Laundry.Infrastructure.Persistence;

public class LaundryDbContextFactory : IDesignTimeDbContextFactory<LaundryDbContext>
{
    public LaundryDbContext CreateDbContext(string[] args)
    {
        var conn = EnvironmentConfig.GetKoneksi("CONN_ADMIN",
            "Host=localhost;Port=5433;Database=laundry_admin;Username=laundry;Password=laundry_dev_2026");
        var options = new DbContextOptionsBuilder<LaundryDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new LaundryDbContext(options);
    }
}

/// <summary>
/// Factory sisi cloud. Model dan migrationnya identik dengan <see cref="LaundryDbContext"/>,
/// jadi skema database cloud dibangun dengan konteks migrasi yang sama dan hanya connection
/// string-nya yang diganti (lihat scripts/migrate.ps1). Sengaja tidak ada nilai bawaan di kode:
/// database cloud ada di mesin orang lain, jadi kalau CONN_CLOUD kosong lebih baik berhenti
/// dengan pesan jelas daripada membuat skema di tempat yang salah.
/// </summary>
public class CloudDbContextFactory : IDesignTimeDbContextFactory<CloudDbContext>
{
    public CloudDbContext CreateDbContext(string[] args)
    {
        var conn = EnvironmentConfig.GetKoneksi("CONN_CLOUD");
        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException(
                "CONN_CLOUD belum diisi di environment atau berkas .env — diperlukan untuk membangun skema database cloud.");
        var options = new DbContextOptionsBuilder<CloudDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new CloudDbContext(options);
    }
}

public static class EnvironmentConfig
{
    private static Dictionary<string, string>? _file;

    /// <summary>Urutan: variabel lingkungan proses > isi berkas .env > nilai default.</summary>
    public static string Get(string key, string fallback = "")
    {
        var fromEnv = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
        _file ??= LoadDotEnv();
        return _file.TryGetValue(key, out var v) ? v : fallback;
    }

    /// <summary>Apakah kunci diisi di lingkungan/.env — dipakai laporan konfigurasi tanpa membocorkan nilai.</summary>
    public static bool Terisi(string key) => !string.IsNullOrWhiteSpace(Get(key));

    /// <summary>
    /// Connection string dalam bentuk yang pasti bisa dibaca Npgsql.
    /// Database cloud (Neon, Supabase, RDS) menyerahkan URI seperti
    /// <c>postgresql://pengguna:sandi@host:5432/basisdata?sslmode=require</c>, sedangkan Npgsql
    /// hanya mau bentuk kunci=nilai — URI itu dilempar apa adanya menghasilkan ArgumentException
    /// yang isinya seluruh URI. Jadi URI dikonversi di sini, sekali untuk semua pemanggil.
    /// Nilai kunci=nilai (database dev lokal) lewat tanpa diubah.
    /// </summary>
    public static string GetKoneksi(string key, string fallback = "") => DariUri(Get(key, fallback));

    /// <summary>Konversi URI postgres → connection string Npgsql. Bentuk lain dikembalikan apa adanya.</summary>
    public static string DariUri(string nilai)
    {
        if (string.IsNullOrWhiteSpace(nilai)) return nilai;
        if (!nilai.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !nilai.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)) return nilai;
        if (!Uri.TryCreate(nilai.Trim(), UriKind.Absolute, out var uri)) return nilai;

        var bagian = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.DnsSafeHost)) bagian.Add($"Host={uri.DnsSafeHost}");
        if (uri.Port > 0) bagian.Add($"Port={uri.Port}");

        var basisData = uri.AbsolutePath.Trim('/');
        if (basisData.Length > 0) bagian.Add($"Database={Tulis(LepasKode(basisData))}");

        var (pengguna, sandi) = BagiPengguna(uri.UserInfo);
        if (pengguna.Length > 0) bagian.Add($"Username={Tulis(pengguna)}");
        if (sandi.Length > 0) bagian.Add($"Password={Tulis(sandi)}");

        foreach (var pasangan in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var tanda = pasangan.IndexOf('=');
            if (tanda <= 0) continue;
            var nama = pasangan[..tanda].Trim();
            bagian.Add($"{EjaanNpgsql(nama)}={Tulis(LepasKode(pasangan[(tanda + 1)..]))}");
        }

        return string.Join(';', bagian);
    }

    /// <summary>
    /// Sebagian parameter URI punya ejaan sendiri di connection string Npgsql (sudah diverifikasi
    /// lewat test): sslmode dan options sudah dikenali apa adanya, dua lainnya tidak.
    /// Parameter yang tidak dikenal diteruskan apa adanya supaya Npgsql menolak dengan pesan
    /// yang terlihat, bukan menghilangkannya diam-diam.
    /// </summary>
    private static string EjaanNpgsql(string namaParam) => namaParam.ToLowerInvariant() switch
    {
        "channel_binding" => "Channel Binding",
        "connect_timeout" => "Timeout",
        _ => namaParam,
    };

    private static (string Pengguna, string Sandi) BagiPengguna(string info)
    {
        if (string.IsNullOrEmpty(info)) return (string.Empty, string.Empty);
        var tanda = info.IndexOf(':');
        return tanda < 0
            ? (LepasKode(info), string.Empty)
            : (LepasKode(info[..tanda]), LepasKode(info[(tanda + 1)..]));
    }

    private static string LepasKode(string bernilai) => Uri.UnescapeDataString(bernilai);

    /// <summary>Nilai yang mengandung pemisah connection string perlu dikurung kutip.</summary>
    private static string Tulis(string nilai) =>
        nilai.Contains(';') || nilai.Contains('=') ? $"'{nilai.Replace("'", "''")}'" : nilai;

    private static Dictionary<string, string> LoadDotEnv()
    {
        var result = new Dictionary<string, string>();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, ".env");
            if (File.Exists(file))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#')) continue;
                    var ix = t.IndexOf('=');
                    if (ix <= 0) continue;
                    result[t[..ix].Trim()] = t[(ix + 1)..].Trim();
                }
                return result;
            }
            dir = dir.Parent;
        }
        return result;
    }
}
