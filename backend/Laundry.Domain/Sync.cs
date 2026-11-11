namespace Laundry.Domain;

/// <summary>Satu event perubahan data yang dikirim antar database lokal &amp; cloud. Payload disimpan sebagai JSON agar bentuk event bisa dievolusikan.</summary>
public class SyncEventEnvelope
{
    public long Sequence { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>Antrean keluar (transactional outbox) pada sisi pengirim.</summary>
public class SyncOutbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public SyncEventStatus Status { get; set; } = SyncEventStatus.Menunggu;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    /// <summary>Retry exponential backoff: baris belum boleh dicoba sebelum waktu ini.</summary>
    public DateTime? NextRetryAtUtc { get; set; }
    public string SourceDevice { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }

    /// <summary>
    /// Urutan pembuatan event di dalam proses ini (bukan kolom database). Dipakai saat menyimpan
    /// untuk memetakan nomor sequence supaya event induk selalu dapat nomor lebih kecil dari anaknya.
    /// </summary>
    public long UrutanLokal { get; set; }
}

/// <summary>Antrean masuk di sisi penerima; deduplication berbasis EventId.</summary>
public class SyncInbox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public SyncEventStatus Status { get; set; } = SyncEventStatus.Menunggu;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    /// <summary>Retry exponential backoff: baris belum boleh diterapkan sebelum waktu ini.</summary>
    public DateTime? NextRetryAtUtc { get; set; }
}

/// <summary>Posisi terakhir sinkronisasi agar bisa lanjut setelah crash/putus.</summary>
public class SyncCheckpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Direction { get; set; } = string.Empty; // admin_ke_cloud | cloud_ke_admin
    public long LastSequence { get; set; }
    public DateTime? LastSuccessAtUtc { get; set; }
}

public class SyncConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public string Detail { get; set; } = string.Empty;
    public string Resolution { get; set; } = "PerluRekonsiliasi";
    public DateTime CreatedAtUtc { get; set; }
}
