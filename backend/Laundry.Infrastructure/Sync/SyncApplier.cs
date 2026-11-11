using System.Text.Json;
using System.Text.Json.Serialization;
using Laundry.Application.Sync;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Sync;

/// <summary>
/// Menerapkan satu event inbox ke database tujuan.
/// Aturan utama (PROMPT fase 6, diperhalus saat replika pertama diuji ke cloud nyata):
/// jejak beku (nominal, kunci idempotensi, layanan + tarif pesanan yang dibekukan) bersifat
/// immutable — isi berbeda untuk id yang sama jadi konflik untuk rekonsiliasi, bukan ditimpa.
/// Nilai yang memang bergerak bersama status (subtotal dari timbangan nyata, status pembayaran)
/// mengikuti event terbaru; event lama hanya dilewati, tidak memundurkan sisi penerima.
/// </summary>
public sealed class SyncApplier(LaundryDbContext target)
{
    private const long ToleransiJamDetik = 300;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task ApplyAsync(SyncInbox row, DateTime nowUtc, CancellationToken ct)
    {
        switch (row.AggregateType)
        {
            case SyncAggregate.Customer:
                await TerapkanCustomer(JsonSerializer.Deserialize<CustomerEvent>(row.PayloadJson, Json)!, row, nowUtc, ct);
                break;
            case SyncAggregate.Order:
                await TerapkanOrder(JsonSerializer.Deserialize<OrderEvent>(row.PayloadJson, Json)!, row, nowUtc, ct);
                break;
            case SyncAggregate.Payment:
                await TerapkanPayment(JsonSerializer.Deserialize<PaymentEvent>(row.PayloadJson, Json)!, row, nowUtc, ct);
                break;
            case SyncAggregate.Refund:
                await TerapkanRefund(JsonSerializer.Deserialize<RefundEvent>(row.PayloadJson, Json)!, row, nowUtc, ct);
                break;
            case SyncAggregate.CreditLedger:
                await TerapkanLedger(JsonSerializer.Deserialize<CreditLedgerEvent>(row.PayloadJson, Json)!, row, nowUtc, ct);
                break;
            default:
                // Event dari versi aplikasi lain: jangan dihentikan diam-diam.
                throw new SyncConflictException($"Agregat '{row.AggregateType}' tidak dikenali sisi ini.");
        }
        await target.SaveChangesAsync(ct);
    }

    private static DateTime Terbaru(DateTime awal, IEnumerable<DateTime> lain)
    {
        var terbesar = awal;
        foreach (var t in lain) if (t > terbesar) terbesar = t;
        return terbesar;
    }

    private static void CekJam(DateTime waktuEvent, DateTime nowUtc, string label)
    {
        if (waktuEvent > nowUtc.AddSeconds(ToleransiJamDetik))
            throw new SyncRetryableException(
                $"{label} membawa waktu {waktuEvent:O} yang mendahului jam penerima — periksa jam perangkat pengirim.");
    }

    private async Task TerapkanCustomer(CustomerEvent p, SyncInbox row, DateTime nowUtc, CancellationToken ct)
    {
        CekJam(p.CreatedAtUtc, nowUtc, "Pelanggan");
        var user = await target.Users.IgnoreQueryFilters().Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.Id == p.UserId, ct);
        if (user is null)
        {
            if (await target.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedPhone == p.NormalizedPhone, ct))
                throw new SyncConflictException(
                    $"Nomor {p.NormalizedPhone} sudah dipakai akun lain di sisi ini (event {row.EventId}).");
            target.Users.Add(new User
            {
                Id = p.UserId,
                Role = p.Role,
                NormalizedPhone = p.NormalizedPhone,
                // Hash kata sandi sengaja tidak direplikasi antar database.
                PasswordHash = string.Empty,
                IsActive = p.Aktif,
                CreatedAtUtc = p.CreatedAtUtc,
                Profile = new CustomerProfile
                {
                    Id = p.CustomerId, UserId = p.UserId, Nama = p.Nama, Alamat = p.Alamat,
                },
            });
            return;
        }
        if (user.Profile is null)
            throw new SyncConflictException($"User {p.UserId} di sisi ini bukan pelanggan (event {row.EventId}).");
        // Profil pelanggan bukan catatan finansial → nilai terbaru dari event yang sah dipakai.
        user.Profile.Nama = p.Nama;
        user.Profile.Alamat = p.Alamat;
        user.IsActive = p.Aktif;
    }

    private async Task TerapkanOrder(OrderEvent p, SyncInbox row, DateTime nowUtc, CancellationToken ct)
    {
        if (p.Items is null || p.History is null)
            throw new SyncConflictException(
                $"Payload pesanan {row.EventId} tidak lengkap (format lama?) — perlu rekonsiliasi manual.");
        CekJam(p.CreatedAtUtc, nowUtc, "Pesanan");
        var pelanggan = await target.CustomerProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == p.CustomerId, ct);
        if (pelanggan is null)
            throw new SyncRetryableException($"Pelanggan {p.CustomerId} belum tiba di sisi ini.");

        var waktuTerkini = Terbaru(p.CreatedAtUtc, p.History.Select(h => h.AtUtc));
        var order = await target.Orders.IgnoreQueryFilters()
            .Include(o => o.Items).Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == p.OrderId, ct);

        if (order is null)
        {
            target.Orders.Add(new Order
            {
                Id = p.OrderId,
                OrderNumber = p.OrderNumber,
                CustomerId = p.CustomerId,
                Status = p.Status,
                PaymentStatus = p.PaymentStatus,
                Express = p.Express,
                Fulfillment = p.Fulfillment,
                EstimatedTotal = p.EstimatedTotal,
                ActualWeightKg = p.ActualWeightKg,
                ActualItemCount = p.ActualItemCount,
                FinalTotal = p.FinalTotal,
                DeliveryFee = p.DeliveryFee,
                AppliedCredit = p.AppliedCredit,
                ExpressSurchargeSnapshot = p.ExpressSurchargeSnapshot,
                Source = p.Source,
                IsCancelled = p.IsCancelled,
                CancelRequestedAtUtc = p.CancelRequestedAtUtc,
                CancelReason = p.CancelReason,
                CreatedAtUtc = p.CreatedAtUtc,
                EstimatedFinishUtc = p.EstimatedFinishUtc,
                Items = p.Items.Select(i => new OrderItem
                {
                    Id = i.Id, OrderId = p.OrderId, ServiceId = i.ServiceId,
                    ServiceNameSnapshot = i.ServiceName, UnitTypeSnapshot = i.UnitType,
                    PriceSnapshot = i.PriceSnapshot, EstimatedWeightKg = i.EstimatedWeightKg,
                    EstimatedItemCount = i.EstimatedItemCount, ActualWeightKg = i.ActualWeightKg,
                    ActualItemCount = i.ActualItemCount, Subtotal = i.Subtotal,
                    MinimumWeightKgSnapshot = i.MinimumWeightKgSnapshot,
                }).ToList(),
                StatusHistory = p.History.Select(h => new OrderStatusHistory
                {
                    Id = h.Id, OrderId = p.OrderId, FromStatus = h.FromStatus, ToStatus = h.ToStatus,
                    ActorUserId = h.ActorUserId, AtUtc = h.AtUtc, Note = h.Note,
                }).ToList(),
            });
            return;
        }

        // Replay event lama: isi pesanan yang sudah lebih baru di sisi ini tidak boleh mundur,
        // termasuk baris dan riwayatnya yang sudah ikut maju.
        if (Numbering(order) > waktuTerkini)
            return;

        foreach (var baru in p.History)
        {
            if (order.StatusHistory.Any(h => h.Id == baru.Id)) continue;
            target.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = baru.Id, OrderId = order.Id, FromStatus = baru.FromStatus, ToStatus = baru.ToStatus,
                ActorUserId = baru.ActorUserId, AtUtc = baru.AtUtc, Note = baru.Note,
            });
        }

        foreach (var item in p.Items)
        {
            var ada = order.Items.FirstOrDefault(i => i.Id == item.Id);
            if (ada is null)
            {
                target.OrderItems.Add(new OrderItem
                {
                    Id = item.Id, OrderId = order.Id, ServiceId = item.ServiceId,
                    ServiceNameSnapshot = item.ServiceName, UnitTypeSnapshot = item.UnitType,
                    PriceSnapshot = item.PriceSnapshot, EstimatedWeightKg = item.EstimatedWeightKg,
                    EstimatedItemCount = item.EstimatedItemCount, ActualWeightKg = item.ActualWeightKg,
                    ActualItemCount = item.ActualItemCount, Subtotal = item.Subtotal,
                    MinimumWeightKgSnapshot = item.MinimumWeightKgSnapshot,
                });
                continue;
            }
            // Layanan dan tarif yang dibekukan saat pesanan dibuat adalah jejak finansial — tidak pernah ditimpa.
            if (ada.ServiceId != item.ServiceId || ada.ServiceNameSnapshot != item.ServiceName
                || ada.UnitTypeSnapshot != item.UnitType || ada.PriceSnapshot != item.PriceSnapshot
                || ada.MinimumWeightKgSnapshot != item.MinimumWeightKgSnapshot)
                throw new SyncConflictException(
                    $"Baris pesanan {item.Id} berbeda antara dua sisi; perlu rekonsiliasi manual.");
            // Timbangan/jumlah nyata dan subtotal berubah saat status bergerak (estimasi → actuals → lunas),
            // jadi snapshot terbaru dari event yang sah boleh menimpa. Yang tidak boleh hanya jejak beku di atas.
            ada.EstimatedWeightKg = item.EstimatedWeightKg;
            ada.EstimatedItemCount = item.EstimatedItemCount;
            ada.ActualWeightKg = item.ActualWeightKg;
            ada.ActualItemCount = item.ActualItemCount;
            ada.Subtotal = item.Subtotal;
        }

        order.Status = p.Status;
        order.PaymentStatus = p.PaymentStatus;
        order.ActualWeightKg = p.ActualWeightKg;
        order.ActualItemCount = p.ActualItemCount;
        order.FinalTotal = p.FinalTotal;
        order.EstimatedTotal = p.EstimatedTotal;
        order.DeliveryFee = p.DeliveryFee;
        order.AppliedCredit = p.AppliedCredit;
        order.EstimatedFinishUtc = p.EstimatedFinishUtc;
        order.IsCancelled = p.IsCancelled;
        order.CancelRequestedAtUtc = p.CancelRequestedAtUtc;
        order.CancelReason = p.CancelReason;
        order.SyncVersion = row.Sequence;
    }

    private static DateTime Numbering(Order order) =>
        Terbaru(order.CreatedAtUtc, order.StatusHistory.Select(h => h.AtUtc));

    private async Task TerapkanPayment(PaymentEvent p, SyncInbox row, DateTime nowUtc, CancellationToken ct)
    {
        CekJam(p.CreatedAtUtc, nowUtc, "Pembayaran");
        var lama = await target.Payments.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == p.Id, ct);
        if (lama is not null)
        {
            // Nominal, kunci idempotensi, dan pemiliknya adalah jejak finansial: beda = konflik, bukan timpa.
            if (lama.IdempotencyKey != p.IdempotencyKey || lama.Amount != p.Amount
                || lama.OrderId != p.OrderId || lama.CustomerId != p.CustomerId
                || lama.Kind != p.Kind || lama.Method != p.Method)
                throw new SyncConflictException(
                    $"Pembayaran {p.Id} sudah tercatat dengan isi berbeda di sisi ini.");
            // Satu pembayaran diantrekan ulang saat webhook mengubah statusnya (menunggu → lunas/gagal),
            // jadi status maju mengikuti event terbaru; event lama tidak memundurkan yang sudah lunas.
            if (Terbaru(lama.CreatedAtUtc, [lama.ConfirmedAtUtc ?? lama.CreatedAtUtc])
                > Terbaru(p.CreatedAtUtc, [p.ConfirmedAtUtc ?? p.CreatedAtUtc])) return;
            lama.Status = p.Status;
            lama.ProviderReference = p.ProviderReference;
            lama.ConfirmedAtUtc = p.ConfirmedAtUtc;
            return;
        }
        if (await target.Payments.IgnoreQueryFilters().AnyAsync(x => x.IdempotencyKey == p.IdempotencyKey, ct))
            return; // kunci idempotensi sama = pembayaran yang sama
        if (!await target.Orders.IgnoreQueryFilters().AnyAsync(o => o.Id == p.OrderId, ct))
            throw new SyncRetryableException($"Pesanan {p.OrderId} dari pembayaran belum tiba.");

        target.Payments.Add(new Payment
        {
            Id = p.Id, OrderId = p.OrderId, CustomerId = p.CustomerId, Amount = p.Amount, Method = p.Method,
            Kind = p.Kind, Status = p.Status, ProviderReference = p.ProviderReference,
            IdempotencyKey = p.IdempotencyKey, RecordedByUserId = p.RecordedByUserId,
            CreatedAtUtc = p.CreatedAtUtc, ConfirmedAtUtc = p.ConfirmedAtUtc,
        });
    }

    private async Task TerapkanRefund(RefundEvent p, SyncInbox row, DateTime nowUtc, CancellationToken ct)
    {
        CekJam(p.CreatedAtUtc, nowUtc, "Refund");
        if (await target.Refunds.IgnoreQueryFilters().AnyAsync(x => x.Id == p.Id, ct))
        {
            var lama = await target.Refunds.FirstAsync(x => x.Id == p.Id, ct);
            if (lama.IdempotencyKey == p.IdempotencyKey && lama.Amount == p.Amount) return;
            throw new SyncConflictException($"Refund {p.Id} sudah tercatat dengan isi berbeda.");
        }
        if (await target.Refunds.IgnoreQueryFilters().AnyAsync(x => x.IdempotencyKey == p.IdempotencyKey, ct))
            return;
        if (!await target.Payments.IgnoreQueryFilters().AnyAsync(x => x.Id == p.PaymentId, ct))
            throw new SyncRetryableException($"Pembayaran asal refund {p.PaymentId} belum tiba.");

        target.Refunds.Add(new Refund
        {
            Id = p.Id, OrderId = p.OrderId, PaymentId = p.PaymentId, Amount = p.Amount, Reason = p.Reason,
            Status = p.Status, ProviderReference = p.ProviderReference, IdempotencyKey = p.IdempotencyKey,
            CreatedAtUtc = p.CreatedAtUtc,
        });
    }

    private async Task TerapkanLedger(CreditLedgerEvent p, SyncInbox row, DateTime nowUtc, CancellationToken ct)
    {
        CekJam(p.CreatedAtUtc, nowUtc, "Ledger kredit");
        if (await target.CreditLedger.IgnoreQueryFilters().AnyAsync(x => x.Id == p.Id, ct))
        {
            var lama = await target.CreditLedger.FirstAsync(x => x.Id == p.Id, ct);
            if (lama.IdempotencyKey == p.IdempotencyKey && lama.Delta == p.Delta) return;
            throw new SyncConflictException($"Entri ledger {p.Id} berbeda antara dua sisi.");
        }
        if (await target.CreditLedger.IgnoreQueryFilters().AnyAsync(x => x.IdempotencyKey == p.IdempotencyKey, ct))
            return;
        if (!await target.CustomerProfiles.IgnoreQueryFilters().AnyAsync(c => c.Id == p.CustomerId, ct))
            throw new SyncRetryableException($"Pelanggan {p.CustomerId} dari ledger belum tiba.");

        target.CreditLedger.Add(new CreditLedgerEntry
        {
            Id = p.Id, CustomerId = p.CustomerId, Delta = p.Delta, OrderId = p.OrderId, PaymentId = p.PaymentId,
            RefundId = p.RefundId, ActorUserId = p.ActorUserId, IdempotencyKey = p.IdempotencyKey,
            CreatedAtUtc = p.CreatedAtUtc, Reason = p.Reason,
        });
    }
}
