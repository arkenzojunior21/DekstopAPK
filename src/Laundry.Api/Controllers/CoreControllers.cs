using Laundry.Application.Abstractions;
using Laundry.Application.Core;
using Laundry.Domain;
using Laundry.Infrastructure.Persistence;
using Laundry.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Laundry.Api.Controllers;

public record TransitionRequest(OrderStatus To, string? Note);
public record ActualsRequest(List<ActualItemInput> Actuals);
public record EstimateRequest(DateTime? EstimatedFinishUtc, string? Note);
public record CancelRequest(string Reason);
public record DecideRequest(bool Approve, string? Note);
public record ComplaintReply(string Body);
public record ComplaintStatusRequest(ComplaintStatus Status);
public record DeliveryFeeRequest(decimal Fee);
public record BusinessNameRequest(string Nama);

[ApiController]
[Route("api/catalog")]
public class CatalogController(ICatalogService catalog) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<List<ServiceDto>> List([FromQuery] bool activeOnly = true) => catalog.ListAsync(activeOnly, HttpContext.RequestAborted);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public Task<ServiceDto> Upsert([FromBody] UpsertServiceRequest req) => catalog.UpsertAsync(req, HttpContext.RequestAborted);

    [HttpGet("delivery-fee")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Fee() => Ok(new { fee = await catalog.GetDeliveryFeeAsync(HttpContext.RequestAborted) });

    [HttpPut("delivery-fee")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetFee([FromBody] DeliveryFeeRequest req)
    {
        await catalog.SetDeliveryFeeAsync(req.Fee, HttpContext.RequestAborted);
        return NoContent();
    }
}

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(SettingsStore settings) : ControllerBase
{
    [HttpGet("business-name")]
    public async Task<IActionResult> GetNama() => Ok(new
    {
        nama = await settings.GetStringAsync(OrderService.BusinessNameKey, HttpContext.RequestAborted),
    });

    [HttpPut("business-name")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetNama([FromBody] BusinessNameRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Nama)) throw new InvalidOperationException("Nama usaha wajib diisi.");
        await settings.SetStringAsync(OrderService.BusinessNameKey, req.Nama.Trim(), HttpContext.RequestAborted);
        return NoContent();
    }
}

[ApiController]
[Route("api/slots")]
[Authorize]
public class SlotsController(ISlotService slots) : ControllerBase{
    /// <summary>Jadwal slot boleh dibaca pelanggan karena pemesanan antar-jemput harus memilih salah
    /// satunya; yang tetap khusus admin hanya membuat slot.</summary>
    [HttpGet]
    public async Task<List<SlotDto>> List([FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
        await slots.ListAsync(from ?? DateTime.UtcNow, to ?? DateTime.UtcNow.AddDays(14), HttpContext.RequestAborted);

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public Task<SlotDto> Create([FromBody] CreateSlotRequest req) => slots.CreateAsync(req, HttpContext.RequestAborted);
}

[ApiController]
[Route("api/customers")]
[Authorize(Roles = "Admin")]
public class CustomersController(ICustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<List<CustomerDto>> Search([FromQuery] string? q, [FromQuery] int page = 0,
        [FromQuery] int pageSize = 20) => customers.SearchAsync(q, page, pageSize, HttpContext.RequestAborted);

    [HttpGet("{id:guid}")]
    public Task<CustomerDto> Get(Guid id) => customers.GetOrCreateAsync(id, null, null, null, HttpContext.RequestAborted);

    [HttpPost]
    public Task<CustomerDto> Create([FromBody] CreateCustomerRequest req) =>
        customers.GetOrCreateAsync(null, req.Nama, req.Phone, req.Alamat, HttpContext.RequestAborted);

    [HttpPut("{id:guid}")]
    public Task<CustomerDto> Update(Guid id, [FromBody] UpdateCustomerRequest req) =>
        customers.UpdateAsync(id, req, HttpContext.RequestAborted);

    [HttpPut("{id:guid}/active")]
    public Task<CustomerDto> SetActive(Guid id, [FromQuery] bool aktif) =>
        customers.SetActiveAsync(id, aktif, HttpContext.RequestAborted);
}

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController(
    IOrderService orders,
    IPaymentService payments,
    ICurrentUser me,
    LaundryDbContext db) : ControllerBase
{
    private async Task<Guid?> CustomerScopeAsync()
    {
        if (me.IsAdmin) return null;
        // Pelanggan dibatasi ke profile miliknya; kalau bukan pelanggan, 403.
        var userId = me.UserId ?? throw new UnauthorizedAccessException();
        return await db.CustomerProfiles.Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(HttpContext.RequestAborted)
            ?? throw new UnauthorizedAccessException("Akun pelanggan belum lengkap.");
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<OrderDetailDto> CreateWalkIn([FromBody] CreateOrderRequest req) =>
        await orders.CreateWalkInAsync(req, me.UserId!.Value, HttpContext.RequestAborted);

    [HttpPost("online")]
    [Authorize(Roles = "Pelanggan")]
    public async Task<OrderDetailDto> CreateOnline([FromBody] CreateOrderRequest req)
    {
        var scope = await CustomerScopeAsync();
        return await orders.CreateOnlineAsync(req, scope!.Value, HttpContext.RequestAborted);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public Task<List<OrderListItem>> Search([FromQuery] string? q, [FromQuery] OrderStatus? status,
        [FromQuery] PaymentStatus? payment, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 0, [FromQuery] int pageSize = 20) =>
        orders.SearchAsync(q, status, payment, null, from, to, page, pageSize, HttpContext.RequestAborted);

    [HttpGet("mine")]
    public async Task<List<OrderListItem>> Mine([FromQuery] int page = 0, [FromQuery] int pageSize = 20)
    {
        var profileId = await CustomerScopeAsync()
            ?? await ResolveProfileAsync(me.UserId ?? Guid.Empty);
        return await orders.SearchAsync(null, null, null, profileId, null, null, page, pageSize,
            HttpContext.RequestAborted);
    }

    private async Task<Guid?> ResolveProfileAsync(Guid userId) =>
        await db.CustomerProfiles.Where(p => p.UserId == userId).Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

    [HttpGet("dashboard/admin")]
    [Authorize(Roles = "Admin")]
    public Task<AdminDashboardDto> AdminDashboard() => orders.AdminDashboardAsync(HttpContext.RequestAborted);

    [HttpGet("dashboard/customer")]
    public async Task<CustomerDashboardDto> CustomerDashboard()
    {
        var scope = await CustomerScopeAsync();
        return await orders.CustomerDashboardAsync(scope!.Value, HttpContext.RequestAborted);
    }

    [HttpGet("{id:guid}")]
    public async Task<OrderDetailDto> Get(Guid id) =>
        await orders.GetAsync(id, await CustomerScopeAsync(), HttpContext.RequestAborted);

    [HttpGet("{id:guid}/receipt")]
    public async Task<ReceiptDto> Receipt(Guid id) =>
        await orders.GetReceiptAsync(id, await CustomerScopeAsync(), HttpContext.RequestAborted);

    [HttpPost("{id:guid}/transition")]
    [Authorize(Roles = "Admin")]
    public Task<OrderDetailDto> Transition(Guid id, [FromBody] TransitionRequest req) =>
        orders.TransitionAsync(id, req.To, me.UserId!.Value, req.Note, HttpContext.RequestAborted);

    [HttpPost("{id:guid}/actuals")]
    [Authorize(Roles = "Admin")]
    public Task<OrderDetailDto> Actuals(Guid id, [FromBody] ActualsRequest req) =>
        orders.RecordActualsAsync(id, req.Actuals, me.UserId!.Value, HttpContext.RequestAborted);

    [HttpPost("{id:guid}/estimate")]
    [Authorize(Roles = "Admin")]
    public Task<OrderDetailDto> Estimate(Guid id, [FromBody] EstimateRequest req) =>
        orders.UpdateEstimateAsync(id, req.EstimatedFinishUtc, req.Note, me.UserId!.Value, HttpContext.RequestAborted);

    [HttpPost("{id:guid}/cancel-request")]
    [Authorize(Roles = "Pelanggan")]
    public async Task<OrderDetailDto> CancelRequest(Guid id, [FromBody] CancelRequest req)
    {
        var scope = await CustomerScopeAsync();
        return await orders.RequestCancellationAsync(id, scope!.Value, req.Reason, HttpContext.RequestAborted);
    }

    [HttpPost("{id:guid}/cancel-decision")]
    [Authorize(Roles = "Admin")]
    public Task<OrderDetailDto> CancelDecision(Guid id, [FromBody] DecideRequest req) =>
        orders.DecideCancellationAsync(id, req.Approve, me.UserId!.Value, req.Note, HttpContext.RequestAborted);

    [HttpGet("{id:guid}/payments")]
    public async Task<List<PaymentDto>> Payments(Guid id) =>
        await payments.ListForOrderAsync(id, await CustomerScopeAsync(), HttpContext.RequestAborted);
}

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController(IPaymentService payments, ICurrentUser me) : ControllerBase
{
    [HttpPost("cash")]
    [Authorize(Roles = "Admin")]
    public Task<PaymentDto> Cash([FromBody] RecordCashPaymentRequest req) =>
        payments.RecordCashAsync(req, me.UserId!.Value, HttpContext.RequestAborted);

    [HttpPost("refund")]
    [Authorize(Roles = "Admin")]
    public Task<RefundDto> Refund([FromBody] RefundRequest req) =>
        payments.RefundAsync(req, me.UserId!.Value, HttpContext.RequestAborted);

    [HttpPost("credit")]
    [Authorize(Roles = "Admin")]
    public async Task<OrderDetailDto> Credit([FromBody] ApplyCreditRequest req)
    {
        // Persetujuan pelanggan dieksplisitkan di payload; admin hanya mencatat,
        // flag CustomerConsent divalidasi service.
        return await payments.ApplyCreditAsync(req, me.UserId!.Value, HttpContext.RequestAborted);
    }

    [HttpGet("credit-balance")]
    public async Task<IActionResult> Balance([FromQuery] Guid? customerId = null)
    {
        if (customerId is null && !me.IsAdmin) return Forbid();
        var id = customerId ?? Guid.Empty;
        return Ok(new { balance = await payments.CreditBalanceAsync(id, HttpContext.RequestAborted) });
    }
}

[ApiController]
[Route("api/complaints")]
[Authorize]
public class ComplaintsController(IComplaintService complaints, ICurrentUser me) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Pelanggan")]
    public async Task<ComplaintDto> Create([FromBody] CreateComplaintRequest req)
    {
        // Komplain tertaut customer profile — resolusikan lewat order milik pelanggan di service.
        return await complaints.CreateAsync(req, await ProfileIdAsync(), HttpContext.RequestAborted);
    }

    private async Task<Guid> ProfileIdAsync()
    {
        var db = HttpContext.RequestServices.GetRequiredService<LaundryDbContext>();
        return await db.CustomerProfiles.Where(p => p.UserId == me.UserId).Select(p => p.Id)
            .FirstAsync(HttpContext.RequestAborted);
    }

    [HttpGet]
    public async Task<List<ComplaintDto>> List([FromQuery] ComplaintStatus? status)
    {
        var scope = me.IsAdmin ? (Guid?)null : await ProfileIdAsync();
        return await complaints.ListAsync(scope, status, HttpContext.RequestAborted);
    }

    [HttpPost("{id:guid}/reply")]
    public async Task<ComplaintDto> Reply(Guid id, [FromBody] ComplaintReply req)
    {
        var profile = me.IsAdmin ? (Guid?)null : await ProfileIdAsync();
        if (profile is { } p)
        {
            var all = await complaints.ListAsync(p, null, HttpContext.RequestAborted);
            if (!all.Any(c => c.Id == id)) throw new KeyNotFoundException("Komplain tidak ditemukan.");
        }
        return await complaints.ReplyAsync(id, req.Body, me.UserId!.Value, me.IsAdmin, HttpContext.RequestAborted);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "Admin")]
    public Task<ComplaintDto> Status(Guid id, [FromBody] ComplaintStatusRequest req) =>
        complaints.SetStatusAsync(id, req.Status, me.UserId!.Value, HttpContext.RequestAborted);
}
