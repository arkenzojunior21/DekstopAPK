namespace Laundry.Application.Abstractions;

/// <summary>
/// Tanda terima penawaran pembayaran dari gateway: nomor referensi + instruksi bayar
/// yang akan ditampilkan ke pelanggan. Instruksi disimpan apa adanya dari adapter,
/// tidak dikarang di sisi aplikasi.
/// </summary>
public sealed record PaymentQuote(
    Guid OrderId,
    Guid PaymentId,
    string GatewayName,
    string Reference,
    decimal Amount,
    DateTime ExpiresAtUtc,
    string InstructionsJson,
    bool IsLive);

/// <summary>Isi notifikasi webhook pembayaran setelah diverifikasi tanda tangannya.</summary>
public sealed record GatewayPaymentUpdate(
    Guid PaymentId,
    string Reference,
    decimal Amount,
    bool Paid,
    DateTime OccurredAtUtc,
    string? FailureReason);

/// <summary>
/// Adapter payment gateway. Hanya MockPaymentGateway yang tersedia sekarang:
/// tidak ada nama vendor, endpoint, atau credential yang dikarang (D-04).
/// </summary>
public interface IPaymentGateway
{
    string Name { get; }

    /// <summary>false = adapter simulasi; mock tidak pernah ditampilkan sebagai integrasi sungguhan.</summary>
    bool IsLive { get; }

    /// <summary>Nama header tempat adapter menaruh tanda tangan (berbeda-beda per gateway).</summary>
    string SignatureHeader { get; }

    Task<PaymentQuote> CreateQuoteAsync(OrderPaymentRequest request, CancellationToken ct = default);

    /// <summary>Verifikasi HMAC atas body mentah. Secret belum diisi → selalu false.</summary>
    bool VerifySignature(string rawBody, string? signatureHeader);

    /// <summary>Isi webhook yang bentuknya tidak dikenali → null (bukan dipaksakan).</summary>
    GatewayPaymentUpdate? ParseUpdate(string rawBody);
}

/// <summary>Data yang dibutuhkan adapter untuk membuat tagihan.</summary>
public sealed record OrderPaymentRequest(
    Guid OrderId,
    Guid PaymentId,
    string OrderNumber,
    string CustomerName,
    decimal Amount,
    string CustomerPhone);

/// <summary>
/// Adapter pengirim pesan WhatsApp. Mock hanya mencatat di log; pengiriman sungguhan
/// menunggu credential WhatsApp Business resmi (PRD 6.12, D-04).
/// </summary>
public interface IWhatsAppSender
{
    string Name { get; }
    bool IsLive { get; }

    /// <summary>Mengembalikan id pesan dari provider sebagai bukti kirim.</summary>
    Task<string> SendAsync(string templateKey, string targetPhone, string payloadJson,
        CancellationToken ct = default);
}

/// <summary>Provider eksternal belum dikonfigurasi — bukan kesalahan data, jangan ditandai sukses.</summary>
public sealed class ProviderUnavailableException(string message) : Exception(message);
