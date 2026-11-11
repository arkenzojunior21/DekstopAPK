using System.Text.Json;
using Laundry.Application.Sync;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Services;

/// <summary>Tulis audit transaksi append-only (PRD 6.16) — hanya order/payment/refund/credit.</summary>
public sealed class AuditWriter(LaundryDbContext db)
{
    public void Write(string entityType, Guid entityId, string action, Guid actorUserId,
        object? before, object? after, string? reason = null, string? correlationId = null, string source = "lokal")
    {
        db.AuditLogs.Add(new TransactionAuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ActorUserId = actorUserId,
            BeforeJson = before is null ? "{}" : JsonSerializer.Serialize(before),
            AfterJson = after is null ? "{}" : JsonSerializer.Serialize(after),
            Reason = reason,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N"),
            Source = source,
            AtUtc = DateTime.UtcNow,
        });
    }
}

/// <summary>
/// Enqueue event outbox + notifikasi dalam transaksi yang sama dengan perubahan data (Fase 6).
/// Hanya payload bertipe (CustomerEvent/OrderEvent/...) yang boleh masuk antrian, agar sisi
/// penerima selalu bisa mendeserialisasi — tidak ada anonimous object yang lolos.
/// </summary>
public sealed class EventWriter(LaundryDbContext db)
{
    private static long nomorLokal;

    private void EnqueueSync(string aggregateType, Guid aggregateId, string operation, object payload, string deviceId)
    {
        // Nomor antrean (Sequence) baru terbit saat SaveChanges; UrutanLokal mencatat bahwa event
        // ini dibuat lebih dulu dari event lain, supaya induk tidak pernah kalah nomor dari anaknya.
        db.SyncOutbox.Add(new SyncOutbox
        {
            EventId = Guid.NewGuid().ToString("N"),
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Operation = operation,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAtUtc = DateTime.UtcNow,
            SourceDevice = deviceId,
            UrutanLokal = Interlocked.Increment(ref nomorLokal),
        });
    }

    public void EnqueueOrder(Order order, string operation, string deviceId) =>
        EnqueueSync(SyncAggregate.Order, order.Id, operation, BuildOrder(order), deviceId);

    /// <summary>
    /// Snapshot pesanan untuk antrian sinkronisasi, dibaca ulang setelah SaveChanges supaya baris
    /// riwayat yang baru dibuat ikut terkirim. Pelanggannya ikut diantrekan bila belum pernah,
    /// agar sisi tujuan tidak menggantung menunggu induk yang tidak akan datang.
    /// </summary>
    public async Task EnqueueOrderSnapshotAsync(Guid orderId, string operation, string deviceId,
        CancellationToken ct)
    {
        var segar = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.Customer).ThenInclude(c => c!.User)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");

        if (segar.Customer is { } pelanggan && pelanggan.User is { } pemilik)
        {
            var sudahPernah = await db.SyncOutbox.AnyAsync(s =>
                s.AggregateType == SyncAggregate.Customer && s.AggregateId == pelanggan.Id, ct);
            if (!sudahPernah) EnqueueCustomer(pelanggan, pemilik, deviceId);
        }
        EnqueueOrder(segar, operation, deviceId);
    }

    public void EnqueuePayment(Payment payment, string deviceId) =>
        EnqueueSync(SyncAggregate.Payment, payment.Id, "created", new PaymentEvent(
            payment.Id, payment.OrderId, payment.CustomerId, payment.Amount, payment.Method, payment.Kind,
            payment.Status, payment.ProviderReference, payment.IdempotencyKey, payment.RecordedByUserId,
            payment.CreatedAtUtc, payment.ConfirmedAtUtc), deviceId);

    public void EnqueueRefund(Refund refund, string deviceId) =>
        EnqueueSync(SyncAggregate.Refund, refund.Id, "created", new RefundEvent(
            refund.Id, refund.OrderId, refund.PaymentId, refund.Amount, refund.Reason, refund.Status,
            refund.ProviderReference, refund.IdempotencyKey, refund.CreatedAtUtc), deviceId);

    public void EnqueueCreditLedger(CreditLedgerEntry entry, string deviceId) =>
        EnqueueSync(SyncAggregate.CreditLedger, entry.Id, "dibuat", new CreditLedgerEvent(
            entry.Id, entry.CustomerId, entry.Delta, entry.OrderId, entry.PaymentId, entry.RefundId,
            entry.ActorUserId, entry.IdempotencyKey, entry.CreatedAtUtc, entry.Reason), deviceId);

    public void EnqueueCustomer(CustomerProfile profile, User user, string deviceId) =>
        EnqueueSync(SyncAggregate.Customer, profile.Id, "disimpan", new CustomerEvent(
            profile.Id, user.Id, profile.Nama, profile.Alamat, user.NormalizedPhone,
            user.Role, user.IsActive, user.CreatedAtUtc), deviceId);

    /// <summary>
    /// Payload agregat pesanan: snapshot penuh + baris + riwayat, supaya sisi penerima dapat
    /// membangun ulang agregat tanpa ikut mengirim password hash atau data auth lainnya.
    /// </summary>
    public static OrderEvent BuildOrder(Order o) => new(
        o.Id, o.OrderNumber, o.CustomerId, o.Customer?.Nama ?? "", o.Customer?.Alamat,
        o.Customer?.User?.NormalizedPhone, o.Customer?.User?.Role ?? UserRole.Pelanggan,
        o.Status, o.PaymentStatus, o.Express, o.Fulfillment, o.EstimatedTotal, o.ActualWeightKg,
        o.ActualItemCount, o.FinalTotal, o.DeliveryFee, o.AppliedCredit, o.ExpressSurchargeSnapshot,
        o.Source, o.IsCancelled, o.CancelRequestedAtUtc, o.CancelReason, o.CreatedAtUtc,
        o.EstimatedFinishUtc,
        o.Items.Select(i => new OrderItemEvent(i.Id, i.ServiceId, i.ServiceNameSnapshot, i.UnitTypeSnapshot,
            i.PriceSnapshot, i.EstimatedWeightKg, i.EstimatedItemCount, i.ActualWeightKg, i.ActualItemCount,
            i.Subtotal, i.MinimumWeightKgSnapshot)).ToList(),
        o.StatusHistory.Select(h => new StatusHistoryEvent(h.Id, h.FromStatus, h.ToStatus, h.ActorUserId,
            h.AtUtc, h.Note)).ToList());

    /// <summary>EventId deterministik → kirim ulang tidak menghasilkan notifikasi ganda (PRD 6.12).</summary>
    public void EnqueueNotification(string templateKey, string targetPhone, string eventId, object payload)
    {
        // Baris yang belum di-simpan pun ikut dicek: satu request bisa mengantre beberapa notifikasi
        // sebelum SaveChanges, dan EventId punya constraint unik di database.
        if (db.ChangeTracker.Entries<NotificationOutbox>().Any(e => e.Entity.EventId == eventId)) return;
        if (db.NotificationOutbox.Any(n => n.EventId == eventId)) return;
        db.NotificationOutbox.Add(new NotificationOutbox
        {
            TemplateKey = templateKey,
            TargetPhone = targetPhone,
            EventId = eventId,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAtUtc = DateTime.UtcNow,
        });
    }

    /// <summary>
    /// Notifikasi untuk admin memakai nomor admin sungguhan, bukan placeholder "admin":
    /// antrean yang isinya teks placeholder tidak akan pernah bisa terkirim.
    /// </summary>
    public async Task EnqueueAdminNotificationAsync(string templateKey, string eventId, object payload,
        CancellationToken ct)
    {
        var telepon = await db.Users
            .Where(u => u.Role == UserRole.Admin && u.IsActive)
            .Select(u => u.NormalizedPhone).ToListAsync(ct);
        foreach (var nomor in telepon) EnqueueNotification(templateKey, nomor, $"{eventId}:{nomor}", payload);
    }

    /// <summary>Notifikasi ke nomor akun pemilik profil pelanggan.</summary>
    public async Task EnqueueCustomerNotificationAsync(string templateKey, Guid customerId, string eventId,
        object payload, CancellationToken ct)
    {
        var nomor = await db.CustomerProfiles.Where(p => p.Id == customerId)
            .Select(p => p.User!.NormalizedPhone).FirstOrDefaultAsync(ct);
        if (nomor is not null) EnqueueNotification(templateKey, nomor, eventId, payload);
    }
}

/// <summary>Nomor pesanan unik global, stabil saat sinkronisasi (PRD 6.5).</summary>
public static class OrderNumberGenerator
{
    public static string New() =>
        $"LR-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
}

/// <summary>Konfigurasi non-rahasia tersimpan di AppSettings (hybrid config, Fase 8).</summary>
public sealed class SettingsStore(LaundryDbContext db)
{
    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct)
    {
        var row = await db.AppSettings.FindAsync(new object[] { key }, ct);
        if (row is null) return fallback;
        return decimal.TryParse(row.ValueJson.Trim('"'), out var v) ? v : fallback;
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken ct)
    {
        var row = await db.AppSettings.FindAsync(new object[] { key }, ct);
        if (row is null) return null;
        try { return JsonSerializer.Deserialize<string>(row.ValueJson); }
        catch (JsonException) { return row.ValueJson.Trim('"'); }
    }

    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct)
    {
        var nilai = await GetDecimalAsync(key, fallback, ct);
        return nilai > int.MinValue && nilai < int.MaxValue ? (int)Math.Round(nilai) : fallback;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken ct)
    {
        var nilai = await GetStringAsync(key, ct);
        if (nilai is null) return fallback;
        nilai = nilai.Trim().ToLowerInvariant();
        return nilai is "1" or "true" or "ya" ? true : nilai is "0" or "false" or "tidak" ? false : fallback;
    }

    public async Task SetStringAsync(string key, string value, CancellationToken ct)
    {
        var row = await db.AppSettings.FindAsync(new object[] { key }, ct);
        var json = JsonSerializer.Serialize(value);
        if (row is null)
            db.AppSettings.Add(new AppSetting { Key = key, ValueJson = json, UpdatedAtUtc = DateTime.UtcNow });
        else
        {
            row.ValueJson = json;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SetDecimalAsync(string key, decimal value, CancellationToken ct)
    {
        var row = await db.AppSettings.FindAsync(new object[] { key }, ct);
        var json = JsonSerializer.Serialize(value);
        if (row is null)
            db.AppSettings.Add(new AppSetting { Key = key, ValueJson = json, UpdatedAtUtc = DateTime.UtcNow });
        else
        {
            row.ValueJson = json;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
