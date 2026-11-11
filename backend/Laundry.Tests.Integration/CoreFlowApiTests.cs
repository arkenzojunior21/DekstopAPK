using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Laundry.Tests.Integration;

[Collection("api")]
public class CoreFlowApiTests
{
    private readonly ApiFactory _factory;
    public CoreFlowApiTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    private const string AdminPhone = "62812000000001";
    private const string AdminPassword = "Adm1nDev!2026";

    private async Task<ApiFactory.Tokens> AdminAsync(HttpClient http)
    {
        await http.PostAsJsonAsync("/api/auth/bootstrap", new { phone = AdminPhone, password = AdminPassword });
        var login = await http.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone, password = AdminPassword });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<ApiFactory.Tokens>(J))!;
    }

    private static void Bearer(HttpRequestMessage req, ApiFactory.Tokens t) =>
        req.Headers.Add("Authorization", $"Bearer {t.accessToken}");

    private async Task<(HttpClient http, ApiFactory.Tokens admin)> SetupAsync()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);
        return (http, admin);
    }

    private async Task<JsonElement> PostJsonAsync(HttpClient http, ApiFactory.Tokens t, string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        Bearer(req, t);
        var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    private async Task<(HttpClient http, ApiFactory.Tokens admin, JsonElement service)> PrepareServiceAsync()
    {
        var (http, admin) = await SetupAsync();
        var nama = $"Kilo Tes {Guid.NewGuid():N}"[..30];
        var service = await PostJsonAsync(http, admin, "/api/catalog", new
        {
            nama, unitType = "PerKg", minimumWeightKg = (decimal?)1m, price = 10_000m,
            standardDurationDays = 3, expressDurationDays = 1, expressSurcharge = 5_000m, isActive = true,
        });
        return (http, admin, service);
    }

    private async Task<(HttpClient http, ApiFactory.Tokens admin, JsonElement service, JsonElement customer)> PrepareOrderAsync()
    {
        var (http, admin, service) = await PrepareServiceAsync();
        var customer = await PostJsonAsync(http, admin, "/api/customers",
            new { nama = "Pelanggan Alur", phone = ApiFactory.RandomPhone() });
        return (http, admin, service, customer);
    }

    /// <summary>Registrasi OTP penuh lewat API lalu ambil customer profile id hasil pencarian admin.</summary>
    private async Task<(ApiFactory.Tokens tokens, Guid customerId)> RegisterCustomerWithProfileAsync(
        HttpClient http, ApiFactory.Tokens admin)
    {
        var phone = ApiFactory.RandomPhone();
        var start = await http.PostAsJsonAsync("/api/auth/register/start", new { phone });
        start.EnsureSuccessStatusCode();
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(J);
        var reg = await http.PostAsJsonAsync("/api/auth/register/complete", new
        {
            challengeId = started.GetProperty("challengeId").GetGuid(),
            otp = started.GetProperty("devCode").GetString(),
            password = "PasswordAlur!123",
            nama = "Pelanggan Alur",
        });
        reg.EnsureSuccessStatusCode();
        var tokens = (await reg.Content.ReadFromJsonAsync<ApiFactory.Tokens>(J))!;
        var found = await GetAsync(http, admin, $"/api/customers?q={phone}&pageSize=5");
        var customerId = found.EnumerateArray()
            .First(c => c.GetProperty("phone").GetString() == phone)
            .GetProperty("id").GetGuid();
        return (tokens, customerId);
    }

    private async Task<JsonElement> CreateOrderAsync(HttpClient http, ApiFactory.Tokens admin, Guid serviceId, Guid customerId, decimal estKg)
    {
        var order = await PostJsonAsync(http, admin, "/api/orders", new
        {
            customerId,
            items = new[] { new { serviceId, estimatedWeightKg = estKg, estimatedItemCount = (decimal?)null } },
            express = false,
            fulfillment = "AmbilSendiri",
        });
        return order;
    }

    [Fact]
    public async Task WalkIn_KiloanTimbangBayarSelesai()
    {
        var (http, admin, service, customer) = await PrepareOrderAsync();
        var order = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(),
            customer.GetProperty("id").GetGuid(), 2.5m);
        var id = order.GetProperty("id").GetGuid();
        var itemId = order.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal("Dikonfirmasi", order.GetProperty("status").GetString());
        Assert.Equal(25_000m, order.GetProperty("estimatedTotal").GetDecimal());

        await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });
        var ditimbang = await PostJsonAsync(http, admin, $"/api/orders/{id}/actuals",
            new { actuals = new[] { new { orderItemId = itemId, actualWeightKg = 3m, actualItemCount = (decimal?)null } } });
        Assert.Equal(30_000m, ditimbang.GetProperty("finalTotal").GetDecimal());
        Assert.Equal("DitimbangDanDiverifikasi", ditimbang.GetProperty("status").GetString());

        foreach (var tahap in new[] { "MenungguProses", "SedangDiproses", "Pengeringan", "Penyetrikaan", "QualityCheck", "SiapDiambil" })
            await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = tahap });

        // DP tunai 20rb → belum lunas → penyerahan HARUS ditolak.
        await PostJsonAsync(http, admin, "/api/payments/cash",
            new { orderId = id, amount = 20_000m, kind = "Dp", note = (string?)null });
        var tolak = await http.SendAsync(New(HttpMethod.Post, $"/api/orders/{id}/transition", admin,
            new { to = "Selesai" }));
        Assert.Equal(HttpStatusCode.BadRequest, tolak.StatusCode);

        await PostJsonAsync(http, admin, "/api/payments/cash",
            new { orderId = id, amount = 10_000m, kind = "Pelunasan", note = (string?)null });
        Assert.Equal("Lunas", await GetPaymentStatusAsync(http, admin, id));

        var selesai = await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "Selesai" });
        Assert.Equal("Selesai", selesai.GetProperty("status").GetString());
        Assert.Equal(0m, selesai.GetProperty("remaining").GetDecimal());
    }

    /// <summary>
    /// Beranda admin adalah layar pertama sesudah masuk dan seluruh angkanya dihitung di SQL.
    /// Tanpa tes ini, ekspresi domain yang tidak bisa diterjemahkan EF Core baru terlihat saat UI.
    /// </summary>
    [Fact]
    public async Task DashboardAdminDapatDihitungServer()
    {
        var (http, admin, service, customer) = await PrepareOrderAsync();
        var order = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(),
            customer.GetProperty("id").GetGuid(), 2m);
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });

        var d = await GetAsync(http, admin, "/api/orders/dashboard/admin");
        Assert.True(d.GetProperty("dalamProses").GetInt32() >= 1);
        Assert.True(d.GetProperty("belumLunas").GetInt32() >= 1);
    }

    private static HttpRequestMessage New(HttpMethod m, string url, ApiFactory.Tokens t, object body)
    {
        var req = new HttpRequestMessage(m, url) { Content = JsonContent.Create(body) };
        Bearer(req, t);
        return req;
    }

    private static async Task<JsonElement> GetAsync(HttpClient http, ApiFactory.Tokens t, string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        Bearer(req, t);
        var res = await http.SendAsync(req);
        if (!res.IsSuccessStatusCode) throw new Exception($"HTTP {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    private static async Task<string> GetPaymentStatusAsync(HttpClient http, ApiFactory.Tokens t, Guid id)
    {
        var o = await GetAsync(http, t, $"/api/orders/{id}");
        return o.GetProperty("payment").GetString()!;
    }

    [Fact]
    public async Task KelebihanBayarMenjadiKreditDanPemakaianButuhPersetujuan()
    {
        var (http, admin, service, customer) = await PrepareOrderAsync();
        var customerId = customer.GetProperty("id").GetGuid();
        var order = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(), customerId, 2m);
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });

        await PostJsonAsync(http, admin, "/api/payments/cash",
            new { orderId = id, amount = 30_000m, kind = "Pelunasan", note = (string?)null });
        var status = await GetPaymentStatusAsync(http, admin, id);
        Assert.Equal("Lunas", status);

        var saldo = await GetAsync(http, admin, $"/api/payments/credit-balance?customerId={customerId}");
        Assert.Equal(10_000m, saldo.GetProperty("balance").GetDecimal());

        // Order kedua untuk memakai kredit.
        var order2 = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(), customerId, 2m);
        var id2 = order2.GetProperty("id").GetGuid();
        await PostJsonAsync(http, admin, $"/api/orders/{id2}/transition", new { to = "DiterimaDiLaundry" });

        // Tanpa persetujuan → ditolak.
        var reqNoConsent = New(HttpMethod.Post, "/api/payments/credit", admin,
            new { orderId = id2, amount = 5_000m, customerConsent = false });
        var tolak = await http.SendAsync(reqNoConsent);
        Assert.Equal(HttpStatusCode.BadRequest, tolak.StatusCode);

        var Dipakai = await PostJsonAsync(http, admin, "/api/payments/credit",
            new { orderId = id2, amount = 5_000m, customerConsent = true });
        Assert.Equal(5_000m, Dipakai.GetProperty("appliedCredit").GetDecimal());
        Assert.Equal(15_000m, Dipakai.GetProperty("remaining").GetDecimal());

        var saldo2 = await GetAsync(http, admin, $"/api/payments/credit-balance?customerId={customerId}");
        Assert.Equal(5_000m, saldo2.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task PembatalanDiajukanSebelumProsesDisetujuiAdmin()
    {
        var (http, admin, service) = await PrepareServiceAsync();
        var (custTokens, customerId) = await RegisterCustomerWithProfileAsync(http, admin);

        var online = await PostJsonAsync(http, custTokens, "/api/orders/online", new
        {
            items = new[] { new { serviceId = service.GetProperty("id").GetGuid(), estimatedWeightKg = 2m, estimatedItemCount = (decimal?)null } },
            express = false,
            fulfillment = "AmbilSendiri",
        });
        Assert.Equal("MenungguKonfirmasi", online.GetProperty("status").GetString());
        var id = online.GetProperty("id").GetGuid();

        await PostJsonAsync(http, custTokens, $"/api/orders/{id}/cancel-request", new { reason = "Salah pilih layanan" });

        // Admin menolak → status tetap.
        var tolak = await PostJsonAsync(http, admin, $"/api/orders/{id}/cancel-decision",
            new { approve = false, note = (string?)null });
        Assert.NotEqual("Dibatalkan", tolak.GetProperty("status").GetString());

        await PostJsonAsync(http, custTokens, $"/api/orders/{id}/cancel-request", new { reason = "Jadi batal" });
        var setujui = await PostJsonAsync(http, admin, $"/api/orders/{id}/cancel-decision",
            new { approve = true, note = "Disetujui" });
        Assert.Equal("Dibatalkan", setujui.GetProperty("status").GetString());
        _ = customerId;
    }

    [Fact]
    public async Task PembatalanDitolakSistemSetelahMulaiDiproses()
    {
        var (http, admin, service) = await PrepareServiceAsync();
        var (custTokens, customerId) = await RegisterCustomerWithProfileAsync(http, admin);
        var order = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(), customerId, 2m);
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });

        var res = await http.SendAsync(New(HttpMethod.Post, $"/api/orders/{id}/cancel-request",
            custTokens, new { reason = "ma batal" }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task RefundParsialLaluPenuhMenurunkanPembayaran()
    {
        var (http, admin, service, customer) = await PrepareOrderAsync();
        var order = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(),
            customer.GetProperty("id").GetGuid(), 2m);
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });
        await PostJsonAsync(http, admin, "/api/payments/cash",
            new { orderId = id, amount = 20_000m, kind = "Pelunasan", note = (string?)null });
        Assert.Equal("Lunas", await GetPaymentStatusAsync(http, admin, id));

        var kunci = Guid.NewGuid().ToString("N");
        var r1 = await PostJsonAsync(http, admin, "/api/payments/refund",
            new { orderId = id, amount = 5_000m, reason = "Salah hitung", idempotencyKey = kunci });
        // Kunci sama → refund yang sama, bukan refund baru.
        var r1Ulang = await PostJsonAsync(http, admin, "/api/payments/refund",
            new { orderId = id, amount = 5_000m, reason = "Salah hitung", idempotencyKey = kunci });
        Assert.Equal(r1.GetProperty("id").GetGuid(), r1Ulang.GetProperty("id").GetGuid());

        var sebagian = await GetAsync(http, admin, $"/api/orders/{id}");
        Assert.Equal("RefundSebagian", sebagian.GetProperty("payment").GetString());
        Assert.Equal(5_000m, sebagian.GetProperty("refunded").GetDecimal());
        Assert.Equal(15_000m, sebagian.GetProperty("paid").GetDecimal());
        Assert.Equal(5_000m, sebagian.GetProperty("remaining").GetDecimal());

        // Melebihi sisa pembayaran asal → ditolak.
        var berlebih = await http.SendAsync(New(HttpMethod.Post, "/api/payments/refund", admin,
            new { orderId = id, amount = 25_000m, reason = "mau lebih" }));
        Assert.Equal(HttpStatusCode.BadRequest, berlebih.StatusCode);

        await PostJsonAsync(http, admin, "/api/payments/refund",
            new { orderId = id, amount = 15_000m, reason = "Pesanan ditarik" });
        var penuh = await GetAsync(http, admin, $"/api/orders/{id}");
        Assert.Equal("RefundPenuh", penuh.GetProperty("payment").GetString());
        Assert.Equal(20_000m, penuh.GetProperty("refunded").GetDecimal());
        Assert.Equal(0m, penuh.GetProperty("paid").GetDecimal());

        // Tanpa alasan → ditolak.
        var tanpaAlasan = await http.SendAsync(New(HttpMethod.Post, "/api/payments/refund", admin,
            new { orderId = id, amount = 1_000m, reason = "  " }));
        Assert.Equal(HttpStatusCode.BadRequest, tanpaAlasan.StatusCode);
    }

    [Fact]
    public async Task RefundMenarikKembaliKelebihanBayarYangMasihJadiSaldo()
    {
        var (http, admin, service, customer) = await PrepareOrderAsync();
        var customerId = customer.GetProperty("id").GetGuid();
        var order = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(), customerId, 2m);
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });
        await PostJsonAsync(http, admin, "/api/payments/cash",
            new { orderId = id, amount = 30_000m, kind = "Pelunasan", note = (string?)null });

        var saldoAwal = await GetAsync(http, admin, $"/api/payments/credit-balance?customerId={customerId}");
        Assert.Equal(10_000m, saldoAwal.GetProperty("balance").GetDecimal());

        await PostJsonAsync(http, admin, "/api/payments/refund",
            new { orderId = id, amount = 10_000m, reason = "Pembayaran ditarik" });

        // Uang pelanggan kembali sekali: kredit 10rb ditarik, kas 10rb keluar.
        var saldoAkhir = await GetAsync(http, admin, $"/api/payments/credit-balance?customerId={customerId}");
        Assert.Equal(0m, saldoAkhir.GetProperty("balance").GetDecimal());
        var detail = await GetAsync(http, admin, $"/api/orders/{id}");
        Assert.Equal(20_000m, detail.GetProperty("paid").GetDecimal());
        Assert.Equal(0m, detail.GetProperty("remaining").GetDecimal());
    }

    [Fact]
    public async Task StrukDisimpanSekaliDanHasilCetakUlangIdentik()
    {
        var (http, admin, service, customer) = await PrepareOrderAsync();
        var order = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(),
            customer.GetProperty("id").GetGuid(), 2m);
        var id = order.GetProperty("id").GetGuid();
        await PostJsonAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });
        await PostJsonAsync(http, admin, "/api/payments/cash",
            new { orderId = id, amount = 20_000m, kind = "Pelunasan", note = (string?)null });

        var pertama = await GetAsync(http, admin, $"/api/orders/{id}/receipt");
        Assert.Equal(order.GetProperty("orderNumber").GetString(), pertama.GetProperty("orderNumber").GetString());
        Assert.Equal(20_000m, pertama.GetProperty("finalTotal").GetDecimal());
        Assert.Equal(1, pertama.GetProperty("lines").GetArrayLength());
        Assert.Equal(2m, pertama.GetProperty("lines")[0].GetProperty("jumlah").GetDecimal());
        Assert.Equal("kg", pertama.GetProperty("lines")[0].GetProperty("satuan").GetString());

        // Perubahan uang setelah tercetak tidak mengubah struk yang sudah disimpan.
        await PostJsonAsync(http, admin, "/api/payments/refund",
            new { orderId = id, amount = 2_000m, reason = "Koreksi kas" });
        var kedua = await GetAsync(http, admin, $"/api/orders/{id}/receipt");
        Assert.Equal(pertama.GetProperty("generatedAtUtc").GetString(), kedua.GetProperty("generatedAtUtc").GetString());
        Assert.Equal(0m, kedua.GetProperty("refunded").GetDecimal());
    }

    [Fact]
    public async Task PelangganATidakBisaAksesOrderPelangganB()
    {
        var (http, admin, service, customerB) = await PrepareOrderAsync();
        var orderB = await CreateOrderAsync(http, admin, service.GetProperty("id").GetGuid(),
            customerB.GetProperty("id").GetGuid(), 2m);
        var idB = orderB.GetProperty("id").GetGuid();

        // Pelanggan A: registrasi OTP penuh lewat API.
        var phoneA = ApiFactory.RandomPhone();
        var start = await http.PostAsJsonAsync("/api/auth/register/start", new { phone = phoneA });
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(J);
        var regA = await http.PostAsJsonAsync("/api/auth/register/complete", new
        {
            challengeId = started.GetProperty("challengeId").GetGuid(),
            otp = started.GetProperty("devCode").GetString(),
            password = "PasswordA!123",
            nama = "Pelanggan A",
        });
        regA.EnsureSuccessStatusCode();
        var tokensA = (await regA.Content.ReadFromJsonAsync<ApiFactory.Tokens>(J))!;

        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{idB}");
        Bearer(req, tokensA);
        var res = await http.SendAsync(req);
        // 404, bukan 403 — keberadaan data pelanggan lain tidak dibocorkan (IDOR).
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task AlamatPelangganIkutTersimpanSaatDibuatLewatApi()
    {
        var (http, admin) = await SetupAsync();
        var dibuat = await PostJsonAsync(http, admin, "/api/customers", new
        {
            nama = "Pelanggan Beralamat", phone = ApiFactory.RandomPhone(), alamat = "Jalan Mawar 12",
        });
        Assert.Equal("Jalan Mawar 12", dibuat.GetProperty("alamat").GetString());

        var dibaca = await GetAsync(http, admin, $"/api/customers/{dibuat.GetProperty("id").GetGuid()}");
        Assert.Equal("Jalan Mawar 12", dibaca.GetProperty("alamat").GetString());
    }
}
