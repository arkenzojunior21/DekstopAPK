using System.Diagnostics;
using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Laundry.Infrastructure.Backup;

/// <summary>
/// Backup database pakai pg_dump format custom, lalu diverifikasi dengan pg_restore --list.
/// Dua aturan yang tidak bisa ditawar: hasil gagal dicatat gagal (bukan sukses setengah jalan),
/// dan berkas lama hanya dihapus setelah terverifikasi — sisanya selalu disimpan.
/// Password dikirim lewat environment proses anak, tidak pernah lewat daftar argumen.
/// </summary>
public sealed class BackupService(
    LaundryDbContext db,
    SettingsStore settings,
    BackupConfig config,
    ILogger<BackupService> log) : IBackupService
{
    public const string KeyRetensi = "backup_retensi_hari";
    public const string KeyHarian = "backup_harian";
    public const int RetensiDefault = 7;
    private static readonly TimeSpan ToleransiJam = TimeSpan.FromHours(2);

    public static IEnumerable<string> Target => new[] { "admin", "cloud" };

    public async Task<BackupRunResult> JalankanAsync(string target, CancellationToken ct = default)
    {
        var nama = Target.Contains(target) ? target : throw new ArgumentException($"Target backup tidak dikenal: {target}");
        var dimulai = DateTime.UtcNow;
        var row = new BackupHistory
        {
            Target = nama,
            FilePath = string.Empty,
            CreatedAtUtc = dimulai,
            IsSuccess = false,
            IsVerified = false,
        };

        string connKey = nama == "admin" ? "CONN_ADMIN" : "CONN_CLOUD";
        var (exit, _, err, gagal) = await CobaDumpAsync(nama, connKey, row, ct);
        if (gagal is not null)
        {
            row.Error = gagal;
            db.BackupHistories.Add(row);
            await db.SaveChangesAsync(ct);
            log.LogError("Backup {Target} gagal: {Error}", nama, gagal);
            return new BackupRunResult(nama, false, row.Id, null, 0, false, gagal,
                (int)(DateTime.UtcNow - dimulai).TotalSeconds);
        }

        var terverifikasi = await VerifikasiFileAsync(row.FilePath, ct);
        row.IsSuccess = exit == 0;
        row.IsVerified = terverifikasi.Sah;
        row.Error = exit == 0 ? terverifikasi.Error : Ringkas(err);
        db.BackupHistories.Add(row);
        await db.SaveChangesAsync(ct);

        return new BackupRunResult(nama, row.IsSuccess, row.Id, Path.GetFileName(row.FilePath),
            Ukuran(row.FilePath), row.IsVerified, row.Error,
            (int)(DateTime.UtcNow - dimulai).TotalSeconds);
    }

    private async Task<(int Exit, string Out, string Err, string? Gagal)> CobaDumpAsync(string nama,
        string connKey, BackupHistory row, CancellationToken ct)
    {
        if (!File.Exists(config.PgDump))
            return (-1, "", "", $"pg_dump tidak ditemukan di {config.PathBin} (atur PG_BIN di .env).");

        var csb = BangunKoneksi(connKey, out string? rusak);
        if (rusak is not null) return (-1, "", "", rusak);

        Directory.CreateDirectory(config.FolderBackup);
        var path = Jalur(nama, DateTime.UtcNow);
        row.FilePath = path;

        try
        {
            var (exit, stdout, stderr) = await JalankanAsync(config.PgDump,
                ["-h", csb!.Host!, "-p", csb.Port.ToString(), "-U", csb.Username!, "-d", csb.Database!,
                 "-F", "c", "-w", "-f", path],
                csb.Password, ct);
            return (exit, stdout, stderr, exit == 0 && File.Exists(path) && new FileInfo(path).Length > 0
                ? null
                : $"pg_dump tidak menghasilkan berkas (keluar dengan kode {exit}).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (-1, "", "", Ringkas(ex.Message));
        }
    }

    public async Task<BackupVerifyResult> VerifikasiAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.BackupHistories.FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new KeyNotFoundException("Riwayat backup tidak ditemukan.");
        var hasil = await VerifikasiFileAsync(JalurAbsolut(row.FilePath), ct);
        if (hasil.Sah != row.IsVerified)
        {
            row.IsVerified = hasil.Sah;
            await db.SaveChangesAsync(ct);
        }

        return new BackupVerifyResult(id, hasil.Sah, hasil.JumlahObjek, hasil.Error);
    }

    public async Task<BackupCleanupResult> BersihkanAsync(CancellationToken ct = default)
    {
        var hari = await RetensiHariAsync(ct);
        var semua = await DaftarAsync(ct);
        var layak = RetensiBackup.YangBolehDihapus(semua, DateTime.UtcNow, hari);

        var dihapus = new List<string>();
        var ditolak = new List<string>();
        foreach (var b in layak)
        {
            ct.ThrowIfCancellationRequested();
            var hasil = await VerifikasiFileAsync(JalurAbsolut(b.NamaFile), ct);
            if (!hasil.Sah)
            {
                ditolak.Add($"{b.NamaFile}: tidak lolos verifikasi ({hasil.Error ?? "arsip tidak terbaca"})");
                var row = await db.BackupHistories.FirstOrDefaultAsync(x => x.Id == b.Id, ct);
                if (row is not null)
                {
                    row.IsVerified = false;
                    row.Error = hasil.Error ?? "Berkas tidak bisa dibaca saat retensi.";
                    await db.SaveChangesAsync(ct);
                }

                continue;
            }

            try
            {
                File.Delete(JalurAbsolut(b.NamaFile));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ditolak.Add($"{b.NamaFile}: berkas tidak bisa dihapus ({Ringkas(ex.Message)})");
                continue;
            }

            var simpanan = await db.BackupHistories.FirstOrDefaultAsync(x => x.Id == b.Id, ct);
            if (simpanan is not null) db.BackupHistories.Remove(simpanan);
            await db.SaveChangesAsync(ct);
            dihapus.Add(b.NamaFile);
        }

        if (dihapus.Count > 0)
            log.LogInformation("Retensi {Hari} hari menghapus {Jumlah} berkas backup: {File}",
                hari, dihapus.Count, string.Join(", ", dihapus));
        return new BackupCleanupResult(dihapus.Count, dihapus, ditolak);
    }

    public async Task<BackupStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var berkas = await DaftarAsync(ct);
        var terakhir = berkas.Where(b => b.Sukses && b.Terverifikasi)
            .Select(b => (DateTime?)b.DibuatUtc).Max();
        var auto = await settings.GetBoolAsync(KeyHarian, true, ct);
        return new BackupStatusDto(config.FolderBackup,
            File.Exists(config.PgDump) ? config.PgDump : null,
            await RetensiHariAsync(ct), auto, terakhir, berkas);
    }

    public Task<int> RetensiHariAsync(CancellationToken ct) =>
        settings.GetIntAsync(KeyRetensi, RetensiDefault, ct);

    public Task AturRetensiAsync(int hari, CancellationToken ct) =>
        settings.SetDecimalAsync(KeyRetensi, Math.Clamp(hari, 1, 90), ct);

    public Task AturHarianAsync(bool aktif, CancellationToken ct) =>
        settings.SetStringAsync(KeyHarian, aktif ? "1" : "0", ct);

    /// <summary>
    /// Daftar isi folder backup, bukan hanya riwayat di database: berkas yang hilang di luar aplikasi
    /// tetap muncul supaya tidak ada backup yang dianggap ada padahal tidak.
    /// </summary>
    public async Task<IReadOnlyList<BackupInfo>> DaftarAsync(CancellationToken ct = default)
    {
        var riwayat = await db.BackupHistories.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc).Take(500).ToListAsync(ct);
        var hasil = riwayat.Select(b =>
        {
            var path = JalurAbsolut(Path.GetFileName(b.FilePath));
            var ada = !string.IsNullOrEmpty(b.FilePath) && File.Exists(path);
            return new BackupInfo(b.Id, b.Target, Path.GetFileName(b.FilePath),
                ada ? Ukuran(path) : 0, b.CreatedAtUtc, b.IsSuccess && ada,
                b.IsVerified && ada,
                ada ? b.Error : (string.IsNullOrEmpty(b.FilePath) ? b.Error : "Berkas tidak ada di folder backup."));
        }).ToList();

        var tercatat = riwayat.Select(b => Path.GetFileName(b.FilePath)).ToHashSet();
        if (Directory.Exists(config.FolderBackup))
        {
            foreach (var file in new DirectoryInfo(config.FolderBackup).GetFiles("*.dump")
                         .Where(f => !tercatat.Contains(f.Name)))
            {
                // Berkas di luar riwayat tidak pernah dihapus otomatis dan tidak dianggap sudah
                // diverifikasi — penandaannya dibuat terlihat, bukan ditebak.
                hasil.Add(new BackupInfo(Guid.Empty, TargetDari(file.Name), file.Name, file.Length,
                    file.LastWriteTimeUtc, true, false,
                    "Berkas tidak tercatat di riwayat; backup berikutnya tidak akan menghapusnya."));
            }
        }

        return [.. hasil.OrderByDescending(b => b.DibuatUtc)];
    }

    private static string TargetDari(string namaFile) =>
        namaFile.StartsWith("cloud", StringComparison.OrdinalIgnoreCase) ? "cloud" : "admin";

    // Jalur absolut sejak awal: pg_dump anak bekerja dengan direktori proses, jadi nama berkas
    // polos akan menulis arsip ke folder berjalan, bukan ke folder backup yang dilaporkan.
    // Penanda unik di akhir nama: dua dump dalam detik yang sama tidak boleh saling menimpa,
    // karena riwayat keduanya tetap tercatat dan akan menunjuk ke berkas yang sama.
    private string Jalur(string target, DateTime kiniUtc) =>
        Path.Combine(config.FolderBackup,
            $"{target}-{kiniUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.dump");

    private string JalurAbsolut(string namaFile) =>
        Path.IsPathRooted(namaFile) ? namaFile : Path.Combine(config.FolderBackup, namaFile);

    private static long Ukuran(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    private static NpgsqlConnectionStringBuilder? BangunKoneksi(string connKey, out string? error)
    {
        error = null;
        NpgsqlConnectionStringBuilder csb;
        try
        {
            csb = new NpgsqlConnectionStringBuilder(EnvironmentConfig.Get(connKey));
        }
        catch (ArgumentException ex)
        {
            error = $"Connection string {connKey} tidak terbaca: {Ringkas(ex.Message)}";
            return null;
        }

        if (string.IsNullOrWhiteSpace(csb.Host) || string.IsNullOrWhiteSpace(csb.Database))
        {
            error = $"Connection string {connKey} belum lengkap (host dan database wajib ada).";
            return null;
        }

        if (string.IsNullOrWhiteSpace(csb.Username))
        {
            error = $"Connection string {connKey} tidak punya nama pengguna; pg_dump tidak bisa jalan.";
            return null;
        }

        return csb;
    }

    private async Task<(bool Sah, int JumlahObjek, string? Error)> VerifikasiFileAsync(string path,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return (false, 0, "Berkas backup tidak ada.");
        if (!File.Exists(config.PgRestore))
            return (false, 0, $"pg_restore tidak ditemukan di {config.PathBin}.");

        try
        {
            var (exit, stdout, stderr) = await JalankanAsync(config.PgRestore, ["--list", path], null, ct);
            if (exit != 0) return (false, 0, $"pg_restore keluar dengan kode {exit}: {Ringkas(stderr)}");
            var objek = stdout.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith(';'));
            return objek > 0
                ? (true, objek, null)
                : (false, 0, "Arsip backup terbaca tapi tidak berisi objek apa pun.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, 0, Ringkas(ex.Message));
        }
    }

    private async Task<(int Exit, string Out, string Err)> JalankanAsync(string exe, string[] args,
        string? password, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Password masuk lewat environment proses anak saja; tanpa itu --no-password membuat
        // pg_dump gagal cepat alih-alih menunggu input yang tidak akan pernah datang.
        if (string.IsNullOrEmpty(password)) psi.Environment.Remove("PGPASSWORD");
        else psi.Environment["PGPASSWORD"] = password;

        using var proses = new Process { StartInfo = psi };
        proses.Start();
        var outT = proses.StandardOutput.ReadToEndAsync();
        var errT = proses.StandardError.ReadToEndAsync();

        using var batas = CancellationTokenSource.CreateLinkedTokenSource(ct);
        batas.CancelAfter(TimeSpan.FromSeconds(Math.Max(config.TimeoutDetik, 10)));
        try
        {
            await proses.WaitForExitAsync(batas.Token);
        }
        catch (OperationCanceledException)
        {
            try { proses.Kill(entireProcessTree: true); } catch (Exception ex) { log.LogDebug(ex, "Proses {Exe} sudah selesai", exe); }
            throw new TimeoutException($"{Path.GetFileName(exe)} melebihi {config.TimeoutDetik} detik.");
        }

        return (proses.ExitCode, await outT, await errT);
    }

    private static string Ringkas(string pesan) => pesan.Length > 500 ? pesan[..500] : pesan;

    /// <summary>Backup dianggap kedaluwarsa kalau lebih tua dari 26 jam (harian + toleransi macet).</summary>
    public static bool PerluBackupBaru(DateTime? suksesTerakhir, DateTime kini) =>
        suksesTerakhir is null || kini - suksesTerakhir.Value > TimeSpan.FromHours(24) + ToleransiJam;
}
