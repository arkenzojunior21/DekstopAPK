using Laundry.Application.Core;
using Laundry.Infrastructure.Backup;
using Laundry.Infrastructure.Update;

namespace Laundry.Tests.Unit;

/// <summary>
/// Aturan Fase 8 yang sengaja dipisah dari IO: keputusan retensi, keputusan "perlu backup baru",
/// dan pembandingan versi. Semuanya bisa dibuktikan tanpa database, pg_dump, maupun jaringan.
/// </summary>
public class BackupRulesTests
{
    private static readonly DateTime Kini = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static BackupInfo Arsip(string target, int umurJam, bool sukses = true, bool terverifikasi = true) =>
        new(Guid.NewGuid(), target, $"{target}-{umurJam}j.dump", 1024, Kini.AddHours(-umurJam),
            sukses, terverifikasi, null);

    [Fact]
    public void ArsipTerbaruPerTargetTidakPernahIkutTerhapus()
    {
        // Satu arsip, dua arsip, dan arsip campur target: yang paling baru per target selalu disimpan.
        Assert.Empty(RetensiBackup.YangBolehDihapus(new[] { Arsip("admin", 30) }, Kini, 7));

        var dua = new[] { Arsip("admin", 300), Arsip("admin", 1) };
        Assert.Equal(new[] { "admin-300j.dump" },
            RetensiBackup.YangBolehDihapus(dua, Kini, 7).Select(b => b.NamaFile));

        var campur = new[] { Arsip("admin", 300), Arsip("cloud", 300), Arsip("admin", 1), Arsip("cloud", 1) };
        Assert.Equal(2, RetensiBackup.YangBolehDihapus(campur, Kini, 7).Count);

        // Umur saja tidak cukup: semua masih di dalam jendela retensi, jadi tidak ada yang pergi.
        Assert.Empty(RetensiBackup.YangBolehDihapus(
            new[] { Arsip("admin", 30), Arsip("admin", 1) }, Kini, 7));
        Assert.Empty(RetensiBackup.YangBolehDihapus([], Kini, 7));
    }

    [Fact]
    public void BelumTerverifikasiAtauBelumSuksesTidakPernahDihapus()
    {
        var semua = new[]
        {
            Arsip("admin", 300, terverifikasi: false),
            Arsip("admin", 290, sukses: false),
            Arsip("admin", 280),
            Arsip("admin", 1),
        };
        var layak = RetensiBackup.YangBolehDihapus(semua, Kini, 7);
        Assert.Single(layak);
        Assert.Equal(280, (int)(Kini - layak[0].DibuatUtc).TotalHours);
    }

    [Fact]
    public void JendelaRetensiDihitungDariBatasHari()
    {
        var semua = new[] { Arsip("admin", 200), Arsip("admin", 100), Arsip("admin", 1) };
        Assert.Single(RetensiBackup.YangBolehDihapus(semua, Kini, 7));
        // Batas diperketat jadi 4 hari (96 jam): arsip 100 jam ikut layak, arsip 1 jam tetap terlindungi.
        Assert.Equal(2, RetensiBackup.YangBolehDihapus(semua, Kini, 4).Count);

        // Retensi 0 tidak berarti "hapus semua": diperlakukan sebagai satu hari.
        Assert.Single(RetensiBackup.YangBolehDihapus(
            new[] { Arsip("admin", 48), Arsip("admin", 1) }, Kini, 0));
        Assert.Empty(RetensiBackup.YangBolehDihapus(
            new[] { Arsip("admin", 2), Arsip("admin", 1) }, Kini, 0));
    }

    [Fact]
    public void UrutanAntreanHapusPerTargetLaluTuaDulu()
    {
        var semua = new[]
        {
            Arsip("cloud", 40), Arsip("admin", 100), Arsip("cloud", 90), Arsip("admin", 30),
            Arsip("cloud", 1), Arsip("admin", 1),
        };
        var layak = RetensiBackup.YangBolehDihapus(semua, Kini, 1);
        Assert.Equal(new[] { "admin", "admin", "cloud", "cloud" }, layak.Select(b => b.Target).ToArray());
        for (var i = 1; i < layak.Count; i++)
            if (layak[i].Target == layak[i - 1].Target)
                Assert.True(layak[i - 1].DibuatUtc <= layak[i].DibuatUtc, "arsip tua didahulukan");
    }

    [Fact]
    public void BackupHarianMenagihBerkasBaruSetelahSehariDuaJam()
    {
        Assert.True(BackupService.PerluBackupBaru(null, Kini));
        Assert.False(BackupService.PerluBackupBaru(Kini.AddHours(-20), Kini));
        Assert.False(BackupService.PerluBackupBaru(Kini.AddHours(-26), Kini));
        Assert.True(BackupService.PerluBackupBaru(Kini.AddHours(-27), Kini));
    }

    private static UpdateCheckResult Bandingkan(string? sana, string? catatan = null) =>
        UpdateChecker.Bandingkan("0.8.0",
            new UpdateChecker.Manifest(sana, "https://rilis.tidakterkenal/app.exe", "ab12", "catatan manifest"),
            UpdateChecker.StatusBelum, catatan);

    [Fact]
    public void VersiDibandingkanSecaraNumerikBukanKarakter()
    {
        var baru = Bandingkan("0.9.0");
        Assert.Equal(UpdateChecker.StatusBaru, baru.Status);
        Assert.True(baru.AdaVersiBaru);
        Assert.Equal("0.9.0", baru.VersiTersedia);
        Assert.Equal("https://rilis.tidakterkenal/app.exe", baru.UrlUnduhan);
        Assert.Equal("ab12", baru.Sha256);
        Assert.Equal("0.8.0", baru.VersiSaatIni);

        // 0.10.0 > 0.9.0; kalau dibandingkan sebagai teks, "0.1..." kalah dari "0.9...".
        Assert.True(Bandingkan("0.10.0").AdaVersiBaru);
    }

    [Fact]
    public void VersiSamaAtauLebihTuaDigambarkanSebagaiTerkini()
    {
        var sama = Bandingkan("0.8.0", "belum ada yang diumumkan lebih baru");
        Assert.Equal(UpdateChecker.StatusTerkini, sama.Status);
        Assert.False(sama.AdaVersiBaru);
        Assert.Equal("belum ada yang diumumkan lebih baru", sama.Catatan);

        // Manifest menawarkan versi lebih lama (mis. salinan rilis keliru): tidak disebut "baru".
        Assert.False(Bandingkan("0.7.9").AdaVersiBaru);
    }

    [Fact]
    public void VersiYangTidakTerbacaDilaporkanRusakBukanAman()
    {
        foreach (var sana in new string?[] { "1.9-beta", "versi terbaru", "", null })
        {
            var hasil = Bandingkan(sana);
            Assert.Equal(UpdateChecker.StatusManifestRusak, hasil.Status);
            Assert.False(hasil.AdaVersiBaru);
            Assert.Contains("tidak bisa dibandingkan", hasil.Catatan);
        }
    }
}
