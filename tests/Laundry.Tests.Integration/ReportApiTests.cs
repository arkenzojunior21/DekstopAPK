using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Laundry.Tests.Integration;

/// <summary>
/// Laporan dan ekspor lewat API nyata (fase 9). Perbandingan angka selalu dibuat sebagai selisih
/// "sebelum vs sesudah", karena database tes berisi pesanan dari tes lain — angka absolut akan
/// membuat tes ini saling menjatuhkan.
/// </summary>
[Collection("api")]
public class ReportApiTests
{
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private const string AdminPhone = "62812000000001";
    private const string AdminPassword = "Adm1nDev!2026";
    private const decimal HargaKilo = 10_000m;
    private const decimal DuaKilo = 20_000m;

    private readonly ApiFactory _factory;
    public ReportApiTests(ApiFactory factory) => _factory = factory;

    private async Task<(HttpClient http, ApiFactory.Tokens admin)> AdminAsync()
    {
        var http = _factory.Client();
        await http.PostAsJsonAsync("/api/auth/bootstrap", new { phone = AdminPhone, password = AdminPassword });
        var login = await http.PostAsJsonAsync("/api/auth/login",
            new { phone = AdminPhone, password = AdminPassword });
        login.EnsureSuccessStatusCode();
        return (http, (await login.Content.ReadFromJsonAsync<ApiFactory.Tokens>(J))!);
    }

    private async Task<JsonElement> GetAsync(HttpClient http, ApiFactory.Tokens t, string url)
    {
        var res = await http.SendAsync(ApiFactory.Bearer(HttpMethod.Get, url, t));
        if (!res.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    private static async Task<(HttpStatusCode Kode, string Teks)> GetRawAsync(HttpClient http,
        ApiFactory.Tokens? t, string url)
    {
        var res = await http.SendAsync(t is null ? new HttpRequestMessage(HttpMethod.Get, url)
            : ApiFactory.Bearer(HttpMethod.Get, url, t));
        return (res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    private static async Task<(byte[] Isi, string? Tipe, string? Nama)> GetBerkasAsync(HttpClient http,
        ApiFactory.Tokens t, string url)
    {
        var res = await http.SendAsync(ApiFactory.Bearer(HttpMethod.Get, url, t));
        if (!res.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadAsByteArrayAsync(), res.Content.Headers.ContentType?.MediaType,
            res.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    private async Task<JsonElement> PostAsync(HttpClient http, ApiFactory.Tokens t, string url, object body)
    {
        var req = ApiFactory.Bearer(HttpMethod.Post, url, t);
        req.Content = JsonContent.Create(body);
        var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
            throw new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    /// <summary>
    /// Layanan kiloan 10.000/kg + pelanggan + pesanan 2 kg (final 20.000) + DP tunai 5.000.
    /// Semua lewat endpoint resmi, jadi laporan menguji hasil tulisan sistem, bukan hasil sisipan DB.
    /// </summary>
    private async Task<(string nomor, decimal final)> PesanBerDpAsync(HttpClient http, ApiFactory.Tokens admin)
    {
        var nama = $"Kilo Laporan {Guid.NewGuid():N}"[..28];
        var service = await PostAsync(http, admin, "/api/catalog", new
        {
            nama, unitType = "PerKg", minimumWeightKg = (decimal?)1m, price = HargaKilo,
            standardDurationDays = 3, expressDurationDays = 1, expressSurcharge = 5_000m, isActive = true,
        });
        var customer = await PostAsync(http, admin, "/api/customers",
            new { nama = "Pelanggan Laporan", phone = ApiFactory.RandomPhone() });
        var order = await PostAsync(http, admin, "/api/orders", new
        {
            customerId = customer.GetProperty("id").GetGuid(),
            items = new[]
            {
                new
                {
                    serviceId = service.GetProperty("id").GetGuid(),
                    estimatedWeightKg = 2m,
                    estimatedItemCount = (decimal?)null,
                },
            },
            express = false,
            fulfillment = "AmbilSendiri",
        });

        await PostAsync(http, admin, "/api/payments/cash", new
        {
            orderId = order.GetProperty("id").GetGuid(),
            amount = 5_000m,
            kind = "Dp",
            note = (string?)null,
        });
        return (order.GetProperty("orderNumber").GetString()!, order.GetProperty("finalTotal").GetDecimal());
    }

    private static decimal Angka(JsonElement elemen, string nama) => elemen.GetProperty(nama).GetDecimal();

    private static string Latin1(byte[] isi) => Encoding.Latin1.GetString(isi);

    [Fact]
    public async Task LaporanDanEksporHanyaUntukAdmin()
    {
        var (http, admin) = await AdminAsync();
        var pelanggan = await _factory.RegisterCustomerAsync(http);

        foreach (var url in new[]
        {
            "/api/reports/laporan", "/api/reports/tabel?jenis=ringkasan",
            "/api/reports/export?jenis=ringkasan&format=pdf", "/api/audit",
        })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetRawAsync(http, null, url)).Kode);
            Assert.Equal(HttpStatusCode.Forbidden, (await GetRawAsync(http, pelanggan, url)).Kode);
        }

        Assert.Equal(HttpStatusCode.OK, (await GetRawAsync(http, admin, "/api/reports/laporan")).Kode);
    }

    [Fact]
    public async Task RingkasanBertambahPersisSamaDenganPesananBaru()
    {
        var (http, admin) = await AdminAsync();
        var sebelum = (await GetAsync(http, admin, "/api/reports/laporan")).GetProperty("ringkasan");

        var (nomor, final) = await PesanBerDpAsync(http, admin);
        Assert.Equal(DuaKilo, final);        var sesudah = (await GetAsync(http, admin, "/api/reports/laporan")).GetProperty("ringkasan");

        Assert.Equal(1m, Angka(sesudah, "jumlahPesanan") - Angka(sebelum, "jumlahPesanan"));
        Assert.Equal(final, Angka(sesudah, "omzet") - Angka(sebelum, "omzet"));
        // DP tunai 5.000 masuk penerimaan DAN bucket DP; sisanya menjadi piutang baru.
        Assert.Equal(5_000m, Angka(sesudah, "penerimaan") - Angka(sebelum, "penerimaan"));
        Assert.Equal(5_000m, Angka(sesudah, "dp") - Angka(sebelum, "dp"));
        Assert.Equal(0m, Angka(sesudah, "pelunasan") - Angka(sebelum, "pelunasan"));
        Assert.Equal(0m, Angka(sesudah, "refund") - Angka(sebelum, "refund"));
        Assert.Equal(final - 5_000m, Angka(sesudah, "piutang") - Angka(sebelum, "piutang"));
        Assert.Equal(0m, Angka(sesudah, "jumlahDibatalkan") - Angka(sebelum, "jumlahDibatalkan"));

        var transaksi = await GetAsync(http, admin, "/api/reports/laporan");
        Assert.Contains(transaksi.GetProperty("transaksi").EnumerateArray(),
            t => t.GetProperty("orderNumber").GetString() == nomor);
    }

    [Fact]
    public async Task TabelPiutangDanTransaksiMemakaiNomorPesananYangSama()
    {
        var (http, admin) = await AdminAsync();
        var (nomor, final) = await PesanBerDpAsync(http, admin);

        var piutang = await GetAsync(http, admin, "/api/reports/tabel?jenis=piutang");
        Assert.Equal("Sisa", piutang.GetProperty("kolom")[5].GetString());
        var baris = BarisTeratas(piutang, nomor);
        Assert.Equal(final, baris[3].GetProperty("angka").GetDecimal());
        Assert.Equal(final - 5_000m, baris[5].GetProperty("angka").GetDecimal());
        // Sel uang membawa teks bacanya, jadi layar dan PDF menulis angka yang sama.
        Assert.StartsWith("Rp", baris[5].GetProperty("isi").GetString());

        var transaksi = await GetAsync(http, admin, "/api/reports/tabel?jenis=transaksi");
        var rinci = BarisTeratas(transaksi, nomor);
        Assert.Equal("Dikonfirmasi", rinci[3].GetProperty("isi").GetString());
        Assert.Equal(final, rinci[4].GetProperty("angka").GetDecimal());
        Assert.Equal(5_000m, rinci[5].GetProperty("angka").GetDecimal());
        Assert.Equal(5_000m, rinci[6].GetProperty("angka").GetDecimal());
        Assert.Equal(0m, rinci[7].GetProperty("angka").GetDecimal());
        Assert.Equal(15_000m, rinci[9].GetProperty("angka").GetDecimal());
    }

    private static JsonElement[] BarisTeratas(JsonElement tabel, string nomor)
    {
        var baris = tabel.GetProperty("baris").EnumerateArray()
            .Where(b => b[0].GetProperty("isi").GetString() == nomor).ToList();
        Assert.NotEmpty(baris);
        return baris[0].EnumerateArray().ToArray();
    }

    [Fact]
    public async Task RentangMasaLaluKosongMenghasilkanNolBukanDataContoh()
    {
        var (http, admin) = await AdminAsync();
        await PesanBerDpAsync(http, admin);
        var laporan = await GetAsync(http, admin, "/api/reports/laporan?dari=1999-01-01&sampai=1999-01-03");

        var r = laporan.GetProperty("ringkasan");
        Assert.Equal(0m, Angka(r, "jumlahPesanan"));
        Assert.Equal(0m, Angka(r, "omzet"));
        Assert.Equal(0m, Angka(r, "penerimaan"));
        // Piutang adalah posisi sampai akhir rentang: sebelum 1999 belum ada apa pun.
        Assert.Equal(0m, Angka(r, "piutang"));
        Assert.Equal(0m, Angka(r, "saldoKreditPosisi"));
        Assert.Empty(laporan.GetProperty("transaksi").EnumerateArray());
        Assert.Empty(laporan.GetProperty("piutang").EnumerateArray());

        // Deret harian tetap berisi setiap hari di rentang supaya sumbu grafik tidak melompat.
        Assert.Equal(3, laporan.GetProperty("harian").EnumerateArray().Count());
        Assert.All(laporan.GetProperty("harian").EnumerateArray(), h => Assert.Equal(0m, Angka(h, "omzet")));
    }

    [Fact]
    public async Task RentangTerlaluLebarDibatasiDanDilaporkanApaAdanya()
    {
        var (http, admin) = await AdminAsync();
        var laporan = await GetAsync(http, admin, "/api/reports/laporan?dari=2000-01-01");

        var rentang = laporan.GetProperty("rentang");
        Assert.Equal(366, rentang.GetProperty("jumlahHari").GetInt32());
        Assert.Equal(DateTime.UtcNow.Date.AddDays(-365), rentang.GetProperty("mulaiUtc").GetDateTime().Date);
        // Batas akhir selalu hari berikutnya (eksklusif), jadi hari ini ikut terhitung.
        Assert.Equal(DateTime.UtcNow.Date.AddDays(1), rentang.GetProperty("batasAkhirUtc").GetDateTime().Date);
    }

    [Fact]
    public async Task FilterTanggalJenisAtauFormatSalahDitolakDenganPesan()
    {
        var (http, admin) = await AdminAsync();

        var (kode, teks) = await GetRawAsync(http, admin, "/api/reports/laporan?dari=kemarin");
        Assert.Equal(HttpStatusCode.BadRequest, kode);
        Assert.Contains("bukan tanggal yang sah", teks);

        (kode, teks) = await GetRawAsync(http, admin, "/api/reports/tabel?jenis=ramalan");
        Assert.Equal(HttpStatusCode.BadRequest, kode);
        Assert.Contains("Jenis laporan tidak dikenali", teks);

        (kode, teks) = await GetRawAsync(http, admin, "/api/reports/export?jenis=ringkasan&format=dwg");
        Assert.Equal(HttpStatusCode.BadRequest, kode);
        Assert.Contains("Format ekspor tidak dikenali", teks);
        Assert.Contains("pdf", teks);

        (_, teks) = await GetRawAsync(http, admin, "/api/audit?entitas=" + new string('x', 100));
        Assert.Contains("maksimal 64 karakter", teks);
    }

    [Fact]
    public async Task EksporTigaFormatMenghasilkanBerkasYangBenarBenarTerbaca()
    {
        var (http, admin) = await AdminAsync();
        var (nomor, _) = await PesanBerDpAsync(http, admin);

        var pdf = await GetBerkasAsync(http, admin, "/api/reports/export?jenis=transaksi&format=pdf");
        Assert.Equal("application/pdf", pdf.Tipe);
        Assert.EndsWith(".pdf", pdf.Nama);
        var teksPdf = Latin1(pdf.Isi);
        Assert.StartsWith("%PDF-1.4", teksPdf);
        Assert.EndsWith("%%EOF", teksPdf.TrimEnd());
        Assert.Contains(nomor, teksPdf);

        var xlsx = await GetBerkasAsync(http, admin, "/api/reports/export?jenis=transaksi&format=xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Tipe);
        Assert.EndsWith(".xlsx", xlsx.Nama);
        // XLSX adalah arsip zip: tanpa "PK" di depan, berkasnya bukan spreadsheet.
        Assert.Equal((byte)'P', xlsx.Isi[0]);
        Assert.Equal((byte)'K', xlsx.Isi[1]);

        var csv = await GetBerkasAsync(http, admin, "/api/reports/export?jenis=transaksi&format=csv");
        Assert.Equal("text/csv", csv.Tipe);
        Assert.EndsWith(".csv", csv.Nama);
        var teksCsv = Encoding.UTF8.GetString(csv.Isi);
        Assert.StartsWith("sep=;", teksCsv);
        Assert.Contains("Nomor;Tanggal;Pelanggan;Status;Total;DP;Pembayaran;Refund;Kredit;Sisa", teksCsv);
        Assert.Contains(nomor, teksCsv);
    }

    [Fact]
    public async Task EksporRentangKosongTetapMenghasilkanBerkasSah()
    {
        var (http, admin) = await AdminAsync();
        var pdf = await GetBerkasAsync(http, admin,
            "/api/reports/export?jenis=transaksi&format=pdf&dari=1999-01-01&sampai=1999-01-02");

        Assert.True(pdf.Isi.Length > 400);
        var teks = Latin1(pdf.Isi);
        Assert.StartsWith("%PDF-1.4", teks);
        Assert.Contains("Nomor", teks);
        Assert.Contains("Halaman 1 dari 1", teks);
    }

    [Fact]
    public async Task AuditDapatDicariBerdasarkanNomorPesananDanJenisEntitas()
    {
        var (http, admin) = await AdminAsync();
        var (nomor, _) = await PesanBerDpAsync(http, admin);

        var hasil = await GetAsync(http, admin, $"/api/audit?entitas=order&kata={Uri.EscapeDataString(nomor)}");
        var baris = hasil.GetProperty("baris").EnumerateArray().ToList();
        Assert.True(hasil.GetProperty("total").GetInt32() >= 1);
        Assert.NotEmpty(baris);
        Assert.All(baris, b => Assert.Equal("Order", b.GetProperty("entityType").GetString()));
        Assert.Contains(nomor, baris[0].GetProperty("afterJson").GetString());
        // Aktor ditulis nama atau nomor akun, bukan GUID mentah.
        Assert.False(string.IsNullOrWhiteSpace(baris[0].GetProperty("actor").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(baris[0].GetProperty("correlationId").GetString()));

        var pembayaran = await GetAsync(http, admin, "/api/audit?entitas=Payment&ukuran=5");
        Assert.All(pembayaran.GetProperty("baris").EnumerateArray(),
            b => Assert.Equal("Payment", b.GetProperty("entityType").GetString()));
        Assert.Equal(5, pembayaran.GetProperty("ukuranHalaman").GetInt32());

        var dipaksa = await GetAsync(http, admin, "/api/audit?ukuran=9999");
        Assert.Equal(500, dipaksa.GetProperty("ukuranHalaman").GetInt32());
    }

    [Fact]
    public async Task AuditEksporLewatJalurLaporanYangSama()
    {
        var (http, admin) = await AdminAsync();
        var (nomor, _) = await PesanBerDpAsync(http, admin);
        var csv = await GetBerkasAsync(http, admin, "/api/reports/export?jenis=audit&format=csv");

        var teks = Encoding.UTF8.GetString(csv.Isi);
        Assert.Contains("Waktu;Tipe;Entitas;Aksi;Aktor;Sebelum;Sesudah;Alasan;Sumber", teks);
        Assert.Contains(nomor, teks);
        Assert.EndsWith(".csv", csv.Nama);
    }

    [Fact]
    public async Task AuditTidakPunyaJalurUbahAtauHapus()
    {
        var (http, admin) = await AdminAsync();
        var sebelum = await GetAsync(http, admin, "/api/audit?ukuran=1");
        var id = Guid.NewGuid();

        foreach (var (method, url) in new[]
        {
            (HttpMethod.Post, "/api/audit"),
            (HttpMethod.Delete, $"/api/audit/{id}"),
            (HttpMethod.Put, $"/api/audit/{id}"),
            (HttpMethod.Patch, $"/api/audit/{id}"),
        })
        {
            var req = ApiFactory.Bearer(method, url, admin);
            if (method == HttpMethod.Post) req.Content = JsonContent.Create(new { });
            var kode = (await http.SendAsync(req)).StatusCode;
            // 404 = tidak ada rute, 405 = jalurnya tahu-tahu hanya GET. Keduanya berarti tidak bisa menulis.
            Assert.True(kode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{method} {url} menjawab {(int)kode}");
        }

        var sesudah = await GetAsync(http, admin, "/api/audit?ukuran=1");
        Assert.Equal(Angka(sebelum, "total"), Angka(sesudah, "total"));
    }
}
