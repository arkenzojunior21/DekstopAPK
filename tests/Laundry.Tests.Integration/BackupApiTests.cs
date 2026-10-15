using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Tests.Integration;

/// <summary>
/// Backup, retensi, pemeriksaan versi, dan laporan konfigurasi lewat API nyata + pg_dump asli.
/// Semua berkas ditulis ke folder sementara milik ApiFactory, bukan folder backup sungguhan.
/// </summary>
[Collection("api")]
public class BackupApiTests
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private const string AdminPhone = "62812000000001";
    private const string AdminPassword = "Adm1nDev!2026";

    private readonly ApiFactory _factory;
    public BackupApiTests(ApiFactory factory) => _factory = factory;

    private static string Conn => EnvironmentConfig.Get("CONN_ADMIN",
        "Host=localhost;Port=5433;Database=laundry_admin;Username=laundry;Password=laundry_dev_2026");

    private LaundryDbContext Db() =>
        new(new DbContextOptionsBuilder<LaundryDbContext>().UseNpgsql(Conn).Options);

    private async Task<ApiFactory.Tokens> AdminAsync(HttpClient http)
    {
        await http.PostAsJsonAsync("/api/auth/bootstrap", new { phone = AdminPhone, password = AdminPassword });
        var login = await http.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone, password = AdminPassword });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<ApiFactory.Tokens>(J))!;
    }

    private async Task<JsonElement> PostAsync(HttpClient http, ApiFactory.Tokens t, string url, object? body)
    {
        var req = ApiFactory.Bearer(HttpMethod.Post, url, t);
        if (body is not null) req.Content = JsonContent.Create(body);
        var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    private async Task<(HttpStatusCode Kode, JsonElement Isi)> PostBalasAsync(HttpClient http,
        ApiFactory.Tokens? t, string url, object? body)
    {
        var req = t is null ? new HttpRequestMessage(HttpMethod.Post, url)
            : ApiFactory.Bearer(HttpMethod.Post, url, t);
        if (body is not null) req.Content = JsonContent.Create(body);
        var res = await http.SendAsync(req);
        var teks = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, string.IsNullOrWhiteSpace(teks)
            ? default : JsonDocument.Parse(teks).RootElement);
    }

    private async Task<JsonElement> GetAsync(HttpClient http, ApiFactory.Tokens t, string url)
    {
        var res = await http.SendAsync(ApiFactory.Bearer(HttpMethod.Get, url, t));
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    private async Task<JsonElement> PutAsync(HttpClient http, ApiFactory.Tokens t, string url, object body)
    {
        var res = await http.SendAsync(BearerJson(HttpMethod.Put, url, t, body));
        if (!res.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    private async Task<(HttpStatusCode Kode, JsonElement Isi)> PutBalasAsync(HttpClient http,
        ApiFactory.Tokens t, string url, object body)
    {
        var res = await http.SendAsync(BearerJson(HttpMethod.Put, url, t, body));
        var teks = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, string.IsNullOrWhiteSpace(teks)
            ? default : JsonDocument.Parse(teks).RootElement);
    }

    private static HttpRequestMessage BearerJson(HttpMethod method, string url, ApiFactory.Tokens t, object body)
    {
        var req = ApiFactory.Bearer(method, url, t);
        req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task BackupManualMembuatBerkasDanLangsungTerverifikasi()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);
        try
        {
            var hasil = await PostAsync(http, admin, "/api/backup/jalan", new { target = "admin" });
            Assert.True(hasil.GetProperty("sukses").GetBoolean());
            Assert.True(hasil.GetProperty("terverifikasi").GetBoolean());
            Assert.Equal(JsonValueKind.Null, hasil.GetProperty("error").ValueKind);

            var nama = hasil.GetProperty("namaFile").GetString()!;
            var path = Path.Combine(_factory.FolderBackup, nama);
            Assert.True(File.Exists(path), $"berkas tidak ada: {path}");
            Assert.Equal(hasil.GetProperty("ukuranByte").GetInt64(), new FileInfo(path).Length);
            Assert.True(hasil.GetProperty("ukuranByte").GetInt64() > 0);

            var status = await GetAsync(http, admin, "/api/backup/status");
            Assert.Equal(_factory.FolderBackup, status.GetProperty("folderBackup").GetString());
            Assert.Contains(status.GetProperty("berkas").EnumerateArray(),
                b => b.GetProperty("namaFile").GetString() == nama && b.GetProperty("terverifikasi").GetBoolean());
        }
        finally
        {
            await BersihkanBackupAsync();
        }
    }

    [Fact]
    public async Task TargetAsingDitolakSebelumProsesDanHanyaAdminYangBoleh()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);
        var pelanggan = await _factory.RegisterCustomerAsync(http);

        var salah = await PostBalasAsync(http, admin, "/api/backup/jalan", new { target = "postgres" });
        Assert.Equal(HttpStatusCode.BadRequest, salah.Kode);

        var hanyaAdmin = await PostBalasAsync(http, pelanggan, "/api/backup/jalan", new { target = "admin" });
        Assert.Equal(HttpStatusCode.Forbidden, hanyaAdmin.Kode);

        var tanpaToken = await http.PostAsJsonAsync("/api/backup/jalan", new { target = "admin" });
        Assert.Equal(HttpStatusCode.Unauthorized, tanpaToken.StatusCode);

        await BersihkanBackupAsync();
    }

    [Fact]
    public async Task RetensiMenyimpanBerkasTerbaruDanMenghapusYangTua()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);
        try
        {
            var tua = await PostAsync(http, admin, "/api/backup/jalan", new { target = "admin" });
            var baru = await PostAsync(http, admin, "/api/backup/jalan", new { target = "admin" });
            var namaTua = tua.GetProperty("namaFile").GetString()!;
            var namaBaru = baru.GetProperty("namaFile").GetString()!;
            Assert.NotEqual(namaTua, namaBaru);

            // Dua arsip yang baru dibuat ditunda umurnya: hanya yang melampaui retensi yang boleh pergi.
            await using (var db = Db())
            {
                foreach (var row in await db.BackupHistories.ToListAsync())
                    row.CreatedAtUtc = row.CreatedAtUtc.AddDays(row.FilePath.EndsWith(namaTua) ? -30 : -10);
                await db.SaveChangesAsync();
            }

            var bersih = await PostAsync(http, admin, "/api/backup/retensi", null);
            Assert.Equal(1, bersih.GetProperty("dihapus").GetInt32());
            Assert.Equal(namaTua, bersih.GetProperty("namaFileDihapus")[0].GetString());
            Assert.False(File.Exists(Path.Combine(_factory.FolderBackup, namaTua)));
            Assert.True(File.Exists(Path.Combine(_factory.FolderBackup, namaBaru)),
                "backup terbaru tidak boleh ikut terhapus");

            var status = await GetAsync(http, admin, "/api/backup/status");
            Assert.DoesNotContain(status.GetProperty("berkas").EnumerateArray(),
                b => b.GetProperty("namaFile").GetString() == namaTua);
        }
        finally
        {
            await BersihkanBackupAsync();
        }
    }

    [Fact]
    public async Task ArsipRusakTidakPernahDihapusButa()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);
        var tua = "admin-rusak-tua.dump";
        var baru = "admin-rusak-baru.dump";
        try
        {
            // Dua arsip sampah yang mengklaim sudah terverifikasi. Yang tua masuk antrean hapus
            // (yang baru dilindungi aturan "terbaru per target"), dan verifikasi ulang harus menang
            // atas klaim di riwayat: berkasnya tetap ada, penandaannya yang dibetulkan.
            await using (var db = Db())
            {
                foreach (var (nama, umur) in new[] { (tua, -30), (baru, 0) })
                {
                    var path = Path.Combine(_factory.FolderBackup, nama);
                    File.WriteAllText(path, "bukan arsip pg_dump");
                    db.BackupHistories.Add(new Laundry.Domain.BackupHistory
                    {
                        Target = "admin",
                        FilePath = path,
                        CreatedAtUtc = DateTime.UtcNow.AddDays(umur),
                        IsSuccess = true,
                        IsVerified = true,
                    });
                }

                await db.SaveChangesAsync();
            }

            var bersih = await PostAsync(http, admin, "/api/backup/retensi", null);
            Assert.Equal(0, bersih.GetProperty("dihapus").GetInt32());
            var ditolak = bersih.GetProperty("alasanDitolak").EnumerateArray().Select(v => v.GetString())
                .ToList();
            Assert.Single(ditolak);
            Assert.Contains(tua, ditolak[0]);
            Assert.Contains("tidak lolos verifikasi", ditolak[0]);

            foreach (var nama in new[] { tua, baru })
                Assert.True(File.Exists(Path.Combine(_factory.FolderBackup, nama)),
                    $"berkas bermasalah {nama} dibiarkan, tidak dihapus diam-diam");

            await using (var db = Db())
            {
                var row = await db.BackupHistories.FirstAsync(b => b.FilePath.EndsWith(tua));
                Assert.False(row.IsVerified);
                Assert.NotNull(row.Error);
            }
        }
        finally
        {
            foreach (var nama in new[] { tua, baru })
            {
                var path = Path.Combine(_factory.FolderBackup, nama);
                if (File.Exists(path)) File.Delete(path);
            }

            await BersihkanBackupAsync();
        }
    }

    [Fact]
    public async Task RetensiBisaDiaturLewatKonfigurasi()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);
        try
        {
            var status = await PutAsync(http, admin, "/api/backup/konfigurasi",
                new { retensiHari = 14, harian = false });
            Assert.Equal(14, status.GetProperty("retensiHari").GetInt32());
            Assert.False(status.GetProperty("backupHarian").GetBoolean());

            var rusak = await PutBalasAsync(http, admin, "/api/backup/konfigurasi", new { retensiHari = 0 });
            Assert.Equal(HttpStatusCode.BadRequest, rusak.Kode);

            var normal = await PutAsync(http, admin, "/api/backup/konfigurasi",
                new { retensiHari = 7, harian = true });
            Assert.Equal(7, normal.GetProperty("retensiHari").GetInt32());
        }
        finally
        {
            await BersihkanBackupAsync();
        }
    }

    [Fact]
    public async Task CekUpdateTanpaManifestMengakuBelumDikonfigurasi()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);

        var hasil = await GetAsync(http, admin, "/api/app/update-check");
        Assert.Equal("belum_dikonfigurasi", hasil.GetProperty("status").GetString());
        Assert.False(hasil.GetProperty("adaVersiBaru").GetBoolean());
        Assert.False(string.IsNullOrEmpty(hasil.GetProperty("catatan").GetString()));

        var versi = await GetAsync(http, admin, "/api/app/versi");
        Assert.Equal(hasil.GetProperty("versiSaatIni").GetString(), versi.GetProperty("versi").GetString());

        // Nama berkas berbahaya ditolak sebelum sistem file disentuh.
        foreach (var nama in new[] { "../windows/system32/cmd.exe", @"C:\evil.exe", "bukan-exe.txt", "" })
        {
            var tolak = await PostAsync(http, admin, "/api/app/update-verifikasi", new { namaFile = nama });
            Assert.Equal("nama_berkas_ditolak", tolak.GetProperty("status").GetString());
        }

        var hilang = await PostAsync(http, admin, "/api/app/update-verifikasi", new { namaFile = "tiada.exe" });
        Assert.Equal("berkas_tidak_ada", hilang.GetProperty("status").GetString());
    }

    [Fact]
    public async Task LaporanKonfigurasiMenyebutKunciTanpaMenampilkanNilainya()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);

        var status = await GetAsync(http, admin, "/api/config/status");
        var teks = status.ToString();
        Assert.DoesNotContain(AdminPassword, teks);
        Assert.DoesNotContain(UjiWebhook.Secret, teks);
        Assert.DoesNotContain("laundry_dev_2026", teks);

        var kunci = status.GetProperty("kunci").EnumerateArray().ToList();
        Assert.Contains(kunci, k => k.GetProperty("kunci").GetString() == "WEBHOOK_SECRET"
                                   && k.GetProperty("terisi").GetBoolean());
        Assert.Contains(kunci, k => k.GetProperty("kunci").GetString() == "UPDATE_CHECK_URL"
                                   && !k.GetProperty("terisi").GetBoolean());
        foreach (var k in kunci)
            Assert.False(k.TryGetProperty("nilai", out _), "laporan konfigurasi tidak boleh memuat nilai");

        Assert.Equal("belum_dikonfigurasi", status.GetProperty("update").GetProperty("status").GetString());
        Assert.StartsWith(_factory.FolderBackup,
            status.GetProperty("backup").GetProperty("folderBackup").GetString());
        Assert.Equal(JsonValueKind.Number, status.GetProperty("notifikasi").GetProperty("menunggu").ValueKind);
    }

    private async Task BersihkanBackupAsync()
    {
        await using var db = Db();
        db.BackupHistories.RemoveRange(await db.BackupHistories.ToListAsync());
        await db.SaveChangesAsync();
        if (Directory.Exists(_factory.FolderBackup))
            foreach (var f in Directory.GetFiles(_factory.FolderBackup, "*.dump")) File.Delete(f);
    }
}
