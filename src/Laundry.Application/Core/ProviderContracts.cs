using Laundry.Application.Abstractions;
using Laundry.Domain;

namespace Laundry.Application.Core;

public record CreateGatewayPaymentRequest(Guid OrderId, PaymentKind Kind);

/// <summary>
/// Hasil webhook, dibatasi empat nilai agar pemanggil tidak menebak-nebak:
/// Disimpan = baru dibayar, Duplikat = webhook sama sudah pernah diproses,
/// Gagal = pembayaran ditandai gagal, Ditolak = isi tidak sah (nominal beda / payment tak dikenal).
/// </summary>
public enum GatewayWebhookStatus
{
    Disimpan,
    Duplikat,
    Gagal,
    Ditolak,
}

public record GatewayWebhookOutcome(GatewayWebhookStatus Status, Guid? PaymentId, string? Note);

/// <summary>Baris antrean notifikasi untuk ditampilkan/diperiksa admin.</summary>
public record NotificationQueueRow(Guid Id, string TemplateKey, string TargetPhone, string EventId,
    NotificationStatus Status, int RetryCount, string? ProviderMessageId, string? LastError,
    DateTime CreatedAtUtc, DateTime? SentAtUtc);

/// <summary>Hasil satu siklus pengiriman notifikasi.</summary>
public record NotificationDrainResult(int Diproses, int Terkirim, int Gagal, int Ditunda);

/// <summary>Isi antrean notifikasi, dipisahkan per status.</summary>
public record NotificationQueueStatus(int Menunggu, int Terkirim, int Gagal, int Ditunda,
    List<NotificationQueueRow> ContohMacet);

/// <summary>Kondisi provider: antrean notifikasi + adapter yang aktif beserta status live-nya.</summary>
public record ProviderStatusDto(NotificationQueueStatus Notifikasi, List<AdapterInfo> Adapter);

public record AdapterInfo(string Nama, string Jenis, bool Live, string Catatan);

/// <summary>Tagihan non-tunai lewat adapter gateway; status pembayaran berubah hanya lewat webhook tersignasi.</summary>
public interface IPaymentGatewayService
{
    Task<PaymentQuoteDto> CreateQuoteAsync(CreateGatewayPaymentRequest req, Guid actorUserId, CancellationToken ct);

    Task<GatewayWebhookOutcome> HandleWebhookAsync(GatewayPaymentUpdate update, CancellationToken ct);
}

public record PaymentQuoteDto(Guid PaymentId, Guid OrderId, string GatewayName, string Reference,
    decimal Amount, DateTime ExpiresAtUtc, string InstructionsJson, bool Live);

/// <summary>Mengosongkan antrean notifikasi lewat adapter WhatsApp. Tidak pernah menandai sukses bila tujuan tak diketahui.</summary>
public interface INotificationDispatcher
{
    Task<NotificationDrainResult> DrainAsync(CancellationToken ct = default);
    Task<NotificationQueueStatus> QueueStatusAsync(int contoh, CancellationToken ct = default);
}
