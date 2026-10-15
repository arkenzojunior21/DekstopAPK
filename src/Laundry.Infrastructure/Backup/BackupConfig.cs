using Laundry.Infrastructure.Persistence;

namespace Laundry.Infrastructure.Backup;

/// <summary>Lokasi binari PostgreSQL dan folder hasil backup; semuanya dari environment.</summary>
public sealed record BackupConfig(string PathBin, string FolderBackup, int TimeoutDetik)
{
    public string PgDump => Path.Combine(PathBin, NamaTool("pg_dump"));
    public string PgRestore => Path.Combine(PathBin, NamaTool("pg_restore"));

    public static BackupConfig DariLingkungan()
    {
        var bin = EnvironmentConfig.Get("PG_BIN",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "pgsql17", "pgsql", "bin"));
        var folder = EnvironmentConfig.Get("BACKUP_DIR",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "laundry-pg", "backups"));
        return new BackupConfig(bin, folder,
            Math.Clamp(int.TryParse(EnvironmentConfig.Get("BACKUP_TIMEOUT_DETIK"), out var t) ? t : 300, 5, 3600));
    }

    private static string NamaTool(string nama) => OperatingSystem.IsWindows() ? $"{nama}.exe" : nama;

    /// <summary>Dipakai untuk menampilkan keadaan sistem: tool ada atau tidak, tanpa menebak lokasi lain.</summary>
    public bool PgDumpTersedia => File.Exists(PgDump);
}
