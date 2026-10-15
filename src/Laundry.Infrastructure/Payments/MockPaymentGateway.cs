using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Laundry.Application.Abstractions;

namespace Laundry.Infrastructure.Payments;

/// <summary>
/// Adapter payment gateway untuk development: tagihan dibuat dan ditandai lunas hanya lewat
/// webhook yang tanda tangannya cocok. Tidak ada nama vendor, endpoint, atau credential
/// yang dikarang; IsLive selalu false sampai kontrak resmi ada (D-04, PRD 6.13).
/// </summary>
public sealed class MockPaymentGateway(string webhookSecret) : IPaymentGateway
{
    private const string Imbuhan = "sha256=";
    private const int SecretTerpendek = 16;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public string Name => "mock-gateway";
    public bool IsLive => false;
    public string SignatureHeader => "X-Mock-Signature";

    /// <summary>Secret belum diisi (atau masih placeholder) → webhook ditolak, bukan diterima tanpa verifikasi.</summary>
    public bool Terkonfigurasi =>
        !string.IsNullOrWhiteSpace(webhookSecret)
        && webhookSecret.Length >= SecretTerpendek
        && !webhookSecret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    /// <summary>Kontrak tanda tangan lokal: HMAC-SHA256 atas body mentah, format <c>sha256=&lt;hex&gt;</c>.</summary>
    public static string Sign(string secret, string rawBody) =>
        Imbuhan + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();

    public bool VerifySignature(string rawBody, string? signatureHeader)
    {
        if (!Terkonfigurasi || string.IsNullOrWhiteSpace(signatureHeader)) return false;
        var diberi = (signatureHeader.StartsWith(Imbuhan, StringComparison.OrdinalIgnoreCase)
            ? signatureHeader[Imbuhan.Length..]
            : signatureHeader).Trim().ToLowerInvariant();
        var harus = Encoding.UTF8.GetBytes(Sign(webhookSecret, rawBody)[Imbuhan.Length..]);
        var nyata = Encoding.UTF8.GetBytes(diberi);
        return harus.Length == nyata.Length && CryptographicOperations.FixedTimeEquals(harus, nyata);
    }

    public Task<PaymentQuote> CreateQuoteAsync(OrderPaymentRequest request, CancellationToken ct = default)
    {
        var instruksi = JsonSerializer.Serialize(new
        {
            tipe = "qris-simulasi",
            nominal = request.Amount,
            nomorPesanan = request.OrderNumber,
            catatan = "Simulasi development. Tidak ada QRIS sungguhan dan tidak ada dana yang berpindah.",
            kirimWebhookKe = "/api/payments/gateway/webhook",
            headerTandaTangan = SignatureHeader,
        }, Web);

        return Task.FromResult(new PaymentQuote(
            request.OrderId, request.PaymentId, Name, $"MOCK-QRIS-{request.PaymentId:N}",
            request.Amount, DateTime.UtcNow.AddDays(1), instruksi, IsLive));
    }

    public GatewayPaymentUpdate? ParseUpdate(string rawBody)
    {
        WebhookBody? isi;
        try
        {
            isi = JsonSerializer.Deserialize<WebhookBody>(rawBody, Web);
        }
        catch (JsonException)
        {
            return null;
        }
        if (isi is null || !Guid.TryParse(isi.PaymentId, out var id) || id == Guid.Empty) return null;
        if (!Enum.TryParse<WebhookStatus>(isi.Status, ignoreCase: true, out var status)) return null;

        var terjadi = DateTime.TryParse(isi.OccurredAt, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : DateTime.UtcNow;
        return new GatewayPaymentUpdate(id, (isi.Reference ?? string.Empty).Trim(), isi.Amount,
            status == WebhookStatus.Paid, terjadi, isi.FailureReason);
    }

    private sealed record WebhookBody(string? PaymentId, string? Reference, decimal Amount, string? Status,
        string? OccurredAt, string? FailureReason);

    private enum WebhookStatus
    {
        Paid,
        Failed,
    }
}
