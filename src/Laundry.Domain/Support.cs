namespace Laundry.Domain;

public class Complaint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ComplaintStatus Status { get; set; } = ComplaintStatus.Baru;
    public DateTime CreatedAtUtc { get; set; }
    public List<ComplaintMessage> Messages { get; set; } = new();
}

public class ComplaintMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComplaintId { get; set; }
    public Guid SenderUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }
}

public class ReceiptSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    /// <summary>JSON isi struk pada saat dicetak agar dapat dihasilkan ulang identik.</summary>
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class NotificationOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Channel { get; set; } = "whatsapp";
    public string TemplateKey { get; set; } = string.Empty;
    public string TargetPhone { get; set; } = string.Empty;
    /// <summary>Event pemicu; dipakai juga sebagai kunci idempotensi pengiriman.</summary>
    public string EventId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public NotificationStatus Status { get; set; } = NotificationStatus.Menunggu;
    public int RetryCount { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
}
