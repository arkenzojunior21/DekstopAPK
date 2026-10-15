using Laundry.Application.Abstractions;
using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Services;

/// <summary>
/// Pembayaran & saldo kredit. Pembayaran sukses bersifat immutable: tidak ada endpoint
/// edit/hapus; revisi total memakai ledger, bukan mengubah pembayaran lama (PRD 6.6/6.9).
/// Tagihan non-tunai dibuat lewat adapter gateway dan baru menjadi Lunas setelah webhook
/// bertanda tangan cocok — nominal yang berbeda tidak pernah ditimpa.
/// </summary>
public sealed class PaymentService(
    LaundryDbContext db,
    AuditWriter audit,
    EventWriter events,
    IPaymentGateway gateway) : IPaymentService, IPaymentGatewayService
{
    public async Task<PaymentDto> RecordCashAsync(RecordCashPaymentRequest req, Guid actorUserId, CancellationToken ct)
    {
        if (req.Amount <= 0) throw new InvalidOperationException("Nominal pembayaran harus lebih dari 0.");
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Refunds).Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        if (order.Status == OrderStatus.Dibatalkan)
            throw new InvalidOperationException("Pesanan dibatalkan; tidak dapat menerima pembayaran.");
        if (order.PaymentStatus == PaymentStatus.Lunas)
            throw new InvalidOperationException("Pesanan sudah lunas.");

        var idempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : req.IdempotencyKey;
        var dupes = await db.Payments.AnyAsync(p => p.IdempotencyKey == idempotencyKey, ct);
        if (dupes)
        {
            var asli = await db.Payments.FirstAsync(p => p.IdempotencyKey == idempotencyKey, ct);
            return Map(asli);
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Amount = PriceCalculator.RoundRupiah(req.Amount),
            Method = PaymentMethod.Tunai,
            Kind = req.Kind,
            Status = PaymentStatus.Lunas, // tunai manual = sudah diterima fisik
            RecordedByUserId = actorUserId,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow,
            ConfirmedAtUtc = DateTime.UtcNow,
        };
        db.Payments.Add(payment);
        // Change tracking bisa sudah mem-fikup navigasi; hindari duplikasi di collection.
        if (!order.Payments.Contains(payment)) order.Payments.Add(payment);
        var kreditHasilOverpay = FinalizeOrder(order, actorUserId);
        await db.SaveChangesAsync(ct);

        audit.Write("Payment", payment.Id, "dibuat", actorUserId, null,
            new { payment.OrderId, payment.Amount, payment.Method, payment.Kind }, req.Note);
        events.EnqueuePayment(payment, OrderService.DeviceId);
        foreach (var kredit in kreditHasilOverpay) events.EnqueueCreditLedger(kredit, OrderService.DeviceId);
        await events.EnqueueOrderSnapshotAsync(order.Id, "pembayaran", OrderService.DeviceId, ct);
        await db.SaveChangesAsync(ct);
        return Map(payment);
    }

    public async Task<OrderDetailDto> ApplyCreditAsync(ApplyCreditRequest req, Guid actorUserId, CancellationToken ct)
    {
        if (!req.CustomerConsent)
            throw new InvalidOperationException("Penggunaan kredit wajib persetujuan eksplisit pelanggan (PRD 6.6).");
        if (req.Amount <= 0) throw new InvalidOperationException("Nominal kredit harus lebih dari 0.");

        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Refunds).Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");

        var ledger = await db.CreditLedger.Where(e => e.CustomerId == order.CustomerId).ToListAsync(ct);
        var saldo = BillCalculator.CreditBalance(ledger, order.CustomerId);
        var remaining = BillCalculator.Remaining(order, order.Payments);
        var pakai = Math.Min(Math.Min(req.Amount, saldo), remaining);
        if (pakai <= 0) throw new InvalidOperationException("Tidak ada kredit yang dapat dipakai.");

        var kreditDipakai = new CreditLedgerEntry
        {
            CustomerId = order.CustomerId,
            Delta = -pakai,
            OrderId = order.Id,
            ActorUserId = actorUserId,
            IdempotencyKey = $"credit-apply:{order.Id}:{pakai:N0}:{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreatedAtUtc = DateTime.UtcNow,
            Reason = "Pemakaian kredit dengan persetujuan pelanggan",
        };
        db.CreditLedger.Add(kreditDipakai);
        order.AppliedCredit += pakai;
        var kreditHasilOverpay = FinalizeOrder(order, actorUserId);
        await db.SaveChangesAsync(ct);

        audit.Write("Credit", order.Id, "dipakai", actorUserId, new { saldo },
            new { dipakai = pakai, order.AppliedCredit }, "Persetujuan pelanggan eksplisit");
        events.EnqueueCreditLedger(kreditDipakai, OrderService.DeviceId);
        foreach (var kredit in kreditHasilOverpay) events.EnqueueCreditLedger(kredit, OrderService.DeviceId);
        await events.EnqueueOrderSnapshotAsync(order.Id, "kredit", OrderService.DeviceId, ct);
        await db.SaveChangesAsync(ct);
        return await MapOrderAsync(order, ct);
    }

    /// <summary>
    /// Refund kasir (PRD 6.9): append-only, tidak menghapus pembayaran asal. Batasnya nominal
    /// pembayaran yang belum di-refund. Bila order ini pernah menyumbang saldo kredit,
    /// kredit tersebut ditarik lebih dulu agar uang tidak dikembalikan dua kali.
    /// </summary>
    public async Task<RefundDto> RefundAsync(RefundRequest req, Guid actorUserId, CancellationToken ct)
    {
        if (req.Amount <= 0) throw new InvalidOperationException("Nominal refund harus lebih dari 0.");
        if (string.IsNullOrWhiteSpace(req.Reason))
            throw new InvalidOperationException("Alasan refund wajib diisi.");

        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments)
            .Include(o => o.Refunds).Include(o => o.Customer)
            .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");

        var asal = await PilihPembayaranRefundAsync(order, req.PaymentId, ct);
        var sudah = BillCalculator.RefundedPayments(order.Refunds, order.Id);
        var sudahPerPembayaran = order.Refunds
            .Where(r => r.PaymentId == asal.Id && r.Status == PaymentStatus.Lunas).Sum(r => r.Amount);
        var kapasitas = asal.Amount - sudahPerPembayaran;
        if (kapasitas <= 0) throw new InvalidOperationException("Pembayaran ini sudah di-refund seluruhnya.");
        if (req.Amount > kapasitas)
            throw new InvalidOperationException(
                $"Refund maksimal {kapasitas:N0} untuk pembayaran terpilih.");

        var idempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : req.IdempotencyKey;
        var duplikat = await db.Refunds.FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        if (duplikat is not null) return await Map(duplikat, ct);

        var ledger = await db.CreditLedger.Where(e => e.CustomerId == order.CustomerId).ToListAsync(ct);
        var kreditDariOrder = Math.Max(ledger.Where(e => e.OrderId == order.Id).Sum(e => e.Delta), 0m);
        var penarikan = Math.Min(Math.Min(req.Amount, kreditDariOrder), BillCalculator.CreditBalance(ledger, order.CustomerId));
        if (kreditDariOrder > 0 && penarikan < Math.Min(req.Amount, kreditDariOrder))
            throw new InvalidOperationException(
                "Sebagian kelebihan bayar order ini sudah terpakai; selesaikan saldo pelanggan sebelum refund.");

        var refund = new Refund
        {
            OrderId = order.Id,
            PaymentId = asal.Id,
            Amount = PriceCalculator.RoundRupiah(req.Amount),
            Reason = req.Reason.Trim(),
            Status = PaymentStatus.Lunas, // pengembalian tunai di kasir = selesai
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Refunds.Add(refund);
        CreditLedgerEntry? kreditDitarik = null;
        if (penarikan > 0)
        {
            kreditDitarik = new CreditLedgerEntry
            {
                CustomerId = order.CustomerId,
                Delta = -penarikan,
                OrderId = order.Id,
                RefundId = refund.Id,
                ActorUserId = actorUserId,
                IdempotencyKey = $"refund-kredit:{refund.Id}",
                CreatedAtUtc = DateTime.UtcNow,
                Reason = "Penarikan kelebihan bayar karena refund",
            };
            db.CreditLedger.Add(kreditDitarik);
        }

        order.PaymentStatus = sudah + refund.Amount
            >= BillCalculator.SuccessfulPayments(order.Payments, order.Id)
            ? PaymentStatus.RefundPenuh
            : PaymentStatus.RefundSebagian;
        await db.SaveChangesAsync(ct);

        audit.Write("Refund", refund.Id, "dibuat", actorUserId, new { refunded = sudah },
            new { refund.OrderId, refund.PaymentId, refund.Amount, penarikan }, refund.Reason);
        events.EnqueueRefund(refund, OrderService.DeviceId);
        if (kreditDitarik is not null) events.EnqueueCreditLedger(kreditDitarik, OrderService.DeviceId);
        await events.EnqueueOrderSnapshotAsync(order.Id, "refund", OrderService.DeviceId, ct);
        await db.SaveChangesAsync(ct);
        return await Map(refund, ct);
    }

    private async Task<Payment> PilihPembayaranRefundAsync(Order order, Guid? paymentId, CancellationToken ct)
    {
        if (paymentId is { } id)
        {
            return order.Payments.FirstOrDefault(p => p.Id == id && p.OrderId == order.Id)
                ?? await db.Payments.FirstOrDefaultAsync(p => p.Id == id && p.OrderId == order.Id, ct)
                ?? throw new KeyNotFoundException("Pembayaran tidak ditemukan.");
        }
        var lunas = order.Payments.Where(p => p.OrderId == order.Id && p.Status == PaymentStatus.Lunas)
            .OrderByDescending(p => p.CreatedAtUtc).ToList();
        foreach (var p in lunas)
        {
            var dipakai = order.Refunds.Where(r => r.PaymentId == p.Id && r.Status == PaymentStatus.Lunas)
                .Sum(r => r.Amount);
            if (p.Amount - dipakai > 0) return p;
        }
        throw new InvalidOperationException("Tidak ada pembayaran tunai yang dapat di-refund.");
    }

    public async Task<List<PaymentDto>> ListForOrderAsync(Guid orderId, Guid? limitToCustomerId, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        if (limitToCustomerId is not null && order.CustomerId != limitToCustomerId)
            throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        return await db.Payments.Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new PaymentDto(p.Id, p.OrderId, p.Amount, p.Method, p.Kind, p.Status,
                p.ProviderReference, p.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<decimal> CreditBalanceAsync(Guid customerId, CancellationToken ct)
    {
        var entries = await db.CreditLedger.Where(e => e.CustomerId == customerId).ToListAsync(ct);
        return BillCalculator.CreditBalance(entries, customerId);
    }

    /// <summary>
    /// Tagihan non-tunai dibuat berstatus MenungguKonfirmasi. Sistem tidak pernah mengklaim
    /// pembayaran atas nama gateway: status berubah hanya lewat webhook bertanda tangan.
    /// </summary>
    public async Task<PaymentQuoteDto> CreateQuoteAsync(CreateGatewayPaymentRequest req, Guid actorUserId,
        CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Refunds)
            .Include(o => o.Customer).ThenInclude(c => c!.User)
            .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        if (order.Status == OrderStatus.Dibatalkan)
            throw new InvalidOperationException("Pesanan dibatalkan; tagihan tidak dapat dibuat.");
        var nominal = BillCalculator.Remaining(order, order.Payments);
        if (nominal <= 0) throw new InvalidOperationException("Tidak ada sisa tagihan yang dapat dibuat.");

        var payment = new Payment
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Amount = PriceCalculator.RoundRupiah(nominal),
            Method = PaymentMethod.Qris,
            Kind = req.Kind,
            Status = PaymentStatus.MenungguKonfirmasi,
            RecordedByUserId = actorUserId,
            IdempotencyKey = $"qris:{order.Id}:{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Payments.Add(payment);
        if (!order.Payments.Contains(payment)) order.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        var quote = await gateway.CreateQuoteAsync(new OrderPaymentRequest(
            order.Id, payment.Id, order.OrderNumber, order.Customer?.Nama ?? "", payment.Amount,
            order.Customer?.User?.NormalizedPhone ?? string.Empty), ct);
        payment.ProviderReference = quote.Reference;
        await db.SaveChangesAsync(ct);

        audit.Write("Payment", payment.Id, "tagihan-dibuat", actorUserId, null,
            new { payment.OrderId, payment.Amount, payment.Method, quote.Reference },
            "Tagihan non-tunai menunggu konfirmasi gateway", source: quote.GatewayName);
        events.EnqueuePayment(payment, OrderService.DeviceId);
        await db.SaveChangesAsync(ct);

        return new PaymentQuoteDto(payment.Id, order.Id, quote.GatewayName, quote.Reference, payment.Amount,
            quote.ExpiresAtUtc, quote.InstructionsJson, quote.IsLive);
    }

    /// <summary>
    /// Konfirmasi webhook: idempoten (webhook ganda → Duplikat, tanpa pembayaran kedua),
    /// dan nominal yang tidak sama dengan tagihan dicatat sebagai penolakan, bukan ditimpa.
    /// </summary>
    public async Task<GatewayWebhookOutcome> HandleWebhookAsync(GatewayPaymentUpdate update, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == update.PaymentId, ct);
        if (payment is null)
            return new(GatewayWebhookStatus.Ditolak, update.PaymentId, "Nomor tagihan tidak dikenal sistem.");
        if (!string.IsNullOrEmpty(update.Reference) && update.Reference != payment.ProviderReference)
            return new(GatewayWebhookStatus.Ditolak, payment.Id, "Referensi tagihan tidak cocok.");
        if (payment.Status == PaymentStatus.Lunas)
            return new(GatewayWebhookStatus.Duplikat, payment.Id, "Webhook sudah pernah diproses.");
        if (payment.Status == PaymentStatus.GagalAtauKedaluwarsa)
            return new(GatewayWebhookStatus.Duplikat, payment.Id, "Tagihan sudah ditutup sebagai gagal.");
        if (payment.Status != PaymentStatus.MenungguKonfirmasi)
            return new(GatewayWebhookStatus.Ditolak, payment.Id,
                $"Status pembayaran {payment.Status} tidak dapat diubah webhook.");

        var actor = payment.RecordedByUserId ?? Guid.Empty;
        if (!update.Paid)
        {
            payment.Status = PaymentStatus.GagalAtauKedaluwarsa;
            await db.SaveChangesAsync(ct);
            audit.Write("Payment", payment.Id, "gagal", actor,
                new { status = PaymentStatus.MenungguKonfirmasi.ToString() },
                new { status = payment.Status.ToString(), update.FailureReason },
                update.FailureReason, source: gateway.Name);
            events.EnqueuePayment(payment, OrderService.DeviceId);
            await db.SaveChangesAsync(ct);
            return new(GatewayWebhookStatus.Gagal, payment.Id, update.FailureReason);
        }

        if (update.Amount != payment.Amount)
        {
            audit.Write("Payment", payment.Id, "webhook-nominal-bedanya", actor,
                new { diharapkan = payment.Amount }, new { diterima = update.Amount },
                "Nominal webhook berbeda dari tagihan; pembayaran tidak diubah.", source: gateway.Name);
            await db.SaveChangesAsync(ct);
            return new(GatewayWebhookStatus.Ditolak, payment.Id,
                $"Nominal webhook {update.Amount:N0} berbeda dari tagihan {payment.Amount:N0}.");
        }

        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments).Include(o => o.Refunds)
            .Include(o => o.Customer).ThenInclude(c => c!.User)
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        if (order.Status == OrderStatus.Dibatalkan)
            return new(GatewayWebhookStatus.Ditolak, payment.Id,
                "Pesanan sudah dibatalkan; pembayaran tidak dicatat.");

        payment.Status = PaymentStatus.Lunas;
        payment.ConfirmedAtUtc = DateTime.UtcNow;
        if (!order.Payments.Contains(payment)) order.Payments.Add(payment);
        var kreditHasilOverpay = FinalizeOrder(order, actor);
        await db.SaveChangesAsync(ct);

        audit.Write("Payment", payment.Id, "dibayar", actor,
            new { status = PaymentStatus.MenungguKonfirmasi.ToString() },
            new { payment.OrderId, payment.Amount, payment.Method, update.Reference },
            "Konfirmasi webhook gateway", source: gateway.Name);
        events.EnqueuePayment(payment, OrderService.DeviceId);
        foreach (var kredit in kreditHasilOverpay) events.EnqueueCreditLedger(kredit, OrderService.DeviceId);
        await events.EnqueueOrderSnapshotAsync(order.Id, "pembayaran", OrderService.DeviceId, ct);
        events.EnqueueNotification("pembayaran_diterima",
            order.Customer?.User?.NormalizedPhone ?? string.Empty, $"{payment.Id}:lunas",
            new { orderNumber = order.OrderNumber, payment.Amount });
        await db.SaveChangesAsync(ct);

        return new(GatewayWebhookStatus.Disimpan, payment.Id, null);
    }

    /// <summary>Sinkronkan status pembayaran + kelebihan bayar menjadi kredit (append-only).</summary>
    private List<CreditLedgerEntry> FinalizeOrder(Order order, Guid actorUserId)
    {
        var baru = new List<CreditLedgerEntry>();
        decimal paid = BillCalculator.Paid(order, order.Refunds);
        var over = Math.Max(paid - order.FinalTotal, 0m);
        if (over > 0)
        {
            var kredit = new CreditLedgerEntry
            {
                CustomerId = order.CustomerId,
                Delta = over,
                OrderId = order.Id,
                ActorUserId = actorUserId,
                IdempotencyKey = $"overpay:{order.Id}:{order.FinalTotal:N0}",
                CreatedAtUtc = DateTime.UtcNow,
                Reason = "Kelebihan bayar",
            };
            db.CreditLedger.Add(kredit);
            baru.Add(kredit);
            paid -= over;
        }
        order.PaymentStatus = order.FinalTotal > 0 && paid >= order.FinalTotal
            ? PaymentStatus.Lunas
            : BillCalculator.DerivePaymentStatus(order, order.Payments);
        return baru;
    }

    private async Task<OrderDetailDto> MapOrderAsync(Order o, CancellationToken ct)
    {
        decimal paid = BillCalculator.Paid(o, o.Refunds);
        var history = await db.OrderStatusHistories.Where(h => h.OrderId == o.Id)
            .OrderByDescending(h => h.AtUtc).ToListAsync(ct);
        return new OrderDetailDto(o.Id, o.OrderNumber, o.CustomerId, o.Customer?.Nama ?? "",
            o.Status, o.PaymentStatus, o.Express, o.Fulfillment, o.Customer?.Alamat,
            o.EstimatedTotal, o.ActualWeightKg, o.ActualItemCount, o.FinalTotal, paid,
            Math.Max(o.FinalTotal - paid, 0m), o.AppliedCredit,
            BillCalculator.RefundedPayments(o.Refunds, o.Id), o.DeliveryFee,
            o.CreatedAtUtc, o.EstimatedFinishUtc, o.Source,
            o.Items.Select(i => new OrderItemDto(i.Id, i.ServiceNameSnapshot, i.UnitTypeSnapshot,
                i.PriceSnapshot, i.EstimatedWeightKg, i.EstimatedItemCount, i.ActualWeightKg,
                i.ActualItemCount, i.Subtotal)).ToList(),
            history.Select(h => new StatusHistoryDto(h.FromStatus, h.ToStatus, h.ActorUserId, h.AtUtc, h.Note)).ToList());
    }

    private async Task<RefundDto> Map(Refund r, CancellationToken ct)
    {
        var metode = await db.Payments.Where(p => p.Id == r.PaymentId).Select(p => p.Method)
            .FirstOrDefaultAsync(ct);
        return new RefundDto(r.Id, r.OrderId, r.PaymentId, r.Amount, metode, r.Reason, r.Status, r.CreatedAtUtc);
    }

    private static PaymentDto Map(Payment p) =>
        new(p.Id, p.OrderId, p.Amount, p.Method, p.Kind, p.Status, p.ProviderReference, p.CreatedAtUtc);
}

public sealed class ComplaintService(
    LaundryDbContext db,
    EventWriter events) : IComplaintService
{
    public async Task<ComplaintDto> CreateAsync(CreateComplaintRequest req, Guid customerId, CancellationToken ct)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == req.OrderId && o.CustomerId == customerId, ct)
            ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        if (string.IsNullOrWhiteSpace(req.Description))
            throw new InvalidOperationException("Deskripsi komplain wajib diisi.");
        var complaint = new Complaint
        {
            OrderId = order.Id,
            CustomerId = customerId,
            Category = req.Category,
            Description = req.Description.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Complaints.Add(complaint);
        await db.SaveChangesAsync(ct);
        await events.EnqueueAdminNotificationAsync("komplain_baru", $"{complaint.Id}:baru",
            new { order.OrderNumber, req.Category }, ct);
        await db.SaveChangesAsync(ct);
        return Map(complaint);
    }

    public async Task<ComplaintDto> ReplyAsync(Guid complaintId, string body, Guid senderUserId, bool isAdmin,
        CancellationToken ct)
    {
        var complaint = await db.Complaints.Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == complaintId, ct)
            ?? throw new KeyNotFoundException("Komplain tidak ditemukan.");
        if (string.IsNullOrWhiteSpace(body)) throw new InvalidOperationException("Pesan wajib diisi.");
        complaint.Messages.Add(new ComplaintMessage
        {
            ComplaintId = complaint.Id, SenderUserId = senderUserId, Body = body.Trim(),
            SentAtUtc = DateTime.UtcNow,
        });
        if (isAdmin && complaint.Status == ComplaintStatus.Baru) complaint.Status = ComplaintStatus.Ditinjau;
        await db.SaveChangesAsync(ct);
        return Map(complaint);
    }

    public async Task<ComplaintDto> SetStatusAsync(Guid complaintId, ComplaintStatus status, Guid actorUserId,
        CancellationToken ct)
    {
        _ = actorUserId;
        var complaint = await db.Complaints.Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == complaintId, ct)
            ?? throw new KeyNotFoundException("Komplain tidak ditemukan.");
        complaint.Status = status;
        await db.SaveChangesAsync(ct);
        await events.EnqueueCustomerNotificationAsync($"komplain_{status}", complaint.CustomerId,
            $"{complaint.Id}:{status}", new { complaintId = complaint.Id, status = status.ToString() }, ct);
        await db.SaveChangesAsync(ct);
        return Map(complaint);
    }

    public async Task<List<ComplaintDto>> ListAsync(Guid? customerId, ComplaintStatus? status, CancellationToken ct)
    {
        var q = db.Complaints.Include(c => c.Messages).AsQueryable();
        if (customerId is { } cid) q = q.Where(c => c.CustomerId == cid);
        if (status is { } st) q = q.Where(c => c.Status == st);
        return (await q.OrderByDescending(c => c.CreatedAtUtc).ToListAsync(ct)).Select(Map).ToList();
    }

    private static ComplaintDto Map(Complaint c) => new(c.Id, c.OrderId, c.CustomerId, c.Category,
        c.Description, c.Status, c.CreatedAtUtc,
        c.Messages.OrderBy(m => m.SentAtUtc)
            .Select(m => new ComplaintMessageDto(m.Id, m.SenderUserId, m.Body, m.SentAtUtc)).ToList());
}
