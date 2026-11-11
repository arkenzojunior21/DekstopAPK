using Laundry.Domain;

namespace Laundry.Application.Core;

public interface ICatalogService
{
    Task<List<ServiceDto>> ListAsync(bool activeOnly, CancellationToken ct);
    Task<ServiceDto> UpsertAsync(UpsertServiceRequest req, CancellationToken ct);
    Task<decimal> GetDeliveryFeeAsync(CancellationToken ct);
    Task SetDeliveryFeeAsync(decimal fee, CancellationToken ct);
}

public interface ICustomerService
{
    Task<CustomerDto> GetOrCreateAsync(Guid? customerId, string? nama, string? phone, string? alamat,
        CancellationToken ct);
    Task<List<CustomerDto>> SearchAsync(string? query, int page, int pageSize, CancellationToken ct);
    Task<CustomerDto> UpdateAsync(Guid customerId, UpdateCustomerRequest req, CancellationToken ct);
    Task<CustomerDto> SetActiveAsync(Guid customerId, bool aktif, CancellationToken ct);
}

public interface IOrderService
{
    /// <summary>Admin membuat pesanan walk-in (source lokal) atau pesanan atas nama pelanggan.</summary>
    Task<OrderDetailDto> CreateWalkInAsync(CreateOrderRequest req, Guid actorUserId, CancellationToken ct);

    /// <summary>Pelanggan membuat pesanan online lewat cloud (source cloud).</summary>
    Task<OrderDetailDto> CreateOnlineAsync(CreateOrderRequest req, Guid customerId, CancellationToken ct);

    Task<OrderDetailDto> TransitionAsync(Guid orderId, OrderStatus to, Guid actorUserId, string? note, CancellationToken ct);

    Task<OrderDetailDto> RecordActualsAsync(Guid orderId, List<ActualItemInput> actuals, Guid actorUserId, CancellationToken ct);

    Task<OrderDetailDto> UpdateEstimateAsync(Guid orderId, DateTime? estimatedFinishUtc, string? note, Guid actorUserId, CancellationToken ct);

    /// <summary>Pelanggan mengajukan pembatalan sebelum diproses; admin yang memutuskan.</summary>
    Task<OrderDetailDto> RequestCancellationAsync(Guid orderId, Guid customerId, string reason, CancellationToken ct);
    Task<OrderDetailDto> DecideCancellationAsync(Guid orderId, bool approve, Guid actorUserId, string? note, CancellationToken ct);

    Task<OrderDetailDto> GetAsync(Guid orderId, Guid? limitToCustomerId, CancellationToken ct);

    /// <summary>Isi struk: snapshot pesanan + pembayaran, siap dirender PDF oleh UI.</summary>
    Task<ReceiptDto> GetReceiptAsync(Guid orderId, Guid? limitToCustomerId, CancellationToken ct);
    Task<List<OrderListItem>> SearchAsync(string? query, OrderStatus? status, PaymentStatus? payment,
        Guid? customerId, DateTime? fromUtc, DateTime? toUtc, int page, int pageSize, CancellationToken ct);

    Task<AdminDashboardDto> AdminDashboardAsync(CancellationToken ct);
    Task<CustomerDashboardDto> CustomerDashboardAsync(Guid customerId, CancellationToken ct);
}

public interface IPaymentService
{
    /// <summary>Pembayaran tunai dicatat admin; wajib actor+waktu+nominal+metode (PRD 6.9).</summary>
    Task<PaymentDto> RecordCashAsync(RecordCashPaymentRequest req, Guid actorUserId, CancellationToken ct);

    /// <summary>Pemakaian saldo kredit HANYA dengan persetujuan eksplisit pelanggan.</summary>
    Task<OrderDetailDto> ApplyCreditAsync(ApplyCreditRequest req, Guid actorUserId, CancellationToken ct);

    /// <summary>Pengembalian dana tunai di kasir: append-only, wajib alasan + idempoten.</summary>
    Task<RefundDto> RefundAsync(RefundRequest req, Guid actorUserId, CancellationToken ct);

    Task<List<PaymentDto>> ListForOrderAsync(Guid orderId, Guid? limitToCustomerId, CancellationToken ct);
    Task<decimal> CreditBalanceAsync(Guid customerId, CancellationToken ct);
}

public interface IComplaintService
{
    Task<ComplaintDto> CreateAsync(CreateComplaintRequest req, Guid customerId, CancellationToken ct);
    Task<ComplaintDto> ReplyAsync(Guid complaintId, string body, Guid senderUserId, bool isAdmin, CancellationToken ct);
    Task<ComplaintDto> SetStatusAsync(Guid complaintId, ComplaintStatus status, Guid actorUserId, CancellationToken ct);
    Task<List<ComplaintDto>> ListAsync(Guid? customerId, ComplaintStatus? status, CancellationToken ct);
}

public interface ISlotService
{
    Task<List<SlotDto>> ListAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    Task<SlotDto> CreateAsync(CreateSlotRequest req, CancellationToken ct);
}
