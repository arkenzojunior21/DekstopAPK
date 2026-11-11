using Laundry.Domain;

namespace Laundry.Application.Core;

// ---------- Katalog ----------
public record ServicePriceDto(Guid Id, decimal Price, int StandardDurationDays, int ExpressDurationDays,
    decimal ExpressSurcharge, DateTime ValidFromUtc);

public record ServiceDto(Guid Id, string Nama, ServiceUnitType UnitType, decimal? MinimumWeightKg,
    bool IsActive, decimal CurrentPrice, int StandardDurationDays, int ExpressDurationDays,
    decimal ExpressSurcharge);

public record UpsertServiceRequest(string Nama, ServiceUnitType UnitType, decimal? MinimumWeightKg,
    decimal Price, int StandardDurationDays, int ExpressDurationDays, decimal ExpressSurcharge,
    bool IsActive = true);

// ---------- Pelanggan ----------
public record CustomerDto(Guid Id, string Nama, string Phone, string? Alamat, bool Aktif, DateTime DibuatUtc);

public record CreateCustomerRequest(string Nama, string Phone, string? Alamat);
public record UpdateCustomerRequest(string Nama, string? Alamat);

// ---------- Pesanan ----------
public record OrderItemInput(Guid ServiceId, decimal? EstimatedWeightKg, decimal? EstimatedItemCount);
public record ActualItemInput(Guid OrderItemId, decimal? ActualWeightKg, decimal? ActualItemCount);

public record CreateOrderRequest(
    Guid? CustomerId,
    string? WalkInNama,
    string? WalkInPhone,
    List<OrderItemInput> Items,
    bool Express,
    FulfillmentMethod Fulfillment,
    Guid? SlotId,
    string? Alamat,
    string? Catatan);

public record OrderListItem(Guid Id, string OrderNumber, string CustomerName, OrderStatus Status,
    PaymentStatus Payment, decimal EstimatedTotal, decimal? FinalTotal, DateTime CreatedAtUtc,
    DateTime? EstimatedFinishUtc, string Source);

public record OrderDetailDto(
    Guid Id, string OrderNumber, Guid CustomerId, string CustomerName, OrderStatus Status,
    PaymentStatus Payment, bool Express, FulfillmentMethod Fulfillment, string? Alamat,
    decimal EstimatedTotal, decimal? ActualWeightKg, decimal? ActualItemCount, decimal FinalTotal,
    decimal Paid, decimal Remaining, decimal AppliedCredit, decimal Refunded, decimal DeliveryFee,
    DateTime CreatedAtUtc, DateTime? EstimatedFinishUtc, string Source,
    List<OrderItemDto> Items, List<StatusHistoryDto> History);

public record OrderItemDto(Guid Id, string ServiceName, ServiceUnitType UnitType, decimal PriceSnapshot,
    decimal? EstimatedWeightKg, decimal? EstimatedItemCount, decimal? ActualWeightKg,
    decimal? ActualItemCount, decimal Subtotal);

public record StatusHistoryDto(OrderStatus From, OrderStatus To, Guid ActorUserId, DateTime AtUtc, string? Note);

// ---------- Pembayaran ----------
public record RecordCashPaymentRequest(Guid OrderId, decimal Amount, PaymentKind Kind, string? Note, string? IdempotencyKey = null);
public record ApplyCreditRequest(Guid OrderId, decimal Amount, bool CustomerConsent);
public record PaymentDto(Guid Id, Guid OrderId, decimal Amount, PaymentMethod Method, PaymentKind Kind,
    PaymentStatus Status, string? ProviderReference, DateTime CreatedAtUtc);

public record RefundRequest(Guid OrderId, decimal Amount, string Reason, Guid? PaymentId = null,
    string? IdempotencyKey = null);
public record RefundDto(Guid Id, Guid OrderId, Guid PaymentId, decimal Amount, PaymentMethod Method,
    string Reason, PaymentStatus Status, DateTime CreatedAtUtc);

// ---------- Struk ----------
public record ReceiptLineDto(string Nama, string Satuan, decimal Harga, decimal? Jumlah, decimal Subtotal);
public record ReceiptPaymentDto(PaymentMethod Method, decimal Amount, DateTime AtUtc, PaymentStatus Status);
public record ReceiptDto(
    Guid OrderId, string OrderNumber, string? BusinessName, string CustomerName, string? CustomerPhone,
    OrderStatus Status, PaymentStatus Payment, bool Express, FulfillmentMethod Fulfillment, string? Alamat,
    DateTime CreatedAtUtc, DateTime? EstimatedFinishUtc,
    List<ReceiptLineDto> Lines, decimal ItemsSubtotal, decimal DeliveryFee, decimal FinalTotal,
    List<ReceiptPaymentDto> Payments, decimal Refunded, decimal AppliedCredit, decimal Paid, decimal Remaining,
    string? Petugas, DateTime GeneratedAtUtc);

// ---------- Komplain ----------
public record CreateComplaintRequest(Guid OrderId, string Category, string Description);
public record ComplaintDto(Guid Id, Guid OrderId, Guid CustomerId, string Category, string Description,
    ComplaintStatus Status, DateTime CreatedAtUtc, List<ComplaintMessageDto> Messages);
public record ComplaintMessageDto(Guid Id, Guid SenderUserId, string Body, DateTime SentAtUtc);

// ---------- Slot ----------
public record SlotDto(Guid Id, DateTime StartsAtUtc, DateTime EndsAtUtc, int Capacity, int BookedCount, bool IsActive);
public record CreateSlotRequest(DateTime StartsAtUtc, DateTime EndsAtUtc, int Capacity);

// ---------- Dashboard ----------
public record AdminDashboardDto(
    int PesananBaru, int DalamProses, int SiapDiambil, int BelumLunas,
    int KomplainTerbuka, int PenjemputanHariIni, int PengantaranHariIni,
    decimal OmzetHariIni, decimal PenerimaanKasHariIni, decimal Piutang,
    int AntrianSync, int SyncGagal, DateTime? SinkronTerakhirBerhasil);

public record CustomerDashboardDto(List<OrderListItem> PesananAktif, decimal SaldoKredit, decimal TotalTagihan);
