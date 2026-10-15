using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Laundry.Infrastructure.Payments;

namespace Laundry.Tests.Integration;

/// <summary>
/// Uji adapter payment gateway lewat API: tanda tangan webhook, idempotensi, dan
/// penolakan nominal yang tidak cocok. Semua pembayaran di sini tetap simulasi.
/// </summary>
[Collection("api")]
public class ProviderApiTests
{
    private readonly ApiFactory _factory;
    public ProviderApiTests(ApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);
    private const string AdminPhone = "62812000000001";
    private const string AdminPassword = "Adm1nDev!2026";
    private const string WebhookUrl = "/api/payments/gateway/webhook";

    private record Tagihan(HttpClient Http, ApiFactory.Tokens Admin, Guid OrderId, Guid PaymentId,
        string Reference, decimal Total, JsonElement Quote);

    private async Task<ApiFactory.Tokens> AdminAsync(HttpClient http)
    {
        await http.PostAsJsonAsync("/api/auth/bootstrap", new { phone = AdminPhone, password = AdminPassword });
        var login = await http.PostAsJsonAsync("/api/auth/login", new { phone = AdminPhone, password = AdminPassword });
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<ApiFactory.Tokens>(J))!;
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

    private static async Task<JsonElement> GetAsync(HttpClient http, ApiFactory.Tokens t, string url)
    {
        var res = await http.SendAsync(ApiFactory.Bearer(HttpMethod.Get, url, t));
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(J);
    }

    /// <summary>Pesanan sudah ditimbang agar total final pasti: 3 kg x Rp10.000 = Rp30.000.</summary>
    private async Task<(HttpClient http, ApiFactory.Tokens admin, Guid orderId, decimal total)> PesananBeratAsync()
    {
        var http = _factory.Client();
        var admin = await AdminAsync(http);
        var nama = $"Kilo Gateway {Guid.NewGuid():N}"[..28];
        var layanan = await PostAsync(http, admin, "/api/catalog", new
        {
            nama, unitType = "PerKg", minimumWeightKg = (decimal?)1m, price = 10_000m,
            standardDurationDays = 3, expressDurationDays = 1, expressSurcharge = 5_000m, isActive = true,
        });
        var pelanggan = await PostAsync(http, admin, "/api/customers",
            new { nama = "Pelanggan Gateway", phone = ApiFactory.RandomPhone() });
        var order = await PostAsync(http, admin, "/api/orders", new
        {
            customerId = pelanggan.GetProperty("id").GetGuid(),
            items = new[]
            {
                new
                {
                    serviceId = layanan.GetProperty("id").GetGuid(),
                    estimatedWeightKg = 3m,
                    estimatedItemCount = (decimal?)null,
                },
            },
            express = false,
            fulfillment = "AmbilSendiri",
        });
        var id = order.GetProperty("id").GetGuid();
        var itemId = order.GetProperty("items")[0].GetProperty("id").GetGuid();
        await PostAsync(http, admin, $"/api/orders/{id}/transition", new { to = "DiterimaDiLaundry" });
        var timbang = await PostAsync(http, admin, $"/api/orders/{id}/actuals",
            new { actuals = new[] { new { orderItemId = itemId, actualWeightKg = 3m, actualItemCount = (decimal?)null } } });
        return (http, admin, id, timbang.GetProperty("finalTotal").GetDecimal());
    }

    private async Task<Tagihan> TagihanAsync()
    {
        var (http, admin, orderId, total) = await PesananBeratAsync();
        var quote = await PostAsync(http, admin, "/api/payments/gateway/quote",
            new { orderId, kind = "Pelunasan" });
        return new Tagihan(http, admin, orderId, quote.GetProperty("paymentId").GetGuid(),
            quote.GetProperty("reference").GetString()!, total, quote);
    }

    private static async Task<(HttpStatusCode kode, JsonElement isi)> ResponsAsync(Tagihan t, string body,
        bool sign = true, string? secret = null)
    {
        var res = await t.Http.SendAsync(UjiWebhook.Request(WebhookUrl, body, sign, secret));
        return (res.StatusCode, await res.Content.ReadFromJsonAsync<JsonElement>(J));
    }

    private async Task<string> StatusPembayaranAsync(Tagihan t)
    {
        var pembayaran = await GetAsync(t.Http, t.Admin, $"/api/orders/{t.OrderId}/payments");
        return pembayaran.EnumerateArray()
            .First(p => p.GetProperty("method").GetString() == "Qris")
            .GetProperty("status").GetString()!;
    }

    [Fact]
    public async Task WebhookTersignasi_MembayarSekaliDanWebhookUlangTidakMenambahPembayaran()
    {
        var t = await TagihanAsync();
        Assert.False(t.Quote.GetProperty("live").GetBoolean());
        Assert.Equal("mock-gateway", t.Quote.GetProperty("gatewayName").GetString());
        Assert.Equal(t.Total, t.Quote.GetProperty("amount").GetDecimal());
        Assert.Contains("qris-simulasi", t.Quote.GetProperty("instructionsJson").GetString()!);

        var isi = UjiWebhook.Body(t.PaymentId, t.Total, true, t.Reference);
        var (pertama, body1) = await ResponsAsync(t, isi);
        Assert.Equal(HttpStatusCode.OK, pertama);
        Assert.Equal("Disimpan", body1.GetProperty("status").GetString());

        var (kedua, body2) = await ResponsAsync(t, isi);
        Assert.Equal(HttpStatusCode.OK, kedua);
        Assert.Equal("Duplikat", body2.GetProperty("status").GetString());

        var order = await GetAsync(t.Http, t.Admin, $"/api/orders/{t.OrderId}");
        Assert.Equal("Lunas", order.GetProperty("payment").GetString());
        Assert.Equal(t.Total, order.GetProperty("paid").GetDecimal());
        Assert.Equal(0m, order.GetProperty("remaining").GetDecimal());
        var pembayaran = await GetAsync(t.Http, t.Admin, $"/api/orders/{t.OrderId}/payments");
        Assert.Equal(1, pembayaran.EnumerateArray().Count(p => p.GetProperty("method").GetString() == "Qris"));
    }

    [Fact]
    public async Task WebhookNominalBeda_TidakMenandaiLunas()
    {
        var t = await TagihanAsync();
        var (kode, body) = await ResponsAsync(t, UjiWebhook.Body(t.PaymentId, 1_000m, true, t.Reference));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, kode);
        Assert.Equal("Ditolak", body.GetProperty("status").GetString());
        Assert.Equal("MenungguKonfirmasi", await StatusPembayaranAsync(t));

        // Setelah penolakan, tagihan tetap bisa dibayar dengan nominal yang benar.
        var (kode2, body2) = await ResponsAsync(t, UjiWebhook.Body(t.PaymentId, t.Total, true, t.Reference));
        Assert.Equal(HttpStatusCode.OK, kode2);
        Assert.Equal("Disimpan", body2.GetProperty("status").GetString());
    }

    [Fact]
    public async Task WebhookTanpaTandaTanganAtauBodyDiubah_DitolakTanpaMengubahPembayaran()
    {
        var t = await TagihanAsync();
        var isi = UjiWebhook.Body(t.PaymentId, t.Total, true, t.Reference);

        var tanpaHeader = await t.Http.SendAsync(UjiWebhook.Request(WebhookUrl, isi, sign: false));
        Assert.Equal(HttpStatusCode.Unauthorized, tanpaHeader.StatusCode);

        var secretSalah = await t.Http.SendAsync(
            UjiWebhook.Request(WebhookUrl, isi, secret: "secret-lain-yang-juga-cukup-panjang"));
        Assert.Equal(HttpStatusCode.Unauthorized, secretSalah.StatusCode);

        // Tanda tangan mengikat body mentah: isi yang diganti setelah ditandatangani tidak lolos.
        var direkayasa = UjiWebhook.Request(WebhookUrl, UjiWebhook.Body(t.PaymentId, 1m, true, t.Reference));
        direkayasa.Headers.Remove(UjiWebhook.Header);
        direkayasa.Headers.Add(UjiWebhook.Header, MockPaymentGateway.Sign(UjiWebhook.Secret, isi));
        var hasil = await t.Http.SendAsync(direkayasa);
        Assert.Equal(HttpStatusCode.Unauthorized, hasil.StatusCode);

        Assert.Equal("MenungguKonfirmasi", await StatusPembayaranAsync(t));
    }

    [Fact]
    public async Task WebhookGagal_MenutupTagihanDanTidakBisaDibukaLagi()
    {
        var t = await TagihanAsync();
        var (kode, body) = await ResponsAsync(t, UjiWebhook.Body(t.PaymentId, t.Total, false, t.Reference));
        Assert.Equal(HttpStatusCode.OK, kode);
        Assert.Equal("Gagal", body.GetProperty("status").GetString());
        Assert.Equal("GagalAtauKedaluwarsa", await StatusPembayaranAsync(t));

        var (kode2, body2) = await ResponsAsync(t, UjiWebhook.Body(t.PaymentId, t.Total, true, t.Reference));
        Assert.Equal(HttpStatusCode.OK, kode2);
        Assert.Equal("Duplikat", body2.GetProperty("status").GetString());
        Assert.Equal("GagalAtauKedaluwarsa", await StatusPembayaranAsync(t));
    }

    [Fact]
    public async Task TagihanKeduaSetelahLunas_Ditolak()
    {
        var t = await TagihanAsync();
        await ResponsAsync(t, UjiWebhook.Body(t.PaymentId, t.Total, true, t.Reference));

        var req = ApiFactory.Bearer(HttpMethod.Post, "/api/payments/gateway/quote", t.Admin);
        req.Content = JsonContent.Create(new { orderId = t.OrderId, kind = "Pelunasan" });
        var res = await t.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task AntreanNotifikasiTerisiLaluTerkirimDanTidakDikirimUlang()
    {
        var t = await TagihanAsync();

        var sebelum = await GetAsync(t.Http, t.Admin, "/api/providers/status");
        Assert.True(sebelum.GetProperty("notifikasi").GetProperty("menunggu").GetInt32() >= 1);
        var adapter = sebelum.GetProperty("adapter").EnumerateArray().ToList();
        Assert.Equal(2, adapter.Count);
        Assert.All(adapter, a => Assert.False(a.GetProperty("live").GetBoolean()));

        // Batch per siklus terbatas, jadi antrean dihabiskan beberapa siklus sekaligus.
        var terkirimTotal = 0;
        var habis = false;
        for (var i = 0; i < 20 && !habis; i++)
        {
            var drain = await PostAsync(t.Http, t.Admin, "/api/providers/notifications/drain", new { });
            Assert.Equal(0, drain.GetProperty("gagal").GetInt32());
            terkirimTotal += drain.GetProperty("terkirim").GetInt32();
            var sesudah = await GetAsync(t.Http, t.Admin, "/api/providers/status");
            habis = sesudah.GetProperty("notifikasi").GetProperty("menunggu").GetInt32() == 0;
        }

        Assert.True(habis, "Antrean notifikasi tidak habis setelah 20 siklus.");
        Assert.True(terkirimTotal >= 1);
        var drainLagi = await PostAsync(t.Http, t.Admin, "/api/providers/notifications/drain", new { });
        Assert.Equal(0, drainLagi.GetProperty("diproses").GetInt32());
    }

    [Fact]
    public void SecretBelumDiisi_SemuaWebhookDitolak()
    {
        var gateway = new MockPaymentGateway(string.Empty);
        Assert.False(gateway.Terkonfigurasi);
        Assert.False(gateway.VerifySignature("{}", MockPaymentGateway.Sign(UjiWebhook.Secret, "{}")));
        Assert.Null(gateway.ParseUpdate("bukan json"));
        Assert.Null(gateway.ParseUpdate("""{"paymentId":"bukan-guid","amount":1,"status":"paid"}"""));
    }
}
