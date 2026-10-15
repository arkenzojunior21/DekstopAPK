using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using static Laundry.Tests.Integration.ApiFactory;

namespace Laundry.Tests.Integration;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Folder backup sementara: tes backup tidak pernah menulis ke folder aplikasi asli.</summary>
    public string FolderBackup { get; } =
        Path.Combine(Path.GetTempPath(), $"laundry-backup-tes-{Guid.NewGuid():N}");

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("AUTH_RATE_LIMIT_PER_MENIT", "100000");
        // Secret webhook tes diset sebelum host dibangun: adapter membacanya sekali saat DI dibuat.
        Environment.SetEnvironmentVariable("WEBHOOK_SECRET", UjiWebhook.Secret);
        // Pekerja backup dimatikan dan arahnya dipindah ke folder sementara (di-overwrite per tes
        // kalau perlu) supaya host tes tidak mulai menumpuk berkas di luar kendali.
        Environment.SetEnvironmentVariable("BACKUP_AUTO", "0");
        Environment.SetEnvironmentVariable("BACKUP_DIR", FolderBackup);
        Environment.SetEnvironmentVariable("UPDATE_CHECK_URL", string.Empty);
        Environment.SetEnvironmentVariable("UPDATE_DIR", FolderBackup);
        Directory.CreateDirectory(FolderBackup);
    }

    public static string RandomPhone()
    {
        Span<char> digits = stackalloc char[9];
        for (var i = 0; i < 9; i++) digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        return "628" + new string(digits);
    }

    public HttpClient Client() => CreateClient();

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public record Tokens(string accessToken, string refreshToken, int expiresIn);

    /// <summary>Registrasi pelanggan baru lewat OTP dev; mengembalikan access token.</summary>
    public async Task<Tokens> RegisterCustomerAsync(HttpClient http, string? password = null)
    {
        password ??= "PasswordTes!2026";
        var phone = RandomPhone();
        var start = await http.PostAsJsonAsync("/api/auth/register/start", new { phone });
        start.EnsureSuccessStatusCode();
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(Json);
        var complete = await http.PostAsJsonAsync("/api/auth/register/complete", new
        {
            challengeId = started.GetProperty("challengeId").GetGuid(),
            otp = started.GetProperty("devCode").GetString(),
            password = password,
            nama = "Pelanggan Integration Test",
        });
        complete.EnsureSuccessStatusCode();
        return (await complete.Content.ReadFromJsonAsync<Tokens>(Json))!;
    }

    public static HttpRequestMessage Bearer(HttpMethod method, string url, Tokens tokens)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.accessToken);
        return req;
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory> { }

[Collection("api")]
public class AuthApiTests
{
    private readonly ApiFactory _factory;
    public AuthApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task RegistrasiLoginRefreshLogoutBerjalan()
    {
        var http = _factory.Client();
        var tokens = await _factory.RegisterCustomerAsync(http);

        var me = await http.SendAsync(ApiFactory.Bearer(HttpMethod.Get, "/api/auth/me", tokens));
        me.EnsureSuccessStatusCode();
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal("Pelanggan", body.GetProperty("role").GetString());
        Assert.False(body.GetProperty("isAdmin").GetBoolean());

        // Rotasi refresh token: token lama single-use.
        var refresh = await http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.refreshToken });
        refresh.EnsureSuccessStatusCode();
        var baru = (await refresh.Content.ReadFromJsonAsync<ApiFactory.Tokens>(ApiFactory.Json))!;

        var replayLama = await http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replayLama.StatusCode);

        var logout = await http.PostAsJsonAsync("/api/auth/logout", new { refreshToken = baru.refreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var setelahLogout = await http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = baru.refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, setelahLogout.StatusCode);
    }

    [Fact]
    public async Task LoginGagalLimaKaliMengunciakunLimaBelasMenit()
    {
        var http = _factory.Client();
        var phone = RandomPhone();
        var start = await http.PostAsJsonAsync("/api/auth/register/start", new { phone });
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        await http.PostAsJsonAsync("/api/auth/register/complete", new
        {
            challengeId = started.GetProperty("challengeId").GetGuid(),
            otp = started.GetProperty("devCode").GetString(),
            password = "PasswordBenar123",
            nama = "Kunci Tes",
        });

        for (var i = 0; i < 5; i++)
        {
            var gagal = await http.PostAsJsonAsync("/api/auth/login", new { phone, password = "salah-kode" });
            Assert.Equal(HttpStatusCode.Unauthorized, gagal.StatusCode);
        }

        // Meskipun password benar, selama lockout belum selesai harus ditolak.
        var terkunci = await http.PostAsJsonAsync("/api/auth/login", new { phone, password = "PasswordBenar123" });
        Assert.Equal(HttpStatusCode.Unauthorized, terkunci.StatusCode);
        var msg = await terkunci.Content.ReadAsStringAsync();
        Assert.Contains("dikunci", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OtpTidakBisaDipakaiDuaKali()
    {
        var http = _factory.Client();
        var phone1 = RandomPhone();
        var start = await http.PostAsJsonAsync("/api/auth/register/start", new { phone = phone1 });
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var challengeId = started.GetProperty("challengeId").GetGuid();
        var otp = started.GetProperty("devCode").GetString();

        var ok = await http.PostAsJsonAsync("/api/auth/register/complete", new
        {
            challengeId,
            otp,
            password = "PasswordBaru123",
            nama = "Otp Tes",
        });
        ok.EnsureSuccessStatusCode();

        var pakaiUlang = await http.PostAsJsonAsync("/api/auth/register/complete", new
        {
            challengeId,
            otp,
            password = "PasswordBaru123",
            nama = "Otp Tes Dua",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, pakaiUlang.StatusCode);
    }

    [Fact]
    public async Task PelangganTidakBisaAksesEndpointAdmin()
    {
        var http = _factory.Client();
        var tokens = await _factory.RegisterCustomerAsync(http);

        var forbidden = await http.SendAsync(ApiFactory.Bearer(
            HttpMethod.Get, "/api/auth/admin-only", tokens));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var tanpaToken = await http.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, tanpaToken.StatusCode);
    }

    [Fact]
    public async Task AdminLoginDanRoleTepat()
    {
        var http = _factory.Client();
        // Bootstrap idempoten: 201/200 saat pertama kali, 409 bila admin sudah ada.
        var boot = await http.PostAsJsonAsync("/api/auth/bootstrap",
            new { phone = "62812000000001", password = "Adm1nDev!2026" });
        Assert.True(boot.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);

        var login = await http.PostAsJsonAsync("/api/auth/login",
            new { phone = "62812000000001", password = "Adm1nDev!2026" });
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<ApiFactory.Tokens>(ApiFactory.Json))!;

        var me = await http.SendAsync(ApiFactory.Bearer(HttpMethod.Get, "/api/auth/me", tokens));
        var body = await me.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        Assert.Equal("Admin", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("isAdmin").GetBoolean());

        // Admin boleh akses endpoint admin.
        var adminOnly = await http.SendAsync(ApiFactory.Bearer(
            HttpMethod.Get, "/api/auth/admin-only", tokens));
        adminOnly.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ResetPasswordMenggantiLoginDanMencabutSesiLama()
    {
        var http = _factory.Client();
        var phone = RandomPhone();
        var start = await http.PostAsJsonAsync("/api/auth/register/start", new { phone });
        var started = await start.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var complete = await http.PostAsJsonAsync("/api/auth/register/complete", new
        {
            challengeId = started.GetProperty("challengeId").GetGuid(),
            otp = started.GetProperty("devCode").GetString(),
            password = "LamaPassword123",
            nama = "Reset Tes",
        });
        complete.EnsureSuccessStatusCode();
        var lama = (await complete.Content.ReadFromJsonAsync<ApiFactory.Tokens>(ApiFactory.Json))!;
        var resetStart = await http.PostAsJsonAsync("/api/auth/reset/start", new { phone });
        var resetStarted = await resetStart.Content.ReadFromJsonAsync<JsonElement>(ApiFactory.Json);
        var resetDone = await http.PostAsJsonAsync("/api/auth/reset/complete", new
        {
            challengeId = resetStarted.GetProperty("challengeId").GetGuid(),
            otp = resetStarted.GetProperty("devCode").GetString(),
            newPassword = "BaruPassword456",
        });
        resetDone.EnsureSuccessStatusCode();

        // Sesi lama (refresh token) dicabut setelah reset password.
        var refreshLama = await http.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = lama.refreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshLama.StatusCode);

        // Login dengan password baru berhasil.
        var loginBaru = await http.PostAsJsonAsync("/api/auth/login",
            new { phone, password = "BaruPassword456" });
        loginBaru.EnsureSuccessStatusCode();
    }
}
