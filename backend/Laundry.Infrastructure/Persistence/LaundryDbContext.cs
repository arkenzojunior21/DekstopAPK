using Laundry.Application.Abstractions;
using Laundry.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Laundry.Infrastructure.Persistence;

public class LaundryDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public LaundryDbContext(DbContextOptions<LaundryDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>Untuk context turunan (CloudDbContext) yang memakai options bertipe sendiri.</summary>
    protected LaundryDbContext(DbContextOptions options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

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
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Global Query Filter untuk multi-tenancy
        mb.Entity<User>().HasQueryFilter(u => _tenantContext.IsPlatformAdmin || u.TenantId == _tenantContext.TenantId);
        mb.Entity<CustomerProfile>().HasQueryFilter(p => !p.IsDeleted && (_tenantContext.IsPlatformAdmin || p.User!.TenantId == _tenantContext.TenantId));
        mb.Entity<Service>().HasQueryFilter(s => _tenantContext.IsPlatformAdmin || s.TenantId == _tenantContext.TenantId);
        mb.Entity<ServicePriceVersion>().HasQueryFilter(p => _tenantContext.IsPlatformAdmin || p.Service!.TenantId == _tenantContext.TenantId);
        mb.Entity<Order>().HasQueryFilter(o => _tenantContext.IsPlatformAdmin || o.TenantId == _tenantContext.TenantId);
        mb.Entity<OrderItem>().HasQueryFilter(i => _tenantContext.IsPlatformAdmin || i.Order!.TenantId == _tenantContext.TenantId);
        mb.Entity<OrderStatusHistory>().HasQueryFilter(h => _tenantContext.IsPlatformAdmin || h.Order!.TenantId == _tenantContext.TenantId);
        mb.Entity<Payment>().HasQueryFilter(p => _tenantContext.IsPlatformAdmin || p.Order!.TenantId == _tenantContext.TenantId);
        mb.Entity<Refund>().HasQueryFilter(r => _tenantContext.IsPlatformAdmin || r.Order!.TenantId == _tenantContext.TenantId);
        mb.Entity<CreditLedgerEntry>().HasQueryFilter(e => _tenantContext.IsPlatformAdmin || e.Customer!.User!.TenantId == _tenantContext.TenantId);
        mb.Entity<PickupDeliverySlot>().HasQueryFilter(s => _tenantContext.IsPlatformAdmin || s.TenantId == _tenantContext.TenantId);
        mb.Entity<FulfillmentTask>().HasQueryFilter(t => _tenantContext.IsPlatformAdmin || t.TenantId == _tenantContext.TenantId);
        mb.Entity<Complaint>().HasQueryFilter(c => _tenantContext.IsPlatformAdmin || c.TenantId == _tenantContext.TenantId);
        mb.Entity<ComplaintMessage>().HasQueryFilter(m => _tenantContext.IsPlatformAdmin || m.Complaint!.TenantId == _tenantContext.TenantId);
        mb.Entity<ReceiptSnapshot>().HasQueryFilter(r => _tenantContext.IsPlatformAdmin || r.TenantId == _tenantContext.TenantId);
        mb.Entity<NotificationOutbox>().HasQueryFilter(n => _tenantContext.IsPlatformAdmin || n.TenantId == _tenantContext.TenantId);
        mb.Entity<SyncOutbox>().HasQueryFilter(s => _tenantContext.IsPlatformAdmin || s.TenantId == _tenantContext.TenantId);
        mb.Entity<SyncInbox>().HasQueryFilter(s => _tenantContext.IsPlatformAdmin || s.TenantId == _tenantContext.TenantId);
        mb.Entity<SyncCheckpoint>().HasQueryFilter(c => _tenantContext.IsPlatformAdmin || c.TenantId == _tenantContext.TenantId);
        mb.Entity<SyncConflict>().HasQueryFilter(c => _tenantContext.IsPlatformAdmin || c.TenantId == _tenantContext.TenantId);
        mb.Entity<TransactionAuditLog>().HasQueryFilter(a => _tenantContext.IsPlatformAdmin || a.TenantId == _tenantContext.TenantId);
        mb.Entity<AppSetting>().HasQueryFilter(a => _tenantContext.IsPlatformAdmin || a.TenantId == _tenantContext.TenantId);
        mb.Entity<BackupHistory>().HasQueryFilter(b => _tenantContext.IsPlatformAdmin || b.TenantId == _tenantContext.TenantId);

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

        // Tenant
        mb.Entity<Tenant>().HasIndex(t => t.Code).IsUnique();
        mb.Entity<Tenant>().Property(t => t.Code).HasMaxLength(32);
        mb.Entity<Tenant>().Property(t => t.Nama).HasMaxLength(128);

        mb.Entity<User>().HasIndex(u => u.NormalizedPhone).IsUnique();
        mb.Entity<User>().HasOne(u => u.Profile).WithOne(p => p.User).HasForeignKey<CustomerProfile>(p => p.UserId);
        mb.Entity<User>().HasOne(u => u.Tenant).WithMany(t => t.Users).HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.SetNull);
        mb.Entity<CustomerProfile>().HasQueryFilter(p => !p.IsDeleted && (_tenantContext.IsPlatformAdmin || p.User!.TenantId == _tenantContext.TenantId));
        mb.Entity<OtpChallenge>().HasIndex(o => new { o.TargetPhone, o.Purpose });
        mb.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();
        mb.Entity<RefreshToken>().HasQueryFilter(r => _tenantContext.IsPlatformAdmin || r.User!.TenantId == _tenantContext.TenantId);

        mb.Entity<Service>().HasMany(s => s.PriceVersions).WithOne(p => p.Service).HasForeignKey(p => p.ServiceId);
        mb.Entity<ServicePriceVersion>().HasIndex(p => new { p.ServiceId, p.ValidFromUtc });

        mb.Entity<Order>().HasIndex(o => o.OrderNumber).IsUnique();
        mb.Entity<Order>().HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId);
        mb.Entity<Order>().HasOne(o => o.Tenant).WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<Order>().HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        mb.Entity<Order>().HasMany(o => o.StatusHistory).WithOne(h => h.Order).HasForeignKey(h => h.OrderId);
        mb.Entity<OrderStatusHistory>().HasIndex(h => new { h.OrderId, h.AtUtc });

        mb.Entity<Payment>().HasIndex(p => p.IdempotencyKey).IsUnique();
        mb.Entity<Order>().HasMany(o => o.Payments).WithOne(p => p.Order).HasForeignKey(p => p.OrderId);
        mb.Entity<Order>().HasMany(o => o.Refunds).WithOne(r => r.Order).HasForeignKey(r => r.OrderId);
        mb.Entity<Refund>().HasIndex(r => r.OrderId);
        mb.Entity<Refund>().HasOne(r => r.OriginPayment!).WithMany().HasForeignKey(r => r.PaymentId);
        mb.Entity<Refund>().HasIndex(r => r.IdempotencyKey).IsUnique();
        mb.Entity<CreditLedgerEntry>().HasIndex(e => e.IdempotencyKey).IsUnique();
        mb.Entity<CreditLedgerEntry>().HasIndex(e => e.CustomerId);
        mb.Entity<CreditLedgerEntry>().HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId);
        mb.Entity<CreditLedgerEntry>().HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<PickupDeliverySlot>().HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<FulfillmentTask>().HasOne(t => t.Tenant).WithMany().HasForeignKey(t => t.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Complaint>().HasMany(c => c.Messages).WithOne(m => m.Complaint).HasForeignKey(m => m.ComplaintId);
        mb.Entity<Complaint>().HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        mb.Entity<ComplaintMessage>().HasOne(m => m.Tenant).WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<ReceiptSnapshot>().HasIndex(r => r.ReceiptNumber).IsUnique();
        mb.Entity<ReceiptSnapshot>().HasOne(r => r.Tenant).WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<NotificationOutbox>().HasIndex(n => n.EventId).IsUnique();
        mb.Entity<NotificationOutbox>().HasOne(n => n.Tenant).WithMany().HasForeignKey(n => n.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<SyncOutbox>().HasIndex(s => s.EventId).IsUnique();
        mb.Entity<SyncOutbox>().HasIndex(s => s.Sequence);
        mb.Entity<SyncOutbox>().Ignore(s => s.UrutanLokal);
        mb.Entity<SyncOutbox>().Property(s => s.Sequence).HasDefaultValueSql("nextval('sync_event_sequence')");
        mb.Entity<SyncOutbox>().HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<SyncInbox>().HasIndex(s => s.EventId).IsUnique();
        mb.Entity<SyncInbox>().HasIndex(s => s.Sequence);
        mb.Entity<SyncInbox>().HasOne(s => s.Tenant).WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<SyncCheckpoint>().HasIndex(c => c.Direction).IsUnique();
        mb.Entity<SyncCheckpoint>().HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<SyncConflict>().HasIndex(c => c.AggregateType);
        mb.Entity<SyncConflict>().HasOne(c => c.Tenant).WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<TransactionAuditLog>().HasIndex(a => new { a.EntityType, a.EntityId });
        mb.Entity<TransactionAuditLog>().HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AppSetting>().HasKey(a => a.Key);
        mb.Entity<AppSetting>().Property(a => a.Key).HasMaxLength(128);
        mb.Entity<AppSetting>().HasOne(a => a.Tenant).WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);

        mb.Entity<BackupHistory>().HasIndex(b => b.TenantId);
        mb.Entity<BackupHistory>().HasOne(b => b.Tenant).WithMany().HasForeignKey(b => b.TenantId).OnDelete(DeleteBehavior.Restrict);
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
