using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Laundry.Desktop.Services;

/// <summary>Error dari API yang pesannya sudah dibaca dan siap ditampilkan apa adanya ke pengguna.</summary>
public sealed class ApiException(int statusCode, string pesan) : Exception(pesan)
{
    public int StatusCode { get; } = statusCode;

    public bool TidakTerkoneksi => StatusCode == 0;
}

public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, Guid UserId);

public sealed record HasilUnduhan(string NamaBerkas, string TipeMime, byte[] Isi);

/// <summary>
/// Satu-satunya pintu keluar aplikasi desktop: semuanya lewat HTTP ke Laundry.Api, tidak ada jalur
/// database langsung. Token kedaluwarsa disegarkan sekali diam-diam; kalau masih ditolak, sesi
/// diakhiri dan pengguna disuruh masuk lagi — bukan permintaan diulang tanpa batas.
/// </summary>
public sealed class KlienApi
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _kunciSegar = new(1, 1);
    private string? _tokenAkses;
    private string? _tokenSegar;

    public KlienApi(string basisUrl) => BasisUrl = basisUrl.TrimEnd('/');

    public string BasisUrl { get; private set; }

    public bool AdaSesi => !string.IsNullOrEmpty(_tokenAkses);

    /// <summary>Dipanggil saat sesi berakhir (401 yang tidak bisa disegarkan) supaya UI kembali ke login.</summary>
    public event Action? SesiBerakhir;

    public void GantiServer(string basisUrl) => BasisUrl = basisUrl.TrimEnd('/');

    public void SimpanToken(string akses, string segar)
    {
        _tokenAkses = akses;
        _tokenSegar = segar;
    }

    public void KosongkanToken()
    {
        _tokenAkses = null;
        _tokenSegar = null;
    }

    /// <summary>Cek koneksi ke server tanpa perlu token; dipakai di layar login.</summary>
    public async Task<string> CekServerAsync(CancellationToken ct = default)
    {
        try
        {
            using var respons = await KirimSekaliAsync("health", HttpMethod.Get, null, ct, false);
            return respons.IsSuccessStatusCode
                ? $"Terhubung ke {BasisUrl}"
                : $"Server menjawab {(int)respons.StatusCode} dari {BasisUrl}";
        }
        catch (ApiException ex)
        {
            return ex.Message;
        }
    }

    public async Task<T?> AmbilAsync<T>(string path, CancellationToken ct = default)
    {
        using var respons = await KirimAsync(path, HttpMethod.Get, null, ct, true, bolehSegar: true);
        return await BacaAsync<T>(respons, ct);
    }

    public async Task<T> IsiAsync<T>(string path, CancellationToken ct = default) =>
        await AmbilAsync<T>(path, ct) ?? throw new ApiException(0, "Server mengirim jawaban kosong.");

    public Task AmbilAsync(string path, CancellationToken ct = default) =>
        BuangAsync(KirimAsync(path, HttpMethod.Get, null, ct, true));

    public async Task<T> KirimAsync<T>(string path, object? body, CancellationToken ct = default)
    {
        using var respons = await KirimAsync(path, HttpMethod.Post, Serialisasi(body), ct, true);
        return await BacaAsync<T>(respons, ct) ?? default!;
    }

    public Task KirimAsync(string path, object? body, CancellationToken ct = default) =>
        BuangAsync(KirimAsync(path, HttpMethod.Post, Serialisasi(body), ct, true));

    public async Task<T> GantiAsync<T>(string path, object? body, CancellationToken ct = default)
    {
        using var respons = await KirimAsync(path, HttpMethod.Put, Serialisasi(body), ct, true);
        return await BacaAsync<T>(respons, ct) ?? default!;
    }

    public Task GantiAsync(string path, object? body, CancellationToken ct = default) =>
        BuangAsync(KirimAsync(path, HttpMethod.Put, Serialisasi(body), ct, true));

    /// <summary>Unduh berkas ekspor; nama berkas diambil dari Content-Disposition bila ada.</summary>
    public async Task<HasilUnduhan> UnduhAsync(string path, CancellationToken ct = default)
    {
        using var respons = await KirimAsync(path, HttpMethod.Get, null, ct, true);
        var isi = await respons.Content.ReadAsByteArrayAsync(ct);
        var mime = respons.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new HasilUnduhan(NamaBerkas(respons), mime, isi);
    }

    private static byte[]? Serialisasi(object? body) => body is null
        ? null
        : JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Json);

    private static string NamaBerkas(HttpResponseMessage respons)
    {
        var header = respons.Content.Headers.TryGetValues("Content-Disposition", out var nilai)
            ? string.Join(';', nilai)
            : null;
        if (string.IsNullOrEmpty(header)) return "";
        foreach (var bagian in header.Split(';'))
        {
            var teks = bagian.Trim();
            if (teks.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                return teks["filename=".Length..].Trim('"');
        }

        return "";
    }

    private static async Task BuangAsync(Task<HttpResponseMessage> tugas)
    {
        using var _ = await tugas;
    }

    private async Task<HttpResponseMessage> KirimAsync(string path, HttpMethod metode, byte[]? badan,
        CancellationToken ct, bool pakaiToken, bool bolehSegar = false)
    {
        var respons = await KirimSekaliAsync(path, metode, badan, ct, pakaiToken);
        if (bolehSegar && respons.StatusCode == HttpStatusCode.Unauthorized && await SegarkanTokenAsync(ct))
        {
            respons.Dispose();
            respons = await KirimSekaliAsync(path, metode, badan, ct, pakaiToken);
        }

        if (respons.IsSuccessStatusCode) return respons;

        var kode = (int)respons.StatusCode;
        var pesan = await PesanAsync(respons, ct);
        respons.Dispose();
        if (kode == 401)
        {
            KosongkanToken();
            SesiBerakhir?.Invoke();
        }

        throw new ApiException(kode, pesan);
    }

    private async Task<HttpResponseMessage> KirimSekaliAsync(string path, HttpMethod metode, byte[]? badan,
        CancellationToken ct, bool pakaiToken)
    {
        try
        {
            using var permintaan = new HttpRequestMessage(metode, $"{BasisUrl}/{path.TrimStart('/')}");
            if (badan is not null)
                permintaan.Content = new ByteArrayContent(badan)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") },
                };
            if (pakaiToken && !string.IsNullOrEmpty(_tokenAkses))
                permintaan.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _tokenAkses);
            return await _http.SendAsync(permintaan, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(0, $"Server di {BasisUrl} tidak menjawab dalam 30 detik.");
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(0, $"Tidak bisa menghubungi {BasisUrl}: {ex.Message}");
        }
    }

    private async Task<bool> SegarkanTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_tokenSegar)) return false;
        await _kunciSegar.WaitAsync(ct);
        try
        {
            using var respons = await KirimSekaliAsync("api/auth/refresh", HttpMethod.Post,
                Serialisasi(new { refreshToken = _tokenSegar }), ct, false);
            if (!respons.IsSuccessStatusCode) return false;
            var hasil = await BacaAsync<TokenResponse>(respons, ct);
            if (hasil is null) return false;
            SimpanToken(hasil.AccessToken, hasil.RefreshToken);
            return true;
        }
        finally
        {
            _kunciSegar.Release();
        }
    }

    private static async Task<string> PesanAsync(HttpResponseMessage respons, CancellationToken ct)
    {
        var teks = await respons.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(teks))
        {
            try
            {
                var error = JsonSerializer.Deserialize<ErrorJawaban>(teks, Json)?.Error;
                if (!string.IsNullOrWhiteSpace(error)) return error;
            }
            catch (JsonException)
            {
                // badan jawaban bukan JSON (mis. halaman error Kestrel); tampilkan statusnya saja.
            }
        }

        return respons.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Sesi berakhir. Silakan masuk lagi.",
            HttpStatusCode.Forbidden => "Akun ini tidak punya hak untuk aksi tersebut.",
            HttpStatusCode.NotFound => "Data yang diminta tidak ditemukan.",
            HttpStatusCode.TooManyRequests => "Terlalu banyak percobaan. Tunggu sebentar lalu coba lagi.",
            _ => $"Server membalas {(int)respons.StatusCode}: {Potong(teks)}",
        };
    }

    private static string Potong(string teks) => teks.Length <= 200 ? teks : teks[..200] + "…";

    private static async Task<T?> BacaAsync<T>(HttpResponseMessage respons, CancellationToken ct)
    {
        var teks = await respons.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(teks)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(teks, Json);
        }
        catch (JsonException ex)
        {
            throw new ApiException((int)respons.StatusCode, $"Jawaban server tidak bisa dibaca: {ex.Message}");
        }
    }

    private sealed record ErrorJawaban(string? Error);
}
