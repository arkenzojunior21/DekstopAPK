namespace Laundry.Domain;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Nomor pesanan unik global, sama saat disinkronkan.</summary>
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public CustomerProfile? Customer { get; set; }
    public FulfillmentMethod Fulfillment { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.MenungguKonfirmasi;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.BelumBayar;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? EstimatedFinishUtc { get; set; }
    public decimal EstimatedTotal { get; set; }
    public decimal? ActualWeightKg { get; set; }
    public decimal? ActualItemCount { get; set; }
    public decimal FinalTotal { get; set; }
    public bool Express { get; set; }
    /// <summary>Besar biaya express pada saat order dibuat (snapshot).</summary>
    public decimal ExpressSurchargeSnapshot { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal AppliedCredit { get; set; }
    public string Source { get; set; } = "lokal"; // lokal | cloud
    public long SyncVersion { get; set; }
    public bool IsCancelled { get; set; }
    /// <summary>Pengajuan pembatalan oleh pelanggan; admin yang memutuskan (PRD 6.8).</summary>
    public DateTime? CancelRequestedAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public List<OrderStatusHistory> StatusHistory { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
    public List<Refund> Refunds { get; set; } = new();
    public List<FulfillmentTask> FulfillmentTasks { get; set; } = new();
}

public class OrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid ServiceId { get; set; }
    /// <summary>Price snapshot — tidak berubah saat tarif diedit kemudian.</summary>
    public string ServiceNameSnapshot { get; set; } = string.Empty;
    public ServiceUnitType UnitTypeSnapshot { get; set; }
    public decimal PriceSnapshot { get; set; }
    public decimal? EstimatedWeightKg { get; set; }
    public decimal? EstimatedItemCount { get; set; }
    public decimal? ActualWeightKg { get; set; }
    public decimal? ActualItemCount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal? MinimumWeightKgSnapshot { get; set; }
}

public class OrderStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public OrderStatus FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime AtUtc { get; set; }
    public string? Note { get; set; }
}
