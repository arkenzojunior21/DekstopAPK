using System.Security.Cryptography;
using System.Text.Json;
using Laundry.Application.Core;
using Laundry.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Laundry.Infrastructure.Update;

/// <summary>Versi aplikasi diambil dari assembly, tidak ditulis ulang di beberapa tempat.</summary>
public static class AppInfo
{
    public static string Versi => typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}

/// <summary>
/// Pemeriksaan versi lewat manifest publik (UPDATE_CHECK_URL). Tidak ada unduhan otomatis dan
/// tidak ada instalasi otomatis: yang bisa dipastikan hanya "ada versi lain yang diumumkan".
/// Integritas berkas installer diperiksa terpisah terhadap sha256 di manifest, oleh manusia,
/// sebelum berkas itu dijalankan.
/// </summary>
public sealed class UpdateChecker(HttpClient http, ILogger<UpdateChecker> log) : IUpdateChecker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const string StatusBelum = "belum_dikonfigurasi";
    public const string StatusTerkini = "versi_sudah_terkini";
    public const string StatusBaru = "versi_baru_tersedia";
    public const string StatusManifestRusak = "manifest_tidak_sah";
    public const string StatusGagal = "gagal_diperiksa";

    public async Task<UpdateCheckResult> PeriksaAsync(CancellationToken ct = default)
    {
        var hasil = await BacaManifestAsync(ct);
        return hasil.Manifest is null
            ? new UpdateCheckResult(AppInfo.Versi, hasil.Status, null, false, null, null, hasil.Catatan)
            : Bandingkan(AppInfo.Versi, hasil.Manifest, hasil.Status, hasil.Catatan);
    }

    public sealed record Manifest(string? Versi, string? Url, string? Sha256, string? Catatan);

    private sealed record HasilManifest(Manifest? Manifest, string Status, string? Catatan);

    private async Task<HasilManifest> BacaManifestAsync(CancellationToken ct)
    {
        var url = EnvironmentConfig.Get("UPDATE_CHECK_URL");
        if (string.IsNullOrWhiteSpace(url))
            return new(null, StatusBelum,
                "UPDATE_CHECK_URL belum diisi, jadi tidak ada yang bisa diperiksa. Ini bukan berarti versi sudah terbaru.");

        try
        {
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                return new(null, StatusGagal, $"Manifest balas HTTP {(int)resp.StatusCode}.");

            var isi = await resp.Content.ReadAsStringAsync(ct);
            var manifest = JsonSerializer.Deserialize<Manifest>(isi, Json);
            return manifest is null || string.IsNullOrWhiteSpace(manifest.Versi)
                ? new(null, StatusManifestRusak, "Manifest tidak punya field versi.")
                : new(manifest, StatusBaru, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Pemeriksaan update gagal");
            return new(null, StatusGagal, Ringkas($"Pemeriksaan gagal: {ex.Message}"));
        }
    }

    /// <summary>Dipisah supaya aturan pembandingan versi bisa diuji tanpa jaringan.</summary>
    public static UpdateCheckResult Bandingkan(string versiSaatIni, Manifest manifest, string statusAwal,
        string? catatan)
    {
        var kini = Jeda(versiSaatIni);
        var sana = Jeda(manifest.Versi ?? string.Empty);
        if (kini is null || sana is null)
            return new UpdateCheckResult(versiSaatIni, StatusManifestRusak, manifest.Versi, false,
                manifest.Url, manifest.Sha256,
                $"Versi tidak bisa dibandingkan: '{versiSaatIni}' vs '{manifest.Versi}'.");

        var lebihBaru = sana > kini;
        return new UpdateCheckResult(versiSaatIni,
            lebihBaru ? StatusBaru : StatusTerkini, manifest.Versi, lebihBaru,
            manifest.Url, manifest.Sha256,
            lebihBaru ? manifest.Catatan : catatan);
    }

    private static Version? Jeda(string teks) =>
        Version.TryParse(teks.Trim(), out var v) ? v : null;

    /// <summary>
    /// Hitung sha256 berkas installer yang sudah diunduh manual di folder UPDATE_DIR, lalu bandingkan
    /// dengan sha256 yang diumumkan manifest. Nama berkas diterima polos (tanpa path) supaya tidak
    /// bisa dipakai membaca file di luar folder itu.
    /// </summary>
    public async Task<UpdateVerifyResult> VerifikasiBerkasAsync(string namaFile, CancellationToken ct = default)
    {
        var folder = EnvironmentConfig.Get("UPDATE_DIR",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "laundry-pg", "downloads"));
        if (string.IsNullOrWhiteSpace(namaFile) || namaFile.Contains('/') || namaFile.Contains('\\')
            || namaFile.Contains("..") || Path.GetExtension(namaFile) != ".exe")
            return new UpdateVerifyResult(false, null, null, "nama_berkas_ditolak",
                "Hanya nama berkas .exe polos di folder UPDATE_DIR yang boleh diperiksa.");

        var path = Path.Combine(folder, namaFile);
        if (!File.Exists(path))
            return new UpdateVerifyResult(false, null, null, "berkas_tidak_ada", $"Tidak ada {path}.");

        // Berkas installer bisa puluhan megabyte; hash dikerjakan di thread pool supaya request
        // tidak memblokir, dan .NET 8 belum punya ComputeHashAsync berbasis stream.
        var hash = Convert.ToHexString(await Task.Run(() =>
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return sha.ComputeHash(stream);
        }, ct)).ToLowerInvariant();

        var hasil = await BacaManifestAsync(ct);
        var diumumkan = hasil.Manifest?.Sha256?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(diumumkan))
            return new UpdateVerifyResult(false, hash, hasil.Manifest?.Versi, StatusBelum,
                "Berkas terhitung, tetapi manifest belum menyediakan sha256 untuk dibandingkan.");

        var cocok = CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(hash), Convert.FromHexString(diumumkan));
        return new UpdateVerifyResult(cocok, hash, hasil.Manifest?.Versi,
            cocok ? "cocok" : "tidak_cocok",
            cocok ? "Berkas sama dengan yang diumumkan manifest."
                : "Berkas berbeda dari yang diumumkan — jangan jalankan.");
    }

    private static string Ringkas(string teks) => teks.Length > 300 ? teks[..300] : teks;
}
