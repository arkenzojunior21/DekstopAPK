using System.Text;
using Laundry.Application.Abstractions;
using Laundry.Application.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laundry.Api.Controllers;

/// <summary>Tagihan non-tunai lewat adapter gateway (hanya admin yang memicu).</summary>
[ApiController]
[Route("api/payments/gateway")]
[Authorize(Roles = "Admin")]
public class GatewayPaymentsController(IPaymentGatewayService gateway, ICurrentUser me) : ControllerBase
{
    [HttpPost("quote")]
    public Task<PaymentQuoteDto> Quote([FromBody] CreateGatewayPaymentRequest req, CancellationToken ct) =>
        gateway.CreateQuoteAsync(req, me.UserId!.Value, ct);
}

/// <summary>
/// Titik masuk webhook provider. Anonim karena dipanggil sistem eksternal, tetapi body mentah
/// wajib cocok dengan HMAC adapter — tanpa secret yang benar tidak ada pembayaran yang berubah.
/// Kontrak lokal: header X-Mock-Signature berisi sha256=&lt;hmac atas body&gt;.
/// </summary>
[ApiController]
[Route("api/payments/gateway")]
public class GatewayWebhookController(IPaymentGateway gateway, IPaymentGatewayService service) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();

        if (!gateway.VerifySignature(body, Request.Headers[gateway.SignatureHeader]))
            return Unauthorized(new { error = "Tanda tangan webhook tidak valid." });

        var update = gateway.ParseUpdate(body);
        if (update is null) return BadRequest(new { error = "Isi webhook tidak dikenali." });

        var hasil = await service.HandleWebhookAsync(update, ct);
        var isi = new
        {
            status = hasil.Status.ToString(),
            paymentId = hasil.PaymentId,
            catatan = hasil.Note,
        };
        return hasil.Status == GatewayWebhookStatus.Ditolak
            ? UnprocessableEntity(isi)
            : Ok(isi);
    }
}

/// <summary>Kondisi adapter provider + antrean notifikasi, dipakai indikator UI dan pemeriksaan admin.</summary>
[ApiController]
[Route("api/providers")]
[Authorize(Roles = "Admin")]
public class ProvidersController(
    INotificationDispatcher dispatcher,
    IWhatsAppSender whatsapp,
    IPaymentGateway gateway) : ControllerBase
{
    private static string Catatan(bool live, string jenis) => live
        ? $"Adapter {jenis} aktif."
        : $"Simulasi {jenis}: tidak ada interaksi sungguhan dengan pelanggan maupun provider.";

    [HttpGet("status")]
    public async Task<ProviderStatusDto> Status(CancellationToken ct)
    {
        var antrean = await dispatcher.QueueStatusAsync(10, ct);
        var adapter = new List<AdapterInfo>
        {
            new(whatsapp.Name, "whatsapp", whatsapp.IsLive, Catatan(whatsapp.IsLive, "WhatsApp")),
            new(gateway.Name, "payment-gateway", gateway.IsLive, Catatan(gateway.IsLive, "payment gateway")),
        };
        return new ProviderStatusDto(antrean, adapter);
    }

    /// <summary>Coba kirim sekarang juga; tanpa panggilan ini antrean jalan otomatis lewat pekerja.</summary>
    [HttpPost("notifications/drain")]
    public Task<NotificationDrainResult> Drain(CancellationToken ct) => dispatcher.DrainAsync(ct);
}
