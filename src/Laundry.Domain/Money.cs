namespace Laundry.Domain;

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentKind Kind { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    /// <summary>Mencegah pencatatan ganda saat event disinkronkan ulang.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? RecordedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
}

public class Refund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Order? Order { get; set; }
    public Guid PaymentId { get; set; }
    public Payment? OriginPayment { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Ledger append-only. Saldo = SUM(delta); tidak pernah mengedit baris lama.</summary>
public class CreditLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    /// <summary>Positif = kredit masuk (overpayment), negatif = dipakai.</summary>
    public decimal Delta { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? RefundId { get; set; }
    public Guid ActorUserId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}
