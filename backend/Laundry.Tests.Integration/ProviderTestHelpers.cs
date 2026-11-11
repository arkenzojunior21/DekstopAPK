using System.Text;
using System.Text.Json;
using Laundry.Application.Abstractions;
using Laundry.Infrastructure.Payments;

namespace Laundry.Tests.Integration;

/// <summary>
/// Kontrak webhook lokal: body JSON + HMAC atas body mentah di header X-Mock-Signature.
/// Secret hanya dipakai tes; tidak ada kredensial provider sungguhan di repo ini.
/// </summary>
internal static class UjiWebhook
{
    public const string Secret = "rahasia-webhook-tes-integrasi";
    public const string Header = "X-Mock-Signature";

    public static MockPaymentGateway Gateway => new(Secret);

    public static string Body(Guid paymentId, decimal amount, bool paid = true, string? reference = null) =>
        JsonSerializer.Serialize(new
        {
            paymentId = paymentId.ToString("N"),
            reference,
            amount,
            status = paid ? "paid" : "failed",
            occurredAt = DateTime.UtcNow.ToString("O"),
            failureReason = paid ? null : "ditolak dalam simulasi",
        });

    public static HttpRequestMessage Request(string url, string body, bool sign = true, string? secret = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (sign) req.Headers.Add(Header, MockPaymentGateway.Sign(secret ?? Secret, body));
        return req;
    }
}

/// <summary>Adapter WhatsApp tiruan untuk menguji antrean: bisa dipaksa gagal per nomor.</summary>
internal sealed class PengirimPalsu : IWhatsAppSender
{
    private readonly HashSet<string> _nomorGagal;

    public PengirimPalsu(params string[] nomorGagal) => _nomorGagal = nomorGagal.ToHashSet();

    public string Name => "palsu";
    public bool IsLive => false;
    public int Panggilan { get; private set; }

    public Task<string> SendAsync(string templateKey, string targetPhone, string payloadJson,
        CancellationToken ct = default)
    {
        Panggilan++;
        if (_nomorGagal.Contains(targetPhone)) throw new InvalidOperationException("provider menolak permintaan");
        return Task.FromResult($"palsu-{Panggilan}");
    }
}

internal sealed class PengirimBelumTerhubung : IWhatsAppSender
{
    public string Name => "palsu-tanpa-credential";
    public bool IsLive => false;

    public Task<string> SendAsync(string templateKey, string targetPhone, string payloadJson,
        CancellationToken ct = default) =>
        throw new ProviderUnavailableException("Credential provider belum dipasang.");
}
