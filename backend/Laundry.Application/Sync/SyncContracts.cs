using Laundry.Domain;

namespace Laundry.Application.Sync;

/// <summary>Nama agregat yang dipakai di kolom AggregateType outbox/inbox.</summary>
public static class SyncAggregate
{
    public const string Customer = "Customer";
    public const string Order = "Order";
    public const string Payment = "Payment";
    public const string Refund = "Refund";
    public const string CreditLedger = "CreditLedger";

    /// <summary>Urutan penerapan: induk sebelum anak. Event dengan rank lebih kecil diproses lebih dulu.</summary>
    public static int Rank(string aggregateType) => aggregateType switch
    {
        Customer => 0,
        Order => 1,
        Payment => 2,
        Refund => 3,
        CreditLedger => 4,
        _ => 9,
    };
}

public static class SyncDirection
{
    public const string AdminKeCloud = "admin_ke_cloud";
    public const string CloudKeAdmin = "cloud_ke_admin";
}

public sealed record CustomerEvent(Guid CustomerId, Guid UserId, string Nama, string? Alamat,
    string NormalizedPhone, UserRole Role, bool Aktif, DateTime CreatedAtUtc);

public sealed record OrderItemEvent(Guid Id, Guid ServiceId, string ServiceName, ServiceUnitType UnitType,
    decimal PriceSnapshot, decimal? EstimatedWeightKg, decimal? EstimatedItemCount, decimal? ActualWeightKg,
    decimal? ActualItemCount, decimal Subtotal, decimal? MinimumWeightKgSnapshot);

public sealed record StatusHistoryEvent(Guid Id, OrderStatus FromStatus, OrderStatus ToStatus,
    Guid ActorUserId, DateTime AtUtc, string? Note);

/// <summary>Pesanan utuh beserta baris dan riwayatnya, agar sisi penerima dapat membangun ulang agregat.</summary>
public sealed record OrderEvent(
    Guid OrderId, string OrderNumber, Guid CustomerId, string CustomerName, string? CustomerAlamat,
    string? CustomerPhone, UserRole CustomerRole,
    OrderStatus Status, PaymentStatus PaymentStatus, bool Express, FulfillmentMethod Fulfillment,
    decimal EstimatedTotal, decimal? ActualWeightKg, decimal? ActualItemCount, decimal FinalTotal,
    decimal DeliveryFee, decimal AppliedCredit, decimal ExpressSurchargeSnapshot, string Source,
    bool IsCancelled, DateTime? CancelRequestedAtUtc, string? CancelReason,
    DateTime CreatedAtUtc, DateTime? EstimatedFinishUtc,
    List<OrderItemEvent> Items, List<StatusHistoryEvent> History);

public sealed record PaymentEvent(Guid Id, Guid OrderId, Guid CustomerId, decimal Amount, PaymentMethod Method,
    PaymentKind Kind, PaymentStatus Status, string? ProviderReference, string IdempotencyKey,
    Guid? RecordedByUserId, DateTime CreatedAtUtc, DateTime? ConfirmedAtUtc);

public sealed record RefundEvent(Guid Id, Guid OrderId, Guid PaymentId, decimal Amount, string Reason,
    PaymentStatus Status, string? ProviderReference, string IdempotencyKey, DateTime CreatedAtUtc);

public sealed record CreditLedgerEvent(Guid Id, Guid CustomerId, decimal Delta, Guid? OrderId, Guid? PaymentId,
    Guid? RefundId, Guid ActorUserId, string IdempotencyKey, DateTime CreatedAtUtc, string Reason);

/// <summary>Event belum bisa diterapkan (mis. induknya belum tiba atau jam simpang) — antre, jangan dibuang.</summary>
public sealed class SyncRetryableException(string message) : Exception(message);

/// <summary>Data ganda/tabrakan yang tidak boleh ditimpa; dicatat ke SyncConflict untuk rekonsiliasi manual.</summary>
public sealed class SyncConflictException(string message) : Exception(message);

public record SyncQueueRow(long Sequence, string EventId, string AggregateType, Guid AggregateId,
    string Operation, string PayloadJson, DateTime CreatedAtUtc, int RetryCount, string? LastError);

public record SyncStatusDto(
    int AntrianKeluarAdmin, int AntrianKeluarCloud, int AntrianGagalAdmin, int AntrianGagalCloud,
    int InboxMenungguAdmin, int InboxMenungguCloud, int InboxGagalAdmin, int InboxGagalCloud,
    long CheckpointAdminKeCloud, long CheckpointCloudKeAdmin,
    DateTime? SinkronTerakhirBerhasilUtc, DateTime? DetakTerakhirUtc, bool PekerjaAktif);
