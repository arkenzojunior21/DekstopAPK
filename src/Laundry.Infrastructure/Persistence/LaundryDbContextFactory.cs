using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Laundry.Infrastructure.Persistence;

public class LaundryDbContextFactory : IDesignTimeDbContextFactory<LaundryDbContext>
{
    public LaundryDbContext CreateDbContext(string[] args)
    {
        var conn = EnvironmentConfig.Get("CONN_ADMIN",
            "Host=localhost;Port=5433;Database=laundry_admin;Username=laundry;Password=laundry_dev_2026");
        var options = new DbContextOptionsBuilder<LaundryDbContext>()
            .UseNpgsql(conn)
            .Options;
        return new LaundryDbContext(options);
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
