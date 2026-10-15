using System.Text.Json;
using System.Text.Json.Serialization;
using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Infrastructure.Services;

/// <summary>
/// Use case pesanan. Aturan kritis PRD ditegakkan di sini (sisi server):
/// snapshot tarif, rekalkulasi berat aktual tanpa mengubah pembayaran sukses,
/// tidak ada penyerahan sebelum lunas, pembatalan hanya sebelum diproses.
/// </summary>
public sealed class OrderService(
    LaundryDbContext db,
    AuditWriter audit,
    EventWriter events,
    SettingsStore settings) : IOrderService
{
    public const string DeviceId = "admin-local";
    public const string BusinessNameKey = "business_name";

    private static readonly JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private async Task<Order> LoadAsync(Guid orderId, CancellationToken ct) =>
        await db.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.Customer).ThenInclude(p => p!.User)
            .Include(o => o.Payments).Include(o => o.Refunds)
            .Include(o => o.FulfillmentTasks)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
        ?? throw new KeyNotFoundException("Pesanan tidak ditemukan.");

    private async Task<Guid> ResolveCustomerAsync(CreateOrderRequest req, CancellationToken ct)
    {
        if (req.CustomerId is { } id)
        {
            return await db.CustomerProfiles.Where(p => p.Id == id).Select(p => p.Id)
                .FirstOrDefaultAsync(ct) is Guid ok ? ok : throw new KeyNotFoundException("Pelanggan tidak ditemukan.");
        }
        var normalized = Laundry.Application.Auth.PhoneNormalizer.Normalize(req.WalkInPhone)
            ?? throw new InvalidOperationException("Nomor WhatsApp tidak valid.");
        var existing = await db.Users.Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.NormalizedPhone == normalized && u.Role == UserRole.Pelanggan, ct);
        if (existing?.Profile is not null) return existing.Profile.Id;
        var created = await db.Users.Include(u => u.Profile)
            .FirstOrDefaultAsync(u => u.NormalizedPhone == normalized, ct);
        if (created?.Profile is { } p) return p.Id;

        var user = new User
        {
            Role = UserRole.Pelanggan,
            NormalizedPhone = normalized,
            PasswordHash = string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            Profile = new CustomerProfile { Nama = (req.WalkInNama ?? "Pelanggan Tanpa Akun").Trim() },
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        events.EnqueueCustomer(user.Profile, user, DeviceId);
        return user.Profile.Id;
    }

    private async Task<Order> BuildAsync(CreateOrderRequest req, string source, CancellationToken ct)
    {
        if (req.Items is null || req.Items.Count == 0)
            throw new InvalidOperationException("Pesanan minimal satu item.");
        var customerId = await ResolveCustomerAsync(req, ct);
        var now = DateTime.UtcNow;

        var serviceIds = req.Items.Select(i => i.ServiceId).Distinct().ToList();
        var services = await db.Services.Include(s => s.PriceVersions)
            .Where(s => serviceIds.Contains(s.Id)).ToListAsync(ct);

        var order = new Order
        {
            OrderNumber = OrderNumberGenerator.New(),
            CustomerId = customerId,
            Fulfillment = req.Fulfillment,
            Express = req.Express,
            Source = source,
            CreatedAtUtc = now,
            Status = source == "cloud" ? OrderStatus.MenungguKonfirmasi : OrderStatus.Dikonfirmasi,
        };

        decimal deliveryFee = 0m;
        if (req.Fulfillment == FulfillmentMethod.AntarJemput)
        {
            deliveryFee = await settings.GetDecimalAsync(CatalogService.DeliveryFeeKey, 0m, ct);
            if (req.SlotId is null) throw new InvalidOperationException("Antar-jemput wajib memilih slot.");
            if (string.IsNullOrWhiteSpace(req.Alamat)) throw new InvalidOperationException("Alamat wajib diisi.");
            var slot = await db.PickupDeliverySlots.FirstOrDefaultAsync(s => s.Id == req.SlotId, ct)
                ?? throw new KeyNotFoundException("Slot tidak ditemukan.");
            if (!slot.IsActive || slot.StartsAtUtc < now) throw new InvalidOperationException("Slot tidak tersedia.");
            if (slot.BookedCount >= slot.Capacity) throw new InvalidOperationException("Kapasitas slot penuh.");
            slot.BookedCount++;
            db.FulfillmentTasks.Add(new FulfillmentTask
            {
                OrderId = order.Id,
                Method = FulfillmentMethod.AntarJemput,
                IsPickup = true,
                Address = req.Alamat!.Trim(),
                SlotId = slot.Id,
                CreatedAtUtc = now,
            });
        }
        order.DeliveryFee = deliveryFee;

        foreach (var input in req.Items)
        {
            var service = services.FirstOrDefault(s => s.Id == input.ServiceId)
                ?? throw new KeyNotFoundException("Layanan tidak ditemukan.");
            if (!service.IsActive) throw new InvalidOperationException($"Layanan '{service.Name}' tidak aktif.");
            var price = service.EffectivePrice(now);

            if (service.UnitType == ServiceUnitType.PerKg && input.EstimatedWeightKg is not > 0)
                throw new InvalidOperationException($"Berat estimasi '{service.Name}' wajib diisi.");
            if (service.UnitType == ServiceUnitType.PerItem && input.EstimatedItemCount is not > 0)
                throw new InvalidOperationException($"Jumlah item estimasi '{service.Name}' wajib diisi.");

            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ServiceId = service.Id,
                ServiceNameSnapshot = service.Name,
                UnitTypeSnapshot = service.UnitType,
                PriceSnapshot = price.Price,
                MinimumWeightKgSnapshot = service.MinimumWeightKg,
                EstimatedWeightKg = input.EstimatedWeightKg,
                EstimatedItemCount = input.EstimatedItemCount,
            });
        }

        // Biaya express per order: memakai surcharge terbesar dari layanan terpilih (rekomendasi implementasi).
        order.ExpressSurchargeSnapshot = order.Express
            ? services.Select(s => s.EffectivePrice(now).ExpressSurcharge).DefaultIfEmpty(0m).Max()
            : 0m;

        var maxDays = order.Express
            ? services.Select(s => s.EffectivePrice(now).ExpressDurationDays).DefaultIfEmpty(1).Max()
            : services.Select(s => s.EffectivePrice(now).StandardDurationDays).DefaultIfEmpty(3).Max();
        order.EstimatedFinishUtc = now.AddDays(maxDays);

        order.EstimatedTotal = PriceCalculator.OrderTotal(order, useActual: false);
        order.FinalTotal = order.EstimatedTotal;
        return order;
    }

    private async Task<OrderDetailDto> CreatedAsync(Order order, Guid actor, CancellationToken ct)
    {
        db.Orders.Add(order);
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id, FromStatus = order.Status, ToStatus = order.Status,
            ActorUserId = actor, AtUtc = DateTime.UtcNow, Note = "Pesanan dibuat",
        });
        await db.SaveChangesAsync(ct);

        audit.Write("Order", order.Id, "dibuat", actor, null, Shallow(order), source: order.Source);
        await events.EnqueueOrderSnapshotAsync(order.Id, "created", DeviceId, ct);
        var phone = await db.Users.Where(u => u.Profile!.Id == order.CustomerId)
            .Select(u => u.NormalizedPhone).FirstOrDefaultAsync(ct) ?? string.Empty;
        events.EnqueueNotification("order_dibuat", phone, $"{order.Id}:dibuat",
            new { orderNumber = order.OrderNumber, total = order.EstimatedTotal });
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDetailDto> CreateWalkInAsync(CreateOrderRequest req, Guid actorUserId, CancellationToken ct)
    {
        var order = await BuildAsync(req, "lokal", ct);
        return await CreatedAsync(order, actorUserId, ct);
    }

    public async Task<OrderDetailDto> CreateOnlineAsync(CreateOrderRequest req, Guid customerId, CancellationToken ct)
    {
        var order = await BuildAsync(req with { CustomerId = customerId }, "cloud", ct);
        return await CreatedAsync(order, customerId, ct);
    }

    public async Task<OrderDetailDto> TransitionAsync(Guid orderId, OrderStatus to, Guid actorUserId, string? note,
        CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        var from = order.Status;
        if (!OrderStateMachine.CanTransition(from, to))
            throw new InvalidOperationException($"Transisi {from} → {to} tidak diizinkan.");

        decimal paid = BillCalculator.Paid(order, order.Refunds);
        decimal remaining = Math.Max(order.FinalTotal - paid, 0m);
        var derived = remaining == 0 && order.FinalTotal > 0 ? PaymentStatus.Lunas : order.PaymentStatus;
        if (to == OrderStatus.Selesai && derived != PaymentStatus.Lunas)
            throw new InvalidOperationException("Pesanan belum lunas dan tidak dapat diserahkan.");

        order.Status = to;
        order.PaymentStatus = derived;
        db.OrderStatusHistories.Add(new OrderStatusHistory
        {
            OrderId = order.Id, FromStatus = from, ToStatus = to,
            ActorUserId = actorUserId, AtUtc = DateTime.UtcNow, Note = note,
        });
        await db.SaveChangesAsync(ct);

        audit.Write("Order", order.Id, "status", actorUserId, new { status = from.ToString() },
            new { status = to.ToString() }, note, source: order.Source);
        await events.EnqueueOrderSnapshotAsync(order.Id, "status", DeviceId, ct);
        var phone = order.Customer?.User?.NormalizedPhone ?? string.Empty;
        events.EnqueueNotification($"status_{to}", phone, $"{order.Id}:{to}",
            new { orderNumber = order.OrderNumber, status = to.ToString() });
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDetailDto> RecordActualsAsync(Guid orderId, List<ActualItemInput> actuals,
        Guid actorUserId, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        if (order.Status is not (OrderStatus.DiterimaDiLaundry or OrderStatus.DitimbangDanDiverifikasi
                or OrderStatus.MenungguProses or OrderStatus.SedangDiproses))
            throw new InvalidOperationException("Berat aktual hanya dicatat sejak pesanan diterima hingga sedang diproses.");

        var before = new { order.FinalTotal, order.ActualWeightKg, order.ActualItemCount };
        foreach (var a in actuals)
        {
            var item = order.Items.FirstOrDefault(i => i.Id == a.OrderItemId)
                ?? throw new KeyNotFoundException("Item pesanan tidak ditemukan.");
            if (item.UnitTypeSnapshot == ServiceUnitType.PerKg)
            {
                if (a.ActualWeightKg is not > 0) throw new InvalidOperationException("Berat aktual wajib > 0.");
                item.ActualWeightKg = a.ActualWeightKg;
            }
            else if (item.UnitTypeSnapshot == ServiceUnitType.PerItem)
            {
                if (a.ActualItemCount is not > 0) throw new InvalidOperationException("Jumlah aktual wajib > 0.");
                item.ActualItemCount = a.ActualItemCount;
            }
        }
        order.ActualWeightKg = order.Items.Where(i => i.UnitTypeSnapshot == ServiceUnitType.PerKg)
            .Select(i => i.ActualWeightKg ?? 0m).Sum();
        order.ActualItemCount = order.Items.Where(i => i.UnitTypeSnapshot == ServiceUnitType.PerItem)
            .Select(i => i.ActualItemCount ?? 0m).Sum();

        // Total final dari snapshot tarif — pembayaran sukses tidak pernah diubah (PRD 6.6).
        order.FinalTotal = PriceCalculator.Recalculate(order, useActual: true);

        decimal paid = BillCalculator.Paid(order, order.Refunds);
        decimal over = Math.Max(paid - order.FinalTotal, 0m);
        if (over > 0)
        {
            db.CreditLedger.Add(new CreditLedgerEntry
            {
                CustomerId = order.CustomerId,
                Delta = over,
                OrderId = order.Id,
                ActorUserId = actorUserId,
                IdempotencyKey = $"overpay:{order.Id}:{order.FinalTotal:N0}",
                CreatedAtUtc = DateTime.UtcNow,
                Reason = "Kelebihan bayar akibat revisi total",
            });
        }
        order.PaymentStatus = BillCalculator.DerivePaymentStatus(order, order.Payments);

        if (order.Status == OrderStatus.DiterimaDiLaundry)
        {
            order.Status = OrderStatus.DitimbangDanDiverifikasi;
            db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id, FromStatus = OrderStatus.DiterimaDiLaundry,
                ToStatus = OrderStatus.DitimbangDanDiverifikasi,
                ActorUserId = actorUserId, AtUtc = DateTime.UtcNow, Note = "Ditimbang dan diverifikasi",
            });
        }
        await db.SaveChangesAsync(ct);

        audit.Write("Order", order.Id, "total_direvisi", actorUserId, before,
            new { order.FinalTotal, order.ActualWeightKg, order.ActualItemCount }, source: order.Source);
        await events.EnqueueOrderSnapshotAsync(order.Id, "actuals", DeviceId, ct);
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDetailDto> UpdateEstimateAsync(Guid orderId, DateTime? estimatedFinishUtc, string? note,
        Guid actorUserId, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        var before = order.EstimatedFinishUtc;
        order.EstimatedFinishUtc = estimatedFinishUtc;
        await db.SaveChangesAsync(ct);
        audit.Write("Order", order.Id, "estimasi_diubah", actorUserId,
            new { before }, new { after = estimatedFinishUtc }, note, source: order.Source);
        await events.EnqueueOrderSnapshotAsync(order.Id, "estimate", DeviceId, ct);
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDetailDto> RequestCancellationAsync(Guid orderId, Guid customerId, string reason,
        CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        if (order.CustomerId != customerId) throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        if (!OrderStateMachine.CanCancel(order.Status))
            throw new InvalidOperationException("Pesanan sudah mulai diproses; pembatalan ditolak sistem.");
        order.CancelRequestedAtUtc = DateTime.UtcNow;
        order.CancelReason = reason;
        await db.SaveChangesAsync(ct);
        await events.EnqueueAdminNotificationAsync("pembatalan_diminta", $"{order.Id}:cancelreq",
            new { orderNumber = order.OrderNumber, reason }, ct);
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDetailDto> DecideCancellationAsync(Guid orderId, bool approve, Guid actorUserId,
        string? note, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        if (order.CancelRequestedAtUtc is null)
            throw new InvalidOperationException("Tidak ada pengajuan pembatalan untuk pesanan ini.");
        if (approve)
        {
            if (!OrderStateMachine.CanCancel(order.Status))
                throw new InvalidOperationException("Pesanan terlanjur diproses; penyetujuan pembatalan ditolak.");
            if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.Dibatalkan))
                throw new InvalidOperationException($"Transisi {order.Status} → Dibatalkan tidak diizinkan.");
            var from = order.Status;
            order.Status = OrderStatus.Dibatalkan;
            order.IsCancelled = true;
            db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id, FromStatus = from, ToStatus = OrderStatus.Dibatalkan,
                ActorUserId = actorUserId, AtUtc = DateTime.UtcNow, Note = note ?? order.CancelReason,
            });
        }
        else
        {
            order.CancelRequestedAtUtc = null;
            order.CancelReason = null;
        }
        await db.SaveChangesAsync(ct);
        audit.Write("Order", order.Id, approve ? "dibatalkan" : "pembatalan_ditolak", actorUserId,
            null, Shallow(order), note, source: order.Source);
        await events.EnqueueOrderSnapshotAsync(order.Id, "cancellation", DeviceId, ct);
        await events.EnqueueCustomerNotificationAsync(
            approve ? "pembatalan_disetujui" : "pembatalan_ditolak", order.CustomerId, $"{order.Id}:keputusan",
            new { orderNumber = order.OrderNumber, disetujui = approve }, ct);
        await db.SaveChangesAsync(ct);
        return Map(order);
    }

    public async Task<OrderDetailDto> GetAsync(Guid orderId, Guid? limitToCustomerId, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        // Pelanggan hanya melihat miliknya; respons 404 agar tidak membocorkan keberadaan data (IDOR).
        if (limitToCustomerId is not null && order.CustomerId != limitToCustomerId)
            throw new KeyNotFoundException("Pesanan tidak ditemukan.");
        return Map(order);
    }

    /// <summary>
    /// Struk diambil sebagai snapshot (ReceiptSnapshot) pada permintaan pertama, supaya cetak ulang
    /// menghasilkan lembar yang identik meski total/pembayaran berubah setelahnya (PRD 6.13).
    /// </summary>
    public async Task<ReceiptDto> GetReceiptAsync(Guid orderId, Guid? limitToCustomerId, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        if (limitToCustomerId is not null && order.CustomerId != limitToCustomerId)
            throw new KeyNotFoundException("Pesanan tidak ditemukan.");

        var nomor = $"STRUK-{order.OrderNumber}";
        var tersimpan = await db.ReceiptSnapshots.FirstOrDefaultAsync(r => r.ReceiptNumber == nomor, ct);
        if (tersimpan is not null)
            return JsonSerializer.Deserialize<ReceiptDto>(tersimpan.PayloadJson, SnapshotJson)
                ?? throw new InvalidOperationException("Isi snapshot struk tidak dapat dibaca.");

        var struk = await SusunStrukAsync(order, ct);
        db.ReceiptSnapshots.Add(new ReceiptSnapshot
        {
            OrderId = order.Id,
            ReceiptNumber = nomor,
            SchemaVersion = 1,
            PayloadJson = JsonSerializer.Serialize(struk, SnapshotJson),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return struk;
    }

    private async Task<ReceiptDto> SusunStrukAsync(Order order, CancellationToken ct)
    {
        var garis = order.Items.Select(i => i.UnitTypeSnapshot switch
        {
            ServiceUnitType.PerKg => new ReceiptLineDto(i.ServiceNameSnapshot, "kg", i.PriceSnapshot,
                i.ActualWeightKg ?? i.EstimatedWeightKg, i.Subtotal),
            ServiceUnitType.PerItem => new ReceiptLineDto(i.ServiceNameSnapshot, "item", i.PriceSnapshot,
                i.ActualItemCount ?? i.EstimatedItemCount, i.Subtotal),
            _ => new ReceiptLineDto(i.ServiceNameSnapshot, "paket", i.PriceSnapshot, 1m, i.Subtotal),
        }).ToList();

        var kasirId = order.Payments.Where(p => p.RecordedByUserId != null)
            .OrderByDescending(p => p.CreatedAtUtc).Select(p => p.RecordedByUserId).FirstOrDefault();
        string? petugas = kasirId is { } id
            ? await db.Users.Where(u => u.Id == id).Select(u => u.Profile!.Nama).FirstOrDefaultAsync(ct)
            : null;

        var dibayar = BillCalculator.Paid(order, order.Refunds);
        return new ReceiptDto(
            order.Id, order.OrderNumber, await settings.GetStringAsync(BusinessNameKey, ct),
            order.Customer?.Nama ?? "", order.Customer?.User?.NormalizedPhone,
            order.Status, order.PaymentStatus, order.Express, order.Fulfillment,
            order.FulfillmentTasks?.FirstOrDefault(t => t.IsPickup)?.Address ?? order.Customer?.Alamat,
            order.CreatedAtUtc, order.EstimatedFinishUtc, garis,
            garis.Sum(g => g.Subtotal), order.DeliveryFee, order.FinalTotal,
            order.Payments.Select(p => new ReceiptPaymentDto(p.Method, p.Amount, p.CreatedAtUtc, p.Status))
                .OrderBy(p => p.AtUtc).ToList(),
            BillCalculator.RefundedPayments(order.Refunds, order.Id), order.AppliedCredit, dibayar,
            Math.Max(order.FinalTotal - dibayar, 0m), petugas, DateTime.UtcNow);
    }

    public async Task<List<OrderListItem>> SearchAsync(string? query, OrderStatus? status, PaymentStatus? payment,
        Guid? customerId, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct)
    {
        var q = db.Orders.Include(o => o.Customer).AsQueryable();
        if (customerId is { } cid) q = q.Where(o => o.CustomerId == cid);
        if (status is { } st) q = q.Where(o => o.Status == st);
        if (payment is { } pay) q = q.Where(o => o.PaymentStatus == pay);
        if (fromUtc is { } f) q = q.Where(o => o.CreatedAtUtc >= f);
        if (toUtc is { } t) q = q.Where(o => o.CreatedAtUtc <= t);
        if (!string.IsNullOrWhiteSpace(query))
            q = q.Where(o => o.OrderNumber.ToLower().Contains(query.ToLower())
                || o.Customer!.Nama.ToLower().Contains(query.ToLower())
                || o.Customer!.User!.NormalizedPhone.Contains(query));
        pageSize = Math.Clamp(pageSize, 1, 100);
        return await q.OrderByDescending(o => o.CreatedAtUtc)
            .Skip(page * pageSize).Take(pageSize)
            .Select(o => new OrderListItem(o.Id, o.OrderNumber, o.Customer!.Nama, o.Status, o.PaymentStatus,
                o.EstimatedTotal, o.FinalTotal, o.CreatedAtUtc, o.EstimatedFinishUtc, o.Source))
            .ToListAsync(ct);
    }

    public async Task<AdminDashboardDto> AdminDashboardAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var baru = await db.Orders.CountAsync(o => o.Status == OrderStatus.MenungguKonfirmasi, ct);
        var siap = await db.Orders.CountAsync(o => o.Status == OrderStatus.SiapDiambil
            || o.Status == OrderStatus.DalamPengantaran, ct);
        var proses = await db.Orders.CountAsync(o => OrderStatusAlur.TahapDikerjakan.Contains(o.Status), ct);
        var belumLunas = await db.Orders.CountAsync(o => o.Status != OrderStatus.Selesai
            && o.Status != OrderStatus.Dibatalkan && o.PaymentStatus != PaymentStatus.Lunas, ct);

        var omzet = await db.Payments.Where(p => p.Status == PaymentStatus.Lunas
                && p.CreatedAtUtc >= today && p.CreatedAtUtc < tomorrow)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var kas = await db.Payments.Where(p => p.Status == PaymentStatus.Lunas && p.Method == PaymentMethod.Tunai
                && p.CreatedAtUtc >= today && p.CreatedAtUtc < tomorrow)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var refundedHariIni = await db.Refunds.Where(r => r.Status == PaymentStatus.Lunas
                && r.CreatedAtUtc >= today && r.CreatedAtUtc < tomorrow)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        var kasRefund = await db.Refunds.Where(r => r.Status == PaymentStatus.Lunas
                && r.OriginPayment!.Method == PaymentMethod.Tunai
                && r.CreatedAtUtc >= today && r.CreatedAtUtc < tomorrow)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
        var aktif = await db.Orders.Where(o => o.Status != OrderStatus.Selesai && o.Status != OrderStatus.Dibatalkan)
            .Select(o => new { o.FinalTotal, o.AppliedCredit, Paid = o.Payments
                .Where(p => p.Status == PaymentStatus.Lunas).Sum(p => (decimal?)p.Amount) ?? 0m,
                Refunded = o.Refunds.Where(r => r.Status == PaymentStatus.Lunas)
                    .Sum(r => (decimal?)r.Amount) ?? 0m })
            .ToListAsync(ct);
        var antrian = await db.SyncOutbox.CountAsync(s => s.Status == SyncEventStatus.Menunggu, ct);
        var gagal = await db.SyncOutbox.CountAsync(s => s.Status == SyncEventStatus.Gagal, ct);
        var sinkron = await db.SyncCheckpoints.Select(c => (DateTime?)c.LastSuccessAtUtc).ToListAsync(ct);
        var komplain = await db.Complaints.CountAsync(c => c.Status != ComplaintStatus.Selesai
            && c.Status != ComplaintStatus.Ditolak, ct);
        var penjemputan = await db.FulfillmentTasks.CountAsync(t => t.IsPickup && t.Status == "Terjadwal", ct);
        var pengantaran = await db.FulfillmentTasks.CountAsync(t => !t.IsPickup && t.Status == "Terjadwal", ct);

        return new AdminDashboardDto(baru, proses, siap, belumLunas,
            komplain, penjemputan, pengantaran,
            omzet - refundedHariIni, kas - kasRefund,
            aktif.Sum(p => Math.Max(p.FinalTotal - (p.Paid - p.Refunded) - p.AppliedCredit, 0m)),
            antrian, gagal, sinkron.Where(x => x is not null).Max());
    }

    public async Task<CustomerDashboardDto> CustomerDashboardAsync(Guid customerId, CancellationToken ct)
    {
        var active = await db.Orders
            .Where(o => o.CustomerId == customerId && o.Status != OrderStatus.Selesai
                && o.Status != OrderStatus.Dibatalkan)
            .OrderByDescending(o => o.CreatedAtUtc).Take(50)
            .Select(o => new { o.Id, o.OrderNumber, o.Status, o.PaymentStatus, o.EstimatedTotal,
                o.FinalTotal, o.AppliedCredit, o.CreatedAtUtc, o.EstimatedFinishUtc, o.Source,
                Nama = o.Customer!.Nama,
                Paid = (o.Payments.Where(p => p.Status == PaymentStatus.Lunas).Sum(p => (decimal?)p.Amount) ?? 0m)
                       - (o.Refunds.Where(r => r.Status == PaymentStatus.Lunas).Sum(r => (decimal?)r.Amount) ?? 0m) })
            .ToListAsync(ct);
        var saldo = BillCalculator.CreditBalance(
            await db.CreditLedger.Where(e => e.CustomerId == customerId).ToListAsync(ct), customerId);
        var tagihan = active.Sum(o => Math.Max(o.FinalTotal - o.Paid - o.AppliedCredit, 0m));
        return new CustomerDashboardDto(
            active.Select(o => new OrderListItem(o.Id, o.OrderNumber, o.Nama, o.Status, o.PaymentStatus,
                o.EstimatedTotal, o.FinalTotal, o.CreatedAtUtc, o.EstimatedFinishUtc, o.Source)).ToList(),
            saldo, tagihan);
    }

    private static object Shallow(Order o) => new
    {
        o.Id, o.OrderNumber, o.CustomerId, o.Status, o.PaymentStatus, o.EstimatedTotal,
        o.FinalTotal, o.ActualWeightKg, o.ActualItemCount, o.Express, o.DeliveryFee,
        o.AppliedCredit, o.Source, o.CancelRequestedAtUtc, o.IsCancelled,
        Items = o.Items.Select(i => new
        {
            i.Id, i.ServiceId, i.PriceSnapshot, i.EstimatedWeightKg, i.ActualWeightKg,
            i.EstimatedItemCount, i.ActualItemCount, i.Subtotal,
        }),
    };

    private static OrderDetailDto Map(Order o)
    {
        decimal refunded = BillCalculator.RefundedPayments(o.Refunds, o.Id);
        decimal paid = BillCalculator.Paid(o, o.Refunds);
        return new OrderDetailDto(o.Id, o.OrderNumber, o.CustomerId, o.Customer?.Nama ?? "",
            o.Status, o.PaymentStatus, o.Express, o.Fulfillment,
            o.FulfillmentTasks?.FirstOrDefault(t => t.IsPickup)?.Address ?? o.Customer?.Alamat,
            o.EstimatedTotal, o.ActualWeightKg, o.ActualItemCount, o.FinalTotal, paid,
            Math.Max(o.FinalTotal - paid, 0m), o.AppliedCredit, refunded, o.DeliveryFee,
            o.CreatedAtUtc, o.EstimatedFinishUtc, o.Source,
            o.Items.Select(i => new OrderItemDto(i.Id, i.ServiceNameSnapshot, i.UnitTypeSnapshot,
                i.PriceSnapshot, i.EstimatedWeightKg, i.EstimatedItemCount, i.ActualWeightKg,
                i.ActualItemCount, i.Subtotal)).ToList(),
            o.StatusHistory.Select(h => new StatusHistoryDto(h.FromStatus, h.ToStatus, h.ActorUserId,
                h.AtUtc, h.Note)).OrderByDescending(h => h.AtUtc).ToList());
    }
}
