using Laundry.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Laundry.Infrastructure.Persistence;

public class LaundryDbContext : DbContext
{
    public LaundryDbContext(DbContextOptions<LaundryDbContext> options) : base(options) { }

    /// <summary>Untuk context turunan (CloudDbContext) yang memakai options bertipe sendiri.</summary>
    protected LaundryDbContext(DbContextOptions options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServicePriceVersion> ServicePriceVersions => Set<ServicePriceVersion>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistories => Set<OrderStatusHistory>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<CreditLedgerEntry> CreditLedger => Set<CreditLedgerEntry>();
    public DbSet<PickupDeliverySlot> PickupDeliverySlots => Set<PickupDeliverySlot>();
    public DbSet<FulfillmentTask> FulfillmentTasks => Set<FulfillmentTask>();
    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<ComplaintMessage> ComplaintMessages => Set<ComplaintMessage>();
    public DbSet<ReceiptSnapshot> ReceiptSnapshots => Set<ReceiptSnapshot>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<SyncOutbox> SyncOutbox => Set<SyncOutbox>();
    public DbSet<SyncInbox> SyncInbox => Set<SyncInbox>();
    public DbSet<SyncCheckpoint> SyncCheckpoints => Set<SyncCheckpoint>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();
    public DbSet<TransactionAuditLog> AuditLogs => Set<TransactionAuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<BackupHistory> BackupHistories => Set<BackupHistory>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        foreach (var prop in mb.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal)))
        {
            prop.SetColumnType("numeric(18,2)");
        }

        foreach (var prop in mb.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType.BaseType == typeof(Enum)))
        {
            var converterType = typeof(Microsoft.EntityFrameworkCore.Storage.ValueConversion.EnumToStringConverter<>).MakeGenericType(prop.ClrType);
            prop.SetValueConverter((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)Activator.CreateInstance(converterType)!);
        }

        mb.Entity<User>().HasIndex(u => u.NormalizedPhone).IsUnique();
        mb.Entity<User>().HasOne(u => u.Profile).WithOne(p => p.User).HasForeignKey<CustomerProfile>(p => p.UserId);
        mb.Entity<CustomerProfile>().HasQueryFilter(p => !p.IsDeleted);
        mb.Entity<OtpChallenge>().HasIndex(o => new { o.TargetPhone, o.Purpose });
        mb.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();

        mb.Entity<Service>().HasMany(s => s.PriceVersions).WithOne().HasForeignKey(p => p.ServiceId);
        mb.Entity<ServicePriceVersion>().HasIndex(p => new { p.ServiceId, p.ValidFromUtc });

        mb.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        mb.Entity<Order>().HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId);
        mb.Entity<Order>().HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Order>().HasMany(o => o.StatusHistory).WithOne().HasForeignKey(h => h.OrderId);
        mb.Entity<OrderStatusHistory>().HasIndex(h => new { h.OrderId, h.AtUtc });

        mb.Entity<Payment>().HasIndex(p => p.IdempotencyKey).IsUnique();
        mb.Entity<Order>().HasMany(o => o.Payments).WithOne(p => p.Order).HasForeignKey(p => p.OrderId);
        mb.Entity<Order>().HasMany(o => o.Refunds).WithOne(r => r.Order).HasForeignKey(r => r.OrderId);
        mb.Entity<Refund>().HasIndex(r => r.OrderId);
        mb.Entity<Refund>().HasOne(r => r.OriginPayment!).WithMany().HasForeignKey(r => r.PaymentId);
        mb.Entity<Refund>().HasIndex(r => r.IdempotencyKey).IsUnique();
        mb.Entity<CreditLedgerEntry>().HasIndex(e => e.IdempotencyKey).IsUnique();
        mb.Entity<CreditLedgerEntry>().HasIndex(e => e.CustomerId);

        mb.Entity<Complaint>().HasMany(c => c.Messages).WithOne().HasForeignKey(m => m.ComplaintId);

        mb.Entity<ReceiptSnapshot>().HasIndex(r => r.ReceiptNumber).IsUnique();

        mb.Entity<NotificationOutbox>().HasIndex(n => n.EventId).IsUnique();

        mb.Entity<SyncOutbox>().HasIndex(s => s.EventId).IsUnique();
        mb.Entity<SyncOutbox>().HasIndex(s => s.Sequence);
        mb.Entity<SyncOutbox>().Ignore(s => s.UrutanLokal);
        // Nomor antrean datang dari sequence database, bukan MAX+1 di aplikasi: MAX+1 bisa memberi
        // nilai sama untuk beberapa baris dalam satu transaksi. Nilainya diambil lewat
        // BeriNomorEventAsync (lihat bawah) supaya urutannya pasti, bukan default kolom.
        mb.Entity<SyncOutbox>().Property(s => s.Sequence).HasDefaultValueSql("nextval('sync_event_sequence')");
        mb.Entity<SyncInbox>().HasIndex(s => s.EventId).IsUnique();
        mb.Entity<SyncInbox>().HasIndex(s => s.Sequence);
        mb.Entity<SyncCheckpoint>().HasIndex(c => c.Direction).IsUnique();

        mb.Entity<TransactionAuditLog>().HasIndex(a => new { a.EntityType, a.EntityId });
        mb.Entity<AppSetting>().HasKey(a => a.Key);
        mb.Entity<AppSetting>().Property(a => a.Key).HasMaxLength(128);
    }

    public override int SaveChanges()
    {
        BeriNomorEvent();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BeriNomorEvent();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await BeriNomorEventAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        await BeriNomorEventAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Nomor antrean sync event diambil dari sequence, lalu dipetakan manual ke baris baru sesuai
    /// urutan pembuatan event (UrutanLokal). Dua hal yang tidak bisa dipercaya: MAX+1 di aplikasi
    /// (bisa kembar dalam satu transaksi) dan urutan evaluasi nextval antar baris INSERT (Postgres
    /// tidak menjaminnya). Padahal nomor inilah yang membuat event induk terkirim lebih dulu dari
    /// anaknya; tanpa pemetaan ini antrean bisa keluar terbalik dan anak gagal terus sampai Gagal.
    /// </summary>
    private List<EntityEntry<SyncOutbox>> BarisEventBaru() =>
        ChangeTracker.Entries<SyncOutbox>()
            .Where(e => e.State == EntityState.Added && e.Entity.Sequence == 0)
            .OrderBy(e => e.Entity.UrutanLokal)
            .ToList();

    /// <summary>generate_series dipakai supaya satu kali bolak-balik ke database untuk semua nomor.</summary>
    private const string QueryNomor =
        "select nextval('sync_event_sequence') as value from generate_series(1, {0})";

    private void BeriNomorEvent()
    {
        var baru = BarisEventBaru();
        if (baru.Count == 0) return;
        TerapkanNomor(baru, Database.SqlQueryRaw<long>(QueryNomor, baru.Count).ToList());
    }

    private async Task BeriNomorEventAsync(CancellationToken ct)
    {
        var baru = BarisEventBaru();
        if (baru.Count == 0) return;
        var nilai = await Database.SqlQueryRaw<long>(QueryNomor, baru.Count).ToListAsync(ct);
        TerapkanNomor(baru, nilai);
    }

    private static void TerapkanNomor(List<EntityEntry<SyncOutbox>> baru, List<long> nilai)
    {
        nilai = [.. nilai.OrderBy(n => n)];
        if (nilai.Count != baru.Count)
            throw new InvalidOperationException($"Nomor antrean tidak lengkap: {nilai.Count} dari {baru.Count}.");
        for (var i = 0; i < baru.Count; i++) baru[i].Entity.Sequence = nilai[i];
    }
}
