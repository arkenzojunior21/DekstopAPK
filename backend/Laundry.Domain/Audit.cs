namespace Laundry.Domain;

/// <summary>Audit append-only untuk pesanan/pembayaran/refund/kredit. Tanpa edit/hapus dari aplikasi.</summary>
public class TransactionAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string Source { get; set; } = "lokal";
    public DateTime AtUtc { get; set; }
}

/// <summary>Konfigurasi non-rahasia (nama usaha dsb tidak di-tebak; lihat OPEN_QUESTIONS).</summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class BackupHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Target { get; set; } = "admin"; // admin | cloud
    public string FilePath { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public bool IsSuccess { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
